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
        messageFormat: "Mapping method '{0}' must be a non-generic partial instance method with exactly one parameter and a non-void return type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericMapperUnsupported = new(
        id: "MPZ004",
        title: "Generic or nested mapper not supported",
        messageFormat: "Mapper '{0}' must be a non-generic, top-level or namespace-level partial type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
