// Example: the classic instance mapper.
//
// Declare a [Mapper] partial class with partial mapping methods, construct it, and call them.
// This example also shows a rename, an ignore, and automatic nested-object mapping.
using Mapperize;

var mapper = new UserMapper();

var user = new User
{
    Id = 1,
    FullName = "Ada Lovelace",
    Address = new Address { City = "London" },
    Password = "hunter2",
};

UserDto dto = mapper.ToDto(user);

Console.WriteLine($"Id      : {dto.Id}");
Console.WriteLine($"Name    : {dto.Name}          (renamed from FullName)");
Console.WriteLine($"City    : {dto.Address?.City} (nested object mapped automatically)");
Console.WriteLine($"Password: {dto.Password ?? "<null>"} (ignored)");

// Smoke test.
return dto is { Id: 1, Name: "Ada Lovelace", Password: null } && dto.Address?.City == "London" ? 0 : 1;

// ---- mapper ----------------------------------------------------------------

[Mapper]
public partial class UserMapper
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.Password))]
    public partial UserDto ToDto(User user);
}

// ---- models ----------------------------------------------------------------

public class Address { public string City { get; set; } = ""; }
public class AddressDto { public string City { get; set; } = ""; }

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public Address? Address { get; set; }
    public string Password { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AddressDto? Address { get; set; }
    public string? Password { get; set; }
}
