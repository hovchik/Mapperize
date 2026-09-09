// Example: extension methods.
//
// Add `this` to the source parameter and the mapping method becomes an extension method, so you
// can map fluently: `user.ToDto()`. The class must be a `static partial class`.
using Mapperize;

var user = new User { Id = 7, FullName = "Grace Hopper" };

// Fluent, discoverable via IntelliSense on the source value.
UserDto dto = user.ToDto();

Console.WriteLine($"user.ToDto() => Id={dto.Id}, Name={dto.Name}");

// Extension methods compose naturally in LINQ pipelines.
var names = new[] { user, new User { Id = 8, FullName = "Katherine Johnson" } }
    .Select(u => u.ToDto())
    .Select(d => d.Name)
    .ToList();
Console.WriteLine($"pipeline => {string.Join(", ", names)}");

return dto is { Id: 7, Name: "Grace Hopper" } && names.Count == 2 ? 0 : 1;

// ---- mapper ----------------------------------------------------------------

[Mapper]
public static partial class MappingExtensions
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    public static partial UserDto ToDto(this User user);
}

// ---- models ----------------------------------------------------------------

public class User { public int Id { get; set; } public string FullName { get; set; } = ""; }
public class UserDto { public int Id { get; set; } public string Name { get; set; } = ""; }
