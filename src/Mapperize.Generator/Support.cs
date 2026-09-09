using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Mapperize.Generator;

internal enum UnmappedBehavior { Ignore = 0, Warn = 1, Error = 2 }

internal enum EnumStrategy { ByName = 0, ByValue = 1 }

/// <summary>Options read from the <c>[Mapper]</c> attribute on the mapper class.</summary>
internal sealed class Options
{
    public bool CaseInsensitive { get; private init; } = true;
    public UnmappedBehavior UnmappedBehavior { get; private init; } = UnmappedBehavior.Warn;
    public EnumStrategy EnumStrategy { get; private init; } = EnumStrategy.ByName;

    public static Options Read(AttributeData attr)
    {
        var caseInsensitive = true;
        var unmapped = UnmappedBehavior.Warn;
        var enumStrategy = EnumStrategy.ByName;

        foreach (var arg in attr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "CaseInsensitive" when arg.Value.Value is bool b:
                    caseInsensitive = b;
                    break;
                case "UnmappedMemberBehavior" when arg.Value.Value is int u:
                    unmapped = (UnmappedBehavior)u;
                    break;
                case "EnumMappingStrategy" when arg.Value.Value is int e:
                    enumStrategy = (EnumStrategy)e;
                    break;
            }
        }

        return new Options
        {
            CaseInsensitive = caseInsensitive,
            UnmappedBehavior = unmapped,
            EnumStrategy = enumStrategy,
        };
    }
}

internal readonly record struct Rename(string Source, string Target);

/// <summary>Metadata extracted from a user-declared partial mapping method.</summary>
internal sealed class UserMethod
{
    public string Name { get; init; } = string.Empty;
    public ITypeSymbol SourceType { get; init; } = null!;
    public ITypeSymbol ReturnType { get; init; } = null!;
    public string ParameterName { get; init; } = "source";
    public Accessibility Accessibility { get; init; }
    public List<Rename> Renames { get; init; } = new();
    public HashSet<string> Ignores { get; init; } = new();
    public Location? Location { get; init; }

    public static UserMethod Read(IMethodSymbol method)
    {
        var renames = new List<Rename>();
        var ignores = new HashSet<string>();

        foreach (var attr in method.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == "Mapperize.MapPropertyAttribute" && attr.ConstructorArguments.Length == 2)
            {
                var src = attr.ConstructorArguments[0].Value as string;
                var tgt = attr.ConstructorArguments[1].Value as string;
                if (src is not null && tgt is not null)
                    renames.Add(new Rename(src, tgt));
            }
            else if (name == "Mapperize.MapperIgnoreTargetAttribute" && attr.ConstructorArguments.Length == 1)
            {
                if (attr.ConstructorArguments[0].Value is string ignore)
                    ignores.Add(ignore);
            }
        }

        return new UserMethod
        {
            Name = method.Name,
            SourceType = method.Parameters[0].Type,
            ReturnType = method.ReturnType,
            ParameterName = method.Parameters[0].Name,
            Accessibility = method.DeclaredAccessibility,
            Renames = renames,
            Ignores = ignores,
            Location = method.Locations.FirstOrDefault(),
        };
    }
}
