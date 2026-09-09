// Example: dependency injection.
//
// Reference Microsoft.Extensions.DependencyInjection and Mapperize generates a `services.AddMapperize()`
// extension that registers every mapper in the assembly. With `GenerateInterface = true` it also
// emits an interface (IUserMapper) so consumers can depend on the abstraction and mock it in tests.
using Mapperize;
using Microsoft.Extensions.DependencyInjection;

// This is what you would write in Program.cs / Startup.
var services = new ServiceCollection();
services.AddMapperize();                    // one line registers all mappers (Singleton by default)
services.AddSingleton<GreetingService>();   // a service that depends on the mapper via its interface

using var provider = services.BuildServiceProvider();

// Resolve a service whose constructor takes IUserMapper.
var greeter = provider.GetRequiredService<GreetingService>();
Console.WriteLine(greeter.Greet(new User { Id = 1, FullName = "Ada Lovelace" }));

// You can also resolve the mapper directly, by interface or concrete type — both are the same
// singleton instance.
var byInterface = provider.GetRequiredService<IUserMapper>();
var byConcrete = provider.GetRequiredService<UserMapper>();
Console.WriteLine($"same singleton instance? {ReferenceEquals(byInterface, byConcrete)}");

return ReferenceEquals(byInterface, byConcrete) ? 0 : 1;

// ---- a service that depends on the mapper abstraction ----------------------

public class GreetingService(IUserMapper mapper)
{
    public string Greet(User user) => $"Hello, {mapper.ToDto(user).Name}!";
}

// ---- mapper ----------------------------------------------------------------

[Mapper(GenerateInterface = true)]   // also emits IUserMapper (implemented by UserMapper)
public partial class UserMapper
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    public partial UserDto ToDto(User user);
}

// ---- models ----------------------------------------------------------------

public class User { public int Id { get; set; } public string FullName { get; set; } = ""; }
public class UserDto { public int Id { get; set; } public string Name { get; set; } = ""; }
