using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mapperize.Generator;

/// <summary>Generates the implementation of one mapper class.</summary>
internal sealed class Emitter
{
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private static readonly HashSet<string> KnownLeafTypes = new()
    {
        "System.String", "System.Decimal", "System.DateTime", "System.DateTimeOffset",
        "System.TimeSpan", "System.Guid", "System.Uri", "System.Version",
        "System.DateOnly", "System.TimeOnly", "System.Object",
    };

    private readonly Compilation _compilation;
    private readonly CSharpCompilation? _csharp;
    private readonly INamedTypeSymbol _mapper;
    private readonly Options _options;

    private readonly Dictionary<string, string> _mapMethods = new();   // signature -> method name
    private readonly Dictionary<string, string> _converters = new();   // signature -> user converter method
    private readonly Queue<MapJob> _pending = new();
    private readonly HashSet<string> _emitted = new();
    private readonly List<Diagnostic> _diagnostics = new();
    private int _helperCounter;

    /// <summary>
    /// When <c>true</c>, generated helper methods are emitted <c>static</c> so they can be called
    /// from static (and extension) mapping methods. Instance methods can also call static helpers,
    /// so this is safe whenever any user method — or the mapper type itself — is static.
    /// </summary>
    private bool _staticHelpers;

    /// <summary>The simple name of the interface to generate (e.g. <c>IUserMapper</c>), or null.</summary>
    private string? _interfaceName;

    /// <summary>The public instance methods that make up the generated interface.</summary>
    private List<UserMethod> _interfaceMethods = new();

    public Emitter(Compilation compilation, INamedTypeSymbol mapper, AttributeData mapperAttribute)
    {
        _compilation = compilation;
        _csharp = compilation as CSharpCompilation;
        _mapper = mapper;
        _options = Options.Read(mapperAttribute);
    }

    public MapperResult Emit(CancellationToken ct)
    {
        var hint = _mapper.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_') + ".g.cs";

        // The mapper type itself, and every type it is nested inside, must be non-generic and
        // declared `partial` so we can add the implementation as another partial part.
        if (!IsSupportedMapperType(_mapper, out var unsupportedReason))
        {
            _diagnostics.Add(Diagnostic.Create(Diagnostics.GenericMapperUnsupported,
                _mapper.Locations.FirstOrDefault(), unsupportedReason));
            return new MapperResult(hint, null, _diagnostics.ToImmutableArray(), null);
        }

        // 1. Discover user-declared partial mapping methods and register them as reusable maps.
        //    Ordinary (non-partial) methods that take one argument and return a value are collected
        //    separately as user-defined value converters (see CollectConverters).
        var userMethods = new List<UserMethod>();
        foreach (var member in _mapper.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary || !member.IsPartialDefinition)
                continue;
            if (member.PartialImplementationPart is not null)
                continue;

            if (!TryClassify(member, out var kind))
            {
                _diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMapperMethod,
                    member.Locations.FirstOrDefault(), member.Name));
                continue;
            }

            var meta = UserMethod.Read(member, kind);
            userMethods.Add(meta);

            // Only transform methods (source -> new target) can stand in for an auto-generated
            // nested map; update methods populate an existing instance and are not composable.
            if (kind == MapKind.Transform)
                _mapMethods[Key(meta.SourceType, meta.TargetType)] = member.Name;
        }

        if (userMethods.Count == 0)
            return new MapperResult(hint, null, _diagnostics.ToImmutableArray(), null);

        _staticHelpers = _mapper.IsStatic || userMethods.Any(m => m.IsStatic);

        // Custom value converters: any ordinary, fully-implemented `TTarget Method(TSource)` the user
        // writes is used wherever a `TSource -> TTarget` conversion is needed (e.g. `string Format(
        // DateTime)`). Collected after `_staticHelpers` is known so we can skip instance converters
        // that a generated static helper could not legally call.
        CollectConverters();

        // An interface (for injection / mocking) is generated only for a non-static, top-level
        // mapper that opted in and has at least one public instance mapping method.
        if (_options.GenerateInterface && !_mapper.IsStatic && _mapper.ContainingType is null)
        {
            _interfaceMethods = userMethods
                .Where(m => !m.IsStatic && !m.IsExtension
                            && m.Accessibility == Microsoft.CodeAnalysis.Accessibility.Public)
                .ToList();
            if (_interfaceMethods.Count > 0)
                _interfaceName = "I" + _mapper.Name;
        }

        foreach (var m in userMethods)
            _pending.Enqueue(new MapJob(m.SourceType, m.TargetType, m.Name, m));

        // 2. Drain the work queue, generating each method body (which may enqueue nested helpers).
        var bodies = new StringBuilder();
        while (_pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var job = _pending.Dequeue();
            // The kind discriminator keeps a transform and an update that share a name and type
            // pair (e.g. UserDto ToDto(User) and UserDto ToDto(User, UserDto)) from colliding.
            var key = Key(job.Source, job.Target) + "#" + job.MethodName + "#"
                      + (job.User?.Kind == MapKind.Update ? "u" : "t");
            if (!_emitted.Add(key))
                continue;
            EmitMethod(bodies, job);
        }

        var source = Wrap(bodies.ToString());

        // A non-static mapper can be registered with a DI container. Static classes cannot.
        MapperRegistration? registration = null;
        if (!_mapper.IsStatic)
        {
            string? interfaceType = null;
            if (_interfaceName is not null)
            {
                var ns = _mapper.ContainingNamespace;
                var prefix = ns is { IsGlobalNamespace: false } ? ns.ToDisplayString() + "." : string.Empty;
                interfaceType = "global::" + prefix + _interfaceName;
            }
            registration = new MapperRegistration(Display(_mapper), interfaceType);
        }

        return new MapperResult(hint, source, _diagnostics.ToImmutableArray(), registration);
    }

    /// <summary>
    /// The mapper type and each of its containing types must be non-generic and declared
    /// <c>partial</c>, otherwise the generated partial part cannot be attached.
    /// </summary>
    private static bool IsSupportedMapperType(INamedTypeSymbol mapper, out string reason)
    {
        for (INamedTypeSymbol? t = mapper; t is not null; t = t.ContainingType)
        {
            if (t.IsGenericType)
            {
                reason = t.Name;
                return false;
            }
            if (!IsDeclaredPartial(t))
            {
                reason = t.Name;
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static bool IsDeclaredPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax decl
                && decl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Classifies a candidate partial method into a supported <see cref="MapKind"/>, or returns
    /// <c>false</c> if its signature is not a valid mapping method.
    /// </summary>
    private static bool TryClassify(IMethodSymbol method, out MapKind kind)
    {
        kind = MapKind.Transform;
        if (method.IsGenericMethod)
            return false;

        // Transform: one source parameter, returns the (non-void) target.
        if (method.Parameters.Length == 1 && !method.ReturnsVoid)
        {
            kind = MapKind.Transform;
            return true;
        }

        // Update: source parameter plus an existing target that is populated in place. The method
        // may return void or return the target type (a fluent "populate and return" convenience).
        if (method.Parameters.Length == 2)
        {
            var targetParam = method.Parameters[1].Type;
            if (method.ReturnsVoid ||
                SymbolEqualityComparer.Default.Equals(method.ReturnType, targetParam))
            {
                kind = MapKind.Update;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Registers user-written value converters: ordinary, fully-implemented methods of the shape
    /// <c>TTarget Method(TSource)</c> declared on the mapper. These are consulted by
    /// <see cref="Convert"/> for the exact source/target pair before any built-in conversion, so a
    /// user can override or supply a conversion the generator does not know how to synthesize.
    /// </summary>
    private void CollectConverters()
    {
        foreach (var member in _mapper.GetMembers().OfType<IMethodSymbol>())
        {
            if (member.MethodKind != MethodKind.Ordinary || member.IsGenericMethod)
                continue;
            // A partial mapping method (transform/update) is handled elsewhere; only ordinary,
            // user-bodied methods act as converters.
            if (member.IsPartialDefinition || member.PartialImplementationPart is not null)
                continue;
            if (member.Parameters.Length != 1 || member.ReturnsVoid || member.ReturnType.SpecialType == SpecialType.System_Void)
                continue;
            // A converter used from a generated static helper must itself be static.
            if (_staticHelpers && !member.IsStatic)
                continue;

            var key = Key(member.Parameters[0].Type, member.ReturnType);
            // First declaration wins; keeps behaviour deterministic if two converters collide.
            if (!_converters.ContainsKey(key))
                _converters[key] = member.Name;
        }
    }

    private string Wrap(string bodies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
        sb.AppendLine("#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604");
        sb.AppendLine();

        var ns = _mapper.ContainingNamespace;
        var hasNs = ns is { IsGlobalNamespace: false };
        var indent = 0;
        if (hasNs)
        {
            sb.Append("namespace ").Append(ns!.ToDisplayString()).AppendLine();
            sb.AppendLine("{");
            indent = 1;
        }

        // Re-open each containing type (outermost first) so a nested mapper is legal.
        var nesting = new List<INamedTypeSymbol>();
        for (var t = _mapper.ContainingType; t is not null; t = t.ContainingType)
            nesting.Add(t);
        nesting.Reverse();

        var pad = new string(' ', indent * 4);
        foreach (var outer in nesting)
        {
            sb.Append(pad).Append(TypeHeader(outer)).AppendLine();
            sb.Append(pad).AppendLine("{");
            indent++;
            pad = new string(' ', indent * 4);
        }

        // Emit the injectable interface (only generated for a non-nested mapper) alongside the class.
        if (_interfaceName is not null)
            EmitInterface(sb, pad);

        sb.Append(pad).Append(TypeHeader(_mapper));
        if (_interfaceName is not null)
            sb.Append(" : ").Append(_interfaceName);
        sb.AppendLine();
        sb.Append(pad).AppendLine("{");
        sb.Append(bodies);
        sb.Append(pad).AppendLine("}");

        for (var i = 0; i < nesting.Count; i++)
        {
            indent--;
            pad = new string(' ', indent * 4);
            sb.Append(pad).AppendLine("}");
        }

        if (hasNs)
            sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Emits the injectable interface (<c>I{MapperName}</c>) the mapper implements.</summary>
    private void EmitInterface(StringBuilder sb, string pad)
    {
        sb.Append(pad).Append(Accessibility(_mapper.DeclaredAccessibility)).Append(" interface ")
          .Append(_interfaceName).AppendLine();
        sb.Append(pad).AppendLine("{");
        foreach (var m in _interfaceMethods)
        {
            sb.Append(pad).Append("    ").Append(Display(m.ReturnType)).Append(' ').Append(m.Name).Append('(')
              .Append(Display(m.SourceType)).Append(' ').Append(m.ParameterName);
            if (m.Kind == MapKind.Update)
                sb.Append(", ").Append(Display(m.TargetType)).Append(' ').Append(m.TargetParameterName);
            sb.AppendLine(");");
        }
        sb.Append(pad).AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>Emits e.g. <c>public static partial class Foo</c> for a (possibly enclosing) type.</summary>
    private static string TypeHeader(INamedTypeSymbol type)
    {
        var sb = new StringBuilder();
        sb.Append(Accessibility(type.DeclaredAccessibility)).Append(' ');
        if (type.IsStatic)
            sb.Append("static ");
        sb.Append("partial ").Append(TypeKeyword(type)).Append(' ').Append(type.Name);
        return sb.ToString();
    }

    private void EmitMethod(StringBuilder sb, MapJob job)
    {
        if (job.User is { Kind: MapKind.Update } upd)
        {
            EmitUpdateMethod(sb, job, upd);
            return;
        }

        var srcType = job.Source;
        var tgtType = job.Target;
        var srcDisplay = Display(srcType);
        var tgtDisplay = Display(tgtType);
        const string p = "source";
        var paramName = job.User?.ParameterName ?? p;

        // signature
        sb.AppendLine();
        if (job.User is { } um)
            sb.Append("        ").Append(SignaturePrefix(um.Accessibility, um.IsStatic))
              .Append(Display(um.ReturnType)).Append(' ').Append(job.MethodName)
              .Append('(').Append(um.IsExtension ? "this " : string.Empty)
              .Append(Display(um.SourceType)).Append(' ').Append(paramName).AppendLine(")");
        else
            sb.Append("        private ").Append(_staticHelpers ? "static " : string.Empty)
              .Append(tgtDisplay).Append(' ').Append(job.MethodName)
              .Append('(').Append(srcDisplay).Append(' ').Append(paramName).AppendLine(")");

        sb.AppendLine("        {");

        if (srcType.IsReferenceType)
            sb.Append("            if (").Append(paramName).Append(" is null) return default;").AppendLine();

        var location = job.User?.Location ?? _mapper.Locations.FirstOrDefault();

        string body;
        if (IsObjectMappable(srcType) && IsObjectMappable(tgtType) && tgtType is INamedTypeSymbol tgtNamed)
        {
            body = BuildConstruction(tgtNamed, srcType, paramName, job.User, location);
        }
        else
        {
            body = Convert(paramName, srcType, tgtType)!;
            if (body is null)
            {
                _diagnostics.Add(Diagnostic.Create(Diagnostics.NoConversion, location,
                    paramName, Display(srcType), "return", Display(tgtType)));
                body = "default(" + tgtDisplay + ")";
            }
        }

        sb.Append("            return ").Append(body).Append(';').AppendLine();
        sb.AppendLine("        }");
    }

    /// <summary>Emits an <see cref="MapKind.Update"/> method that populates an existing target in place.</summary>
    private void EmitUpdateMethod(StringBuilder sb, MapJob job, UserMethod um)
    {
        var srcType = job.Source;
        var tgtType = job.Target;
        var srcName = um.ParameterName;
        var tgtName = um.TargetParameterName!;
        var location = um.Location ?? _mapper.Locations.FirstOrDefault();

        sb.AppendLine();
        sb.Append("        ").Append(SignaturePrefix(um.Accessibility, um.IsStatic))
          .Append(Display(um.ReturnType)).Append(' ').Append(job.MethodName)
          .Append('(').Append(um.IsExtension ? "this " : string.Empty)
          .Append(Display(um.SourceType)).Append(' ').Append(srcName)
          .Append(", ").Append(Display(tgtType)).Append(' ').Append(tgtName).AppendLine(")");
        sb.AppendLine("        {");

        var returnsTarget = !um.ReturnsVoid;
        string ret = returnsTarget ? "return " + tgtName + ";" : "return;";

        // Nothing to map into if either side is a null reference.
        if (srcType.IsReferenceType || tgtType.IsReferenceType)
        {
            sb.Append("            if (");
            var conds = new List<string>();
            if (srcType.IsReferenceType) conds.Add(srcName + " is null");
            if (tgtType.IsReferenceType) conds.Add(tgtName + " is null");
            sb.Append(string.Join(" || ", conds)).Append(") ").Append(ret).AppendLine();
        }

        if (IsObjectMappable(srcType) && IsObjectMappable(tgtType) && tgtType is INamedTypeSymbol tgtNamed)
        {
            var readable = ReadableMembers(srcType);
            foreach (var member in SettableMembers(tgtNamed))
            {
                if (um.Ignores.Contains(member.Name))
                    continue;
                // An update assigns after construction, so init-only members are unreachable here.
                if (member.InitOnly)
                    continue;
                var expr = ResolveMember(member.Name, member.Type, readable, srcName, um, tgtNamed, srcType, location);
                if (expr is null)
                    continue; // diagnostic already reported by ResolveMember
                sb.Append("            ").Append(tgtName).Append('.').Append(member.Name)
                  .Append(" = ").Append(expr).Append(';').AppendLine();
            }
        }
        else
        {
            _diagnostics.Add(Diagnostic.Create(Diagnostics.NoConversion, location,
                srcName, Display(srcType), tgtName, Display(tgtType)));
        }

        if (returnsTarget)
            sb.Append("            return ").Append(tgtName).Append(';').AppendLine();
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Builds a target <c>ValueTuple</c> such as <c>(int Id, string Name)</c> as a positional literal,
    /// resolving each element by its (friendly) name against the source — supporting renames, nested
    /// objects, and flattening just like a class target.
    /// </summary>
    private string BuildTuple(INamedTypeSymbol tuple, ITypeSymbol sourceType, string accessor,
        UserMethod? user, Location? location)
    {
        var readable = ReadableMembers(sourceType);
        var parts = new List<string>();
        foreach (var element in tuple.TupleElements)
        {
            if (user is not null && user.Ignores.Contains(element.Name))
            {
                parts.Add("default(" + Display(element.Type) + ")");
                continue;
            }
            var expr = ResolveMember(element.Name, element.Type, readable, accessor, user, tuple, sourceType, location)
                       ?? "default(" + Display(element.Type) + ")";
            parts.Add(expr);
        }
        return "(" + string.Join(", ", parts) + ")";
    }

    private string SignaturePrefix(Microsoft.CodeAnalysis.Accessibility accessibility, bool isStatic)
        => Accessibility(accessibility) + " " + (isStatic ? "static " : string.Empty) + "partial ";

    private string BuildConstruction(INamedTypeSymbol targetNamed, ITypeSymbol sourceType, string accessor,
        UserMethod? user, Location? location)
    {
        // A tuple target is built as a positional literal; element names infer from the target type.
        if (targetNamed.IsTupleType)
            return BuildTuple(targetNamed, sourceType, accessor, user, location);

        var readable = ReadableMembers(sourceType);
        var settable = SettableMembers(targetNamed).ToList();
        var ctor = ChooseConstructor(targetNamed, readable, sourceType, user);

        var ctorCovered = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var args = new List<string>();
        if (ctor is { Parameters.Length: > 0 })
        {
            foreach (var prm in ctor.Parameters)
            {
                ctorCovered.Add(prm.Name);
                var expr = ResolveMember(prm.Name, prm.Type, readable, accessor, user, targetNamed, sourceType, location)
                           ?? "default(" + Display(prm.Type) + ")";
                args.Add(expr);
            }
        }

        var inits = new List<string>();
        foreach (var member in settable)
        {
            if (ctorCovered.Contains(member.Name))
                continue;
            if (user is not null && user.Ignores.Contains(member.Name))
                continue;

            var expr = ResolveMember(member.Name, member.Type, readable, accessor, user, targetNamed, sourceType, location);
            if (expr is null)
                continue; // ResolveMember already reported the appropriate diagnostic
            inits.Add(member.Name + " = " + expr);
        }

        var tgtDisplay = Display(targetNamed);
        var sb = new StringBuilder();
        sb.Append("new ").Append(tgtDisplay);
        if (args.Count > 0)
            sb.Append('(').Append(string.Join(", ", args)).Append(')');
        else if (inits.Count == 0)
            sb.Append("()");

        if (inits.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("            {");
            for (var i = 0; i < inits.Count; i++)
                sb.Append("                ").Append(inits[i]).AppendLine(i == inits.Count - 1 ? "," : ",");
            sb.Append("            }");
        }
        return sb.ToString();
    }

    /// <summary>Resolves a target member/ctor-param to a source value expression, or null if unmapped.</summary>
    private string? ResolveMember(string targetName, ITypeSymbol targetType,
        Dictionary<string, ISymbol> readable, string accessor, UserMethod? user,
        INamedTypeSymbol owner, ITypeSymbol sourceRootType, Location? location)
    {
        var sourceName = targetName;
        if (user is not null)
        {
            foreach (var rename in user.Renames)
            {
                if (NameEquals(rename.Target, targetName))
                {
                    sourceName = rename.Source;
                    break;
                }
            }
        }

        // Explicit dotted source path, e.g. [MapProperty("Address.City", "City")] — navigate it,
        // producing a null-safe expression that walks the nested source members.
        if (sourceName.IndexOf('.') >= 0)
        {
            var explicitExpr = ResolvePath(accessor, sourceRootType, SplitPath(sourceName), targetType);
            if (explicitExpr is not null)
                return explicitExpr;
            ReportUnmapped(owner, targetName, location);
            return null;
        }

        if (TryFindMember(readable, sourceName, out var srcMember))
        {
            var srcMemberType = MemberType(srcMember);
            var srcExpr = accessor + "." + srcMember.Name;
            var conv = Convert(srcExpr, srcMemberType, targetType);
            if (conv is null)
            {
                _diagnostics.Add(Diagnostic.Create(Diagnostics.NoConversion, location,
                    srcMember.Name, Display(srcMemberType), targetName, Display(targetType)));
                return null;
            }
            return conv;
        }

        // Flattening: resolve `AddressCity` by walking `source.Address.City` through nested members.
        var flattened = ResolveAutoFlatten(accessor, sourceRootType, sourceName, targetType);
        if (flattened is not null)
            return flattened;

        ReportUnmapped(owner, targetName, location);
        return null;
    }

    private void ReportUnmapped(INamedTypeSymbol owner, string targetName, Location? location)
    {
        if (_options.UnmappedBehavior == UnmappedBehavior.Ignore)
            return;
        var severity = _options.UnmappedBehavior == UnmappedBehavior.Error
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;
        _diagnostics.Add(Diagnostic.Create(Diagnostics.UnmappedMember(severity), location,
            Display(owner), targetName));
    }

    // ---- flattening ---------------------------------------------------------

    private static string[] SplitPath(string dotted) => dotted.Split('.');

    /// <summary>
    /// Auto-flattening: greedily decomposes a flat target name (e.g. <c>AddressCity</c>) into a chain
    /// of nested source members (<c>Address</c> then <c>City</c>) and returns the null-safe accessor,
    /// or null if no such chain exists.
    /// </summary>
    private string? ResolveAutoFlatten(string accessor, ITypeSymbol rootType, string name, ITypeSymbol targetType)
    {
        var path = new List<(string Name, ITypeSymbol Type)>();
        var rootCore = NullableUnderlying(rootType) ?? rootType;
        if (!BuildAutoPath(rootCore, name, path) || path.Count < 2)
            return null;
        return BuildGuardedPath(accessor, path, targetType);
    }

    private bool BuildAutoPath(ITypeSymbol type, string name, List<(string, ITypeSymbol)> path)
    {
        var readable = ReadableMembers(type);
        // Longest member-name prefix first, so `AddressLine1` prefers a member `Address` over `A`.
        var candidates = readable.Values
            .Where(m => name.Length >= m.Name.Length && StartsWithName(name, m.Name))
            .OrderByDescending(m => m.Name.Length)
            .ToList();

        foreach (var m in candidates)
        {
            var mType = MemberType(m);
            if (m.Name.Length == name.Length)
            {
                path.Add((m.Name, mType));
                return true;
            }

            var core = NullableUnderlying(mType) ?? mType;
            path.Add((m.Name, mType));
            if (BuildAutoPath(core, name.Substring(m.Name.Length), path))
                return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    /// <summary>Resolves an explicit segmented source path (case-insensitively unless configured otherwise).</summary>
    private string? ResolvePath(string accessor, ITypeSymbol rootType, string[] segments, ITypeSymbol targetType)
    {
        var path = new List<(string, ITypeSymbol)>();
        var current = NullableUnderlying(rootType) ?? rootType;
        foreach (var segment in segments)
        {
            var readable = ReadableMembers(current);
            if (!TryFindMember(readable, segment, out var member))
                return null;
            var mType = MemberType(member);
            path.Add((member.Name, mType));
            current = NullableUnderlying(mType) ?? mType;
        }
        return BuildGuardedPath(accessor, path, targetType);
    }

    /// <summary>
    /// Builds a null-safe accessor over <paramref name="path"/> (a chain of source members). Each
    /// intermediate reference type or nullable value type contributes a guard so a null anywhere in
    /// the chain yields <c>default(TTarget)</c> instead of throwing.
    /// </summary>
    private string? BuildGuardedPath(string accessor, List<(string Name, ITypeSymbol Type)> path, ITypeSymbol targetType)
    {
        var guards = new List<string>();
        var expr = accessor;
        for (var i = 0; i < path.Count; i++)
        {
            var (name, type) = path[i];
            expr += "." + name;
            if (i == path.Count - 1)
                break; // leaf: Convert handles its (possibly nullable) type below

            var nullableCore = NullableUnderlying(type);
            if (type.IsReferenceType || nullableCore is not null)
                guards.Add(expr + " is null");
            if (nullableCore is not null)
                expr += ".Value"; // step through the nullable value type
        }

        var leafType = path[path.Count - 1].Type;
        var conv = Convert(expr, leafType, targetType);
        if (conv is null)
            return null;
        if (guards.Count == 0)
            return conv;
        return "(" + string.Join(" || ", guards) + ") ? default(" + Display(targetType) + ") : (" + conv + ")";
    }

    private bool StartsWithName(string name, string prefix)
        => name.StartsWith(prefix, _options.CaseInsensitive
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal);

    // ---- conversion engine --------------------------------------------------

    private string? Convert(string expr, ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            return expr;

        // user-defined value converter for this exact pair takes precedence over any built-in rule
        if (_converters.TryGetValue(Key(sourceType, targetType), out var converter))
            return converter + "(" + expr + ")";

        // enums (including nullable enums)
        if (IsEnumLike(sourceType) && IsEnumLike(targetType))
            return ConvertEnum(expr, sourceType, targetType);

        // dictionaries (checked before the generic collection path)
        if (TryGetDictionary(sourceType, out var srcKey, out var srcVal)
            && TryGetTargetDictionary(targetType, out var tgtKey, out var tgtVal))
            return ConvertDictionary(expr, sourceType, srcKey!, srcVal!, tgtKey!, tgtVal!);

        // collections
        if (TryGetElement(sourceType, out var srcElem) && TryGetTargetCollection(targetType, out var tgtElem, out var kind))
            return ConvertCollection(expr, sourceType, srcElem!, tgtElem!, kind);

        // implicit conversion (inheritance, numeric widening, T -> T?, boxing to object, user-defined implicit)
        var classify = _csharp?.ClassifyConversion(sourceType, targetType) ?? default;
        if (classify.IsImplicit && classify.Exists)
            return expr;

        // nested complex object mapping (before the generic nullable unwrap, so that a nullable
        // complex struct such as Point? -> PointDto? keeps its null instead of mapping default).
        var srcCore = NullableUnderlying(sourceType) ?? sourceType;
        var tgtCore = NullableUnderlying(targetType) ?? targetType;
        if (IsObjectMappable(srcCore) && IsObjectMappable(tgtCore)
            && srcCore is INamedTypeSymbol srcNamed && tgtCore is INamedTypeSymbol tgtNamed)
        {
            var method = GetOrCreateMap(srcNamed, tgtNamed);

            // Nullable value-type source: guard HasValue and unwrap (the helper has no null guard
            // because its parameter is a non-nullable value type).
            if (NullableUnderlying(sourceType) is not null)
                return expr + " is null ? default(" + Display(targetType) + ") : "
                       + method + "((" + expr + ").Value)";

            // Reference-type or non-nullable value-type source. For a reference type the helper
            // already returns default for a null argument, so no extra guard (and no re-evaluation
            // of a possibly side-effecting accessor) is needed.
            return method + "(" + expr + ")";
        }

        // nullable value unwrap: T? -> something
        var srcNullableUnderlying = NullableUnderlying(sourceType);
        if (srcNullableUnderlying is not null)
        {
            var inner = Convert("(" + expr + ").GetValueOrDefault()", srcNullableUnderlying, targetType);
            if (inner is not null)
                return inner;
        }

        // explicit numeric conversion
        if (classify.Exists && classify.IsNumeric)
            return "(" + Display(targetType) + ")(" + expr + ")";

        return null;
    }

    private string ConvertEnum(string expr, ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        var srcUnderlying = NullableUnderlying(sourceType);
        var tgtUnderlying = NullableUnderlying(targetType);
        var srcCore = srcUnderlying ?? sourceType;
        var tgtCore = tgtUnderlying ?? targetType;
        var tgtCoreDisplay = Display(tgtCore);

        string Core(string e)
        {
            if (SymbolEqualityComparer.Default.Equals(srcCore, tgtCore))
                return e;
            if (_options.EnumStrategy == EnumStrategy.ByValue)
                return "(" + tgtCoreDisplay + ")(" + e + ")";

            // ByName: switch expression across matching member names.
            var tgtNames = new HashSet<string>(tgtCore.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.ConstantValue is not null).Select(f => f.Name));
            var arms = new List<string>();
            foreach (var f in srcCore.GetMembers().OfType<IFieldSymbol>().Where(f => f.ConstantValue is not null))
            {
                if (tgtNames.Contains(f.Name))
                    arms.Add(Display(srcCore) + "." + f.Name + " => " + tgtCoreDisplay + "." + f.Name);
            }
            var sb = new StringBuilder();
            sb.Append(e).Append(" switch { ");
            foreach (var arm in arms)
                sb.Append(arm).Append(", ");
            sb.Append("_ => (").Append(tgtCoreDisplay).Append(")(").Append(e).Append(") }");
            return sb.ToString();
        }

        if (srcUnderlying is not null)
        {
            var whenNull = tgtUnderlying is not null ? "(" + Display(targetType) + ")null" : "default(" + tgtCoreDisplay + ")";
            return "(" + expr + ").HasValue ? " + Core("(" + expr + ").Value") + " : " + whenNull;
        }
        return Core(expr);
    }

    private string ConvertCollection(string expr, ITypeSymbol sourceType, ITypeSymbol srcElem,
        ITypeSymbol tgtElem, CollectionKind kind)
    {
        var elemConv = Convert("x", srcElem, tgtElem);
        if (elemConv is null)
            return null!; // caller reports; ConvertCollection is only reached when both are collections

        string sequence = elemConv == "x"
            ? expr
            : "global::System.Linq.Enumerable.Select(" + expr + ", x => " + elemConv + ")";

        var tgtElemDisplay = Display(tgtElem);
        string materialized = kind switch
        {
            CollectionKind.Array => "global::System.Linq.Enumerable.ToArray(" + sequence + ")",
            CollectionKind.HashSet => "new global::System.Collections.Generic.HashSet<" + tgtElemDisplay + ">(" + sequence + ")",
            _ => "global::System.Linq.Enumerable.ToList(" + sequence + ")",
        };

        if (sourceType.IsReferenceType)
            return expr + " is null ? default(" + DisplayCollectionTarget(kind, tgtElemDisplay) + ") : " + materialized;
        return materialized;
    }

    private string? ConvertDictionary(string expr, ITypeSymbol sourceType,
        ITypeSymbol srcKey, ITypeSymbol srcVal, ITypeSymbol tgtKey, ITypeSymbol tgtVal)
    {
        var keyConv = Convert("kv.Key", srcKey, tgtKey);
        var valConv = Convert("kv.Value", srcVal, tgtVal);
        if (keyConv is null || valConv is null)
            return null;

        var tgtKeyDisplay = Display(tgtKey);
        var tgtValDisplay = Display(tgtVal);
        var materialized = "global::System.Linq.Enumerable.ToDictionary(" + expr +
            ", kv => " + keyConv + ", kv => " + valConv + ")";

        if (sourceType.IsReferenceType)
            return expr + " is null ? default(global::System.Collections.Generic.Dictionary<"
                   + tgtKeyDisplay + ", " + tgtValDisplay + ">) : " + materialized;
        return materialized;
    }

    // ---- map registry -------------------------------------------------------

    private string GetOrCreateMap(INamedTypeSymbol source, INamedTypeSymbol target)
    {
        var key = Key(source, target);
        if (_mapMethods.TryGetValue(key, out var existing))
            return existing;

        var name = "Map_" + (++_helperCounter);
        _mapMethods[key] = name;
        _pending.Enqueue(new MapJob(source, target, name, null));
        return name;
    }

    // ---- symbol helpers -----------------------------------------------------

    private IMethodSymbol? ChooseConstructor(INamedTypeSymbol type, Dictionary<string, ISymbol> readable,
        ITypeSymbol sourceRootType, UserMethod? user)
    {
        var ctors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Internal)
            .ToList();

        var parameterless = ctors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterless is not null)
            return parameterless;

        foreach (var c in ctors.OrderByDescending(c => c.Parameters.Length))
        {
            if (c.Parameters.All(prm => CanResolve(prm.Name, prm.Type, readable, sourceRootType, user)))
                return c;
        }
        return ctors.OrderByDescending(c => c.Parameters.Length).FirstOrDefault();
    }

    private bool CanResolve(string targetName, ITypeSymbol targetType, Dictionary<string, ISymbol> readable,
        ITypeSymbol sourceRootType, UserMethod? user)
    {
        var sourceName = targetName;
        if (user is not null)
            foreach (var r in user.Renames)
                if (NameEquals(r.Target, targetName)) { sourceName = r.Source; break; }

        if (sourceName.IndexOf('.') >= 0)
            return ResolvePath("x", sourceRootType, SplitPath(sourceName), targetType) is not null;

        if (TryFindMember(readable, sourceName, out var m))
            return Convert("x", MemberType(m), targetType) is not null;

        return ResolveAutoFlatten("x", sourceRootType, sourceName, targetType) is not null;
    }

    private Dictionary<string, ISymbol> ReadableMembers(ITypeSymbol type)
    {
        var result = new Dictionary<string, ISymbol>(_options.CaseInsensitive
            ? System.StringComparer.OrdinalIgnoreCase
            : System.StringComparer.Ordinal);

        foreach (var t in SelfAndBases(type))
        {
            foreach (var member in t.GetMembers())
            {
                if (result.ContainsKey(member.Name)) continue;
                switch (member)
                {
                    case IPropertySymbol { GetMethod: not null, IsStatic: false, Parameters.Length: 0 } prop
                        when prop.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public:
                        result[prop.Name] = prop;
                        break;
                    case IFieldSymbol { IsStatic: false, IsConst: false } field
                        when field.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public:
                        result[field.Name] = field;
                        break;
                }
            }
        }

        // Expose tuple elements by their friendly names (Id, Name, … or Item1, Item2, …) so a tuple
        // can be a mapping source.
        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            foreach (var element in tuple.TupleElements)
                if (!result.ContainsKey(element.Name))
                    result[element.Name] = element;
        }
        return result;
    }

    private IEnumerable<TargetMember> SettableMembers(ITypeSymbol type)
    {
        var seen = new HashSet<string>();
        foreach (var t in SelfAndBases(type))
        {
            foreach (var member in t.GetMembers())
            {
                if (!seen.Add(member.Name)) continue;
                switch (member)
                {
                    case IPropertySymbol { SetMethod: { } set, IsStatic: false, Parameters.Length: 0 } prop
                        when set.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public
                             && prop.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public:
                        yield return new TargetMember(prop.Name, prop.Type, set.IsInitOnly);
                        break;
                    case IFieldSymbol { IsStatic: false, IsConst: false, IsReadOnly: false } field
                        when field.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public:
                        yield return new TargetMember(field.Name, field.Type, InitOnly: false);
                        break;
                }
            }
        }
    }

    private static IEnumerable<ITypeSymbol> SelfAndBases(ITypeSymbol type)
    {
        for (ITypeSymbol? t = type; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            yield return t;
    }

    private bool TryFindMember(Dictionary<string, ISymbol> readable, string name, out ISymbol member)
        => readable.TryGetValue(name, out member!);

    private static ITypeSymbol MemberType(ISymbol s) => s switch
    {
        IPropertySymbol p => p.Type,
        IFieldSymbol f => f.Type,
        _ => throw new System.InvalidOperationException(),
    };

    private static ITypeSymbol? NullableUnderlying(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : null;

    private static bool IsEnumLike(ITypeSymbol type)
    {
        var core = NullableUnderlying(type) ?? type;
        return core.TypeKind == TypeKind.Enum;
    }

    private bool IsMappableComplex(ITypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return false;
        if (type.SpecialType != SpecialType.None)
            return false;
        if (type.TypeKind == TypeKind.Enum)
            return false;
        var full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
        return !KnownLeafTypes.Contains(full);
    }

    /// <summary>True for a plain object we can construct and map member-by-member (not a collection or enum).</summary>
    private bool IsObjectMappable(ITypeSymbol type)
        => IsMappableComplex(type) && !IsEnumLike(type) && !TryGetElement(type, out _);

    private bool TryGetElement(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;
        if (type.SpecialType == SpecialType.System_String)
            return false;
        if (type is IArrayTypeSymbol { Rank: 1 } arr)
        {
            element = arr.ElementType;
            return true;
        }
        var ienum = SelfAndInterfaces(type).FirstOrDefault(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        if (ienum is not null)
        {
            element = ienum.TypeArguments[0];
            return true;
        }
        return false;
    }

    private bool TryGetTargetCollection(ITypeSymbol type, out ITypeSymbol? element, out CollectionKind kind)
    {
        element = null;
        kind = CollectionKind.List;
        if (type.SpecialType == SpecialType.System_String)
            return false;
        if (type is IArrayTypeSymbol { Rank: 1 } arr)
        {
            element = arr.ElementType;
            kind = CollectionKind.Array;
            return true;
        }
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 1)
            return false;

        var def = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        element = named.TypeArguments[0];
        switch (def)
        {
            case "global::System.Collections.Generic.HashSet<T>":
            case "global::System.Collections.Generic.ISet<T>":
            case "global::System.Collections.Generic.IReadOnlySet<T>":
                kind = CollectionKind.HashSet;
                return true;
            case "global::System.Collections.Generic.List<T>":
            case "global::System.Collections.Generic.IList<T>":
            case "global::System.Collections.Generic.ICollection<T>":
            case "global::System.Collections.Generic.IEnumerable<T>":
            case "global::System.Collections.Generic.IReadOnlyList<T>":
            case "global::System.Collections.Generic.IReadOnlyCollection<T>":
                kind = CollectionKind.List;
                return true;
            default:
                element = null;
                return false;
        }
    }

    private static IEnumerable<INamedTypeSymbol> SelfAndInterfaces(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named)
            yield return named;
        foreach (var i in type.AllInterfaces)
            yield return i;
    }

    /// <summary>Detects a dictionary-like source (anything implementing <c>IReadOnlyDictionary</c>
    /// or <c>IDictionary</c>) and yields its key/value types.</summary>
    private static bool TryGetDictionary(ITypeSymbol type, out ITypeSymbol? key, out ITypeSymbol? value)
    {
        key = value = null;
        var dict = SelfAndInterfaces(type).FirstOrDefault(i =>
        {
            var def = i.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return def is "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"
                       or "global::System.Collections.Generic.IDictionary<TKey, TValue>";
        });
        if (dict is null || dict.TypeArguments.Length != 2)
            return false;
        key = dict.TypeArguments[0];
        value = dict.TypeArguments[1];
        return true;
    }

    /// <summary>Detects a supported dictionary target (<c>Dictionary&lt;,&gt;</c> or the read-only /
    /// interface variants) that we can materialize into a concrete <c>Dictionary&lt;,&gt;</c>.</summary>
    private static bool TryGetTargetDictionary(ITypeSymbol type, out ITypeSymbol? key, out ITypeSymbol? value)
    {
        key = value = null;
        if (type is not INamedTypeSymbol named || named.TypeArguments.Length != 2)
            return false;

        var def = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (def)
        {
            case "global::System.Collections.Generic.Dictionary<TKey, TValue>":
            case "global::System.Collections.Generic.IDictionary<TKey, TValue>":
            case "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                key = named.TypeArguments[0];
                value = named.TypeArguments[1];
                return true;
            default:
                return false;
        }
    }

    private string DisplayCollectionTarget(CollectionKind kind, string elemDisplay) => kind switch
    {
        CollectionKind.Array => elemDisplay + "[]",
        CollectionKind.HashSet => "global::System.Collections.Generic.HashSet<" + elemDisplay + ">",
        _ => "global::System.Collections.Generic.List<" + elemDisplay + ">",
    };

    private string Display(ITypeSymbol type) => type.ToDisplayString(FullyQualified);

    private string Key(ITypeSymbol source, ITypeSymbol target)
        => source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "=>" +
           target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private bool NameEquals(string a, string b)
        => string.Equals(a, b, _options.CaseInsensitive ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal);

    private static string Accessibility(Microsoft.CodeAnalysis.Accessibility a) => a switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        Microsoft.CodeAnalysis.Accessibility.Private => "private",
        _ => "internal",
    };

    private static string TypeKeyword(INamedTypeSymbol type)
    {
        if (type.IsRecord)
            return type.TypeKind == TypeKind.Struct ? "record struct" : "record";
        return type.TypeKind == TypeKind.Struct ? "struct" : "class";
    }

    // ---- nested types -------------------------------------------------------

    private enum CollectionKind { List, Array, HashSet }

    private readonly record struct MapJob(ITypeSymbol Source, ITypeSymbol Target, string MethodName, UserMethod? User);

    private readonly record struct TargetMember(string Name, ITypeSymbol Type, bool InitOnly);
}
