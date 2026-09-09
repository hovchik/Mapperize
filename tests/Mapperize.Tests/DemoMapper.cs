using Mapperize;
using Mapperize.Tests.Model;

namespace Mapperize.Tests;

[Mapper]
public partial class DemoMapper
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.SecretNote))]
    public partial UserDto ToDto(User user);

    // Top-level collection mapping; reuses ToDto for the element map.
    public partial List<UserDto> ToDtos(List<User> users);

    // Positional record target (constructor-based mapping).
    public partial PersonRecord ToRecord(Person person);
}
