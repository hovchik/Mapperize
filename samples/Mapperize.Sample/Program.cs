using Mapperize;
using Microsoft.Extensions.DependencyInjection;

// A runnable tour of Mapperize. Mapping code is generated at compile time — inspect it under
// obj/.../generated. This program also acts as a runtime smoke test (exit code 1 on failure).

var mapper = new AppMapper();

var user = new User
{
    Id = 7,
    FullName = "Ada Lovelace",
    Age = 36,
    Ticks = 5_000_000_000L,
    Score = 42,
    Status = Status.Pending,
    Address = new Address { Street = "1 Analytical Way", City = "London" },
    Orders = new List<Order> { new() { Id = 1, Total = 9.99m }, new() { Id = 2, Total = 100m } },
    Tags = new[] { "vip", "beta" },
    SecretNote = "classified",
};

var dto = mapper.ToDto(user);

var failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) failures++;
}

Console.WriteLine("Mapperize sample");
Check("flat property (Id)", dto.Id == 7);
Check("rename FullName -> Name", dto.Name == "Ada Lovelace");
Check("int -> long widening (Age)", dto.Age == 36L);
Check("long -> int narrowing (Ticks)", dto.Ticks == unchecked((int)5_000_000_000L));
Check("int? -> int unwrap (Score)", dto.Score == 42);
Check("enum by name (Pending)", dto.Status == StatusDto.Pending);
Check("nested object (Address.City)", dto.Address?.City == "London");
Check("nested collection (Orders)", dto.Orders.Count == 2 && dto.Orders[1].Total == 100m);
Check("array copy (Tags)", dto.Tags.Length == 2 && dto.Tags[0] == "vip");
Check("ignored member (SecretNote)", dto.SecretNote is null);
Check("null source -> null", mapper.ToDto(null!) is null);
Check("record via constructor", mapper.ToRecord(new Person { Id = 99, Name = "Grace" }) is { Id: 99, Name: "Grace" });
Check("top-level collection", mapper.ToDtos(new List<User> { user, user }).Count == 2);

// Alternative usage styles ---------------------------------------------------

// 1. Extension method: call it fluently on the source value.
Check("extension method (order.ToDto())", user.Orders[0].ToDto().Id == 1);

// 2. Static method: no instance required.
Check("static method (Maps.ToOrder)", Maps.ToOrder(user.Orders[1]).Total == 100m);

// 3. Update in place: populate an existing instance instead of allocating a new one.
var existing = new UserDto { SecretNote = "keep-me" };
var updated = mapper.Apply(user, existing);
Check("update in place (same instance)", ReferenceEquals(updated, existing));
Check("update in place (mapped value)", existing.Name == "Ada Lovelace");
Check("update in place (ignore preserved)", existing.SecretNote == "keep-me");

// 4. Dependency injection: register every mapper in one line, then resolve by interface.
var provider = new ServiceCollection()
    .AddMapperize()                 // generated extension — no reflection, no scanning
    .BuildServiceProvider();
var injected = provider.GetRequiredService<IAppMapper>();   // IAppMapper is generated too
Check("DI: resolve mapper via interface", injected.ToDto(user).Name == "Ada Lovelace");

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

// ---- mappers ---------------------------------------------------------------

// The classic style: an instance mapper class. GenerateInterface also emits `IAppMapper`
// (which this class implements) so it can be injected and mocked.
[Mapper(GenerateInterface = true)]
public partial class AppMapper
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.SecretNote))]
    public partial UserDto ToDto(User user);

    public partial List<UserDto> ToDtos(List<User> users);

    public partial PersonRecord ToRecord(Person person);

    // Update style: map onto an existing target and return it (source, target) -> target.
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.SecretNote))]
    public partial UserDto Apply(User user, UserDto target);
}

// A static class of mapping methods: call them statically or as extension methods.
[Mapper]
public static partial class Maps
{
    public static partial OrderDto ToOrder(Order order);      // Maps.ToOrder(order)
    public static partial OrderDto ToDto(this Order order);   // order.ToDto()
}

// ---- models ----------------------------------------------------------------

public enum Status { Active, Inactive, Pending }
public enum StatusDto { Pending, Active, Inactive }

public class Address { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class AddressDto { public string Street { get; set; } = ""; public string City { get; set; } = ""; }

public class Order { public int Id { get; set; } public decimal Total { get; set; } }
public class OrderDto { public int Id { get; set; } public decimal Total { get; set; } }

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public int Age { get; set; }
    public long Ticks { get; set; }
    public int? Score { get; set; }
    public Status Status { get; set; }
    public Address? Address { get; set; }
    public List<Order> Orders { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string SecretNote { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public long Age { get; set; }
    public int Ticks { get; set; }
    public int Score { get; set; }
    public StatusDto Status { get; set; }
    public AddressDto? Address { get; set; }
    public List<OrderDto> Orders { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string? SecretNote { get; set; }
}

public class Person { public int Id { get; set; } public string Name { get; set; } = ""; }
public record PersonRecord(int Id, string Name);
