using System;

namespace Mapperize;

/// <summary>
/// Marks a partial class as a Mapperize mapper. The source generator implements every
/// <c>partial</c> mapping method declared inside it (a method that takes a single source
/// parameter and returns the target type).
/// </summary>
/// <example>
/// <code>
/// [Mapper]
/// public partial class UserMapper
/// {
///     public partial UserDto ToDto(User source);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MapperAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c> (default) source and target member names are matched
    /// case-insensitively. Set to <c>false</c> to require an exact case match.
    /// </summary>
    public bool CaseInsensitive { get; set; } = true;

    /// <summary>
    /// Controls the build diagnostic emitted when a target member has no matching source
    /// member. Defaults to <see cref="UnmappedMemberBehavior.Warn"/>.
    /// </summary>
    public UnmappedMemberBehavior UnmappedMemberBehavior { get; set; } = UnmappedMemberBehavior.Warn;

    /// <summary>
    /// How enums are mapped when source and target enum types differ. Defaults to
    /// <see cref="EnumMappingStrategy.ByName"/>.
    /// </summary>
    public EnumMappingStrategy EnumMappingStrategy { get; set; } = EnumMappingStrategy.ByName;
}

/// <summary>
/// Overrides the default name-based matching for a single member on a mapping method,
/// or renames/ignores members. Apply it to the partial mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : Attribute
{
    /// <summary>Creates a rename mapping from <paramref name="source"/> to <paramref name="target"/>.</summary>
    public MapPropertyAttribute(string source, string target)
    {
        Source = source;
        Target = target;
    }

    /// <summary>The source member name.</summary>
    public string Source { get; }

    /// <summary>The target member name.</summary>
    public string Target { get; }
}

/// <summary>
/// Instructs the generator to ignore a target member (leave it at its default value and
/// suppress the unmapped-member diagnostic). Apply it to the partial mapping method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapperIgnoreTargetAttribute : Attribute
{
    /// <summary>Creates an ignore rule for the named target member.</summary>
    public MapperIgnoreTargetAttribute(string target) => Target = target;

    /// <summary>The target member name to ignore.</summary>
    public string Target { get; }
}

/// <summary>Behavior when a target member cannot be mapped from any source member.</summary>
public enum UnmappedMemberBehavior
{
    /// <summary>Do nothing; leave the member at its default value.</summary>
    Ignore = 0,

    /// <summary>Emit a build warning (<c>MPZ001</c>). This is the default.</summary>
    Warn = 1,

    /// <summary>Emit a build error (<c>MPZ001</c>), failing the build.</summary>
    Error = 2,
}

/// <summary>Strategy used when mapping between two different enum types.</summary>
public enum EnumMappingStrategy
{
    /// <summary>Match enum members by their name (case-sensitive). This is the default.</summary>
    ByName = 0,

    /// <summary>Cast by the underlying numeric value.</summary>
    ByValue = 1,
}
