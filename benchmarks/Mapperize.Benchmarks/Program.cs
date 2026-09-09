using System.Diagnostics;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Mapperize;

// `--quick` runs an in-process Stopwatch comparison (works anywhere, no child process).
// No args runs the full, rigorous BenchmarkDotNet suite (recommended for real reporting).
if (args.Contains("--quick"))
{
    QuickBench.Run();
    return;
}

BenchmarkRunner.Run<MappingBenchmarks>();

static class QuickBench
{
    public static void Run()
    {
        var b = new MappingBenchmarks();
        b.Setup();
        const int iterations = 2_000_000;

        Measure("Manual (hand-written)", iterations, () => b.Manual());
        Measure("Mapperize (source-gen)", iterations, () => b.Mapperize());
        Measure("AutoMapper (reflection)", iterations, () => b.AutoMapper());
        Console.WriteLine();
        Measure("Mapperize list x1000", iterations / 500, () => b.MapperizeList());
        Measure("AutoMapper list x1000", iterations / 500, () => b.AutoMapperList());
    }

    private static void Measure(string name, int iterations, Func<object> action)
    {
        for (var i = 0; i < 100_000; i++) action();          // warm up / JIT
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) action();
        sw.Stop();
        var after = GC.GetTotalAllocatedBytes(precise: true);
        var nsPerOp = sw.Elapsed.TotalMilliseconds * 1_000_000 / iterations;
        var bytesPerOp = (after - before) / (double)iterations;
        Console.WriteLine($"  {name,-26} {nsPerOp,10:N1} ns/op   {bytesPerOp,8:N0} B/op");
    }
}

[MemoryDiagnoser]
public class MappingBenchmarks
{
    private readonly MapperizeMapper _mapperize = new();
    private IMapper _autoMapper = null!;
    private User _user = null!;
    private List<User> _users = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<User, UserDto>();
            cfg.CreateMap<Address, AddressDto>();
            cfg.CreateMap<Order, OrderDto>();
        });
        _autoMapper = config.CreateMapper();

        _user = new User
        {
            Id = 7,
            Name = "Ada Lovelace",
            Age = 36,
            Address = new Address { Street = "1 Analytical Way", City = "London" },
            Orders = Enumerable.Range(0, 10).Select(i => new Order { Id = i, Total = i * 1.5m }).ToList(),
            Tags = new[] { "vip", "beta", "early" },
        };
        _users = Enumerable.Range(0, 1000).Select(_ => _user).ToList();
    }

    [Benchmark(Baseline = true, Description = "Manual (hand-written)")]
    public UserDto Manual() => Map(_user);

    [Benchmark(Description = "Mapperize (source-gen)")]
    public UserDto Mapperize() => _mapperize.ToDto(_user);

    [Benchmark(Description = "AutoMapper (reflection)")]
    public UserDto AutoMapper() => _autoMapper.Map<UserDto>(_user);

    [Benchmark(Description = "Mapperize list x1000")]
    public List<UserDto> MapperizeList() => _mapperize.ToDtos(_users);

    [Benchmark(Description = "AutoMapper list x1000")]
    public List<UserDto> AutoMapperList() => _autoMapper.Map<List<UserDto>>(_users);

    private static UserDto Map(User u) => new()
    {
        Id = u.Id,
        Name = u.Name,
        Age = u.Age,
        Address = u.Address is null ? null : new AddressDto { Street = u.Address.Street, City = u.Address.City },
        Orders = u.Orders.Select(o => new OrderDto { Id = o.Id, Total = o.Total }).ToList(),
        Tags = u.Tags.ToArray(),
    };
}

[Mapper]
public partial class MapperizeMapper
{
    public partial UserDto ToDto(User user);
    public partial List<UserDto> ToDtos(List<User> users);
}

public class Address { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class AddressDto { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
public class Order { public int Id { get; set; } public decimal Total { get; set; } }
public class OrderDto { public int Id { get; set; } public decimal Total { get; set; } }

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public Address? Address { get; set; }
    public List<Order> Orders { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public AddressDto? Address { get; set; }
    public List<OrderDto> Orders { get; set; } = new();
    public string[] Tags { get; set; } = Array.Empty<string>();
}
