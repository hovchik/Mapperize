using Microsoft.CodeAnalysis;

namespace Mapperize.Generator;

internal static class Diagnostics
{
    public const string Category = "Mapperize";

    public static DiagnosticDescriptor UnmappedMember(DiagnosticSeverity severity) => new(
        id: "MPZ001",
        title: "Unmapped target member",
        messageFormat: "Target member '{0}.{1}' has no matching source member and is left at its default value",
        category: Category,
        defaultSeverity: severity,
        isEnabledByDefault: true,
        description: "Every target member should be mapped. Add a matching source member, a [MapProperty] rename, or [MapperIgnoreTarget] to silence this.");

    public static readonly DiagnosticDescriptor NoConversion = new(
        id: "MPZ002",
        title: "No conversion available",
        messageFormat: "Cannot map source member '{0}' of type '{1}' to target member '{2}' of type '{3}'; the member is left unset",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidMapperMethod = new(
        id: "MPZ003",
        title: "Invalid mapper method",
        messageFormat: "Mapping method '{0}' must be non-generic and either take one parameter and return the target (a transform), or take a source and a target parameter and return void or the target (an update)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Supported shapes are 'TTarget Map(TSource s)' and 'void Update(TSource s, TTarget t)' / 'TTarget Update(TSource s, TTarget t)'. Methods may be static and/or extension methods.");

    public static readonly DiagnosticDescriptor GenericMapperUnsupported = new(
        id: "MPZ004",
        title: "Unsupported mapper type",
        messageFormat: "Mapper type '{0}' must be non-generic, and it and every type it is nested in must be declared 'partial'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
