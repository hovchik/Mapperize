# Mapperize

**A blazing-fast, compile-time, reflection-free object mapper for .NET.**
A safer, faster, AOT/trim-friendly alternative to AutoMapper — powered by a Roslyn source generator.

[![CI](https://github.com/your-username/Mapperize/actions/workflows/ci.yml/badge.svg)](https://github.com/your-username/Mapperize/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Mapperize.svg)](https://www.nuget.org/packages/Mapperize)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## Why Mapperize?

Traditional mappers such as AutoMapper build mapping logic at **runtime** using reflection and
compiled expression trees. That has three costs: a warm-up penalty, incompatibility with
Native AOT / trimming, and — worst of all — **mistakes are only discovered at runtime**.

Mapperize does the work at **compile time**. You declare `partial` mapping methods; the source
generator writes the implementations as plain member assignments. What you ship is exactly the
code you would have written by hand.

|                        | AutoMapper                              | **Mapperize**                                   |
| ---------------------- | --------------------------------------- | ----------------------------------------------- |
| When mapping is built  | Runtime (reflection + expression trees) | **Compile time (source generator)**             |
| Runtime reflection     | Yes                                     | **None**                                        |
| Startup / warm-up cost | Yes (config + first-map JIT)            | **Zero**                                        |
| Native AOT / trimming  | Fragile                                 | **Fully supported**                             |
| Wrong/typo'd mappings  | Throw at runtime                        | **Reported at build time (diagnostics)**        |
| Debuggable output      | Opaque delegates                        | **Readable generated C# you can step into**     |
| Dependencies           | Several                                 | **Zero** (a single, dependency-free package)    |

## Install

```bash
dotnet add package Mapperize
```

Targets `netstandard2.0`, so it works on .NET Framework 4.6.1+, .NET Core, and .NET 5–10.

## Quick start

```csharp
using Mapperize;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; }
    public int Age { get; set; }
    public Address Address { get; set; }
    public List<Order> Orders { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }       // renamed from FullName
    public long Age { get; set; }          // widened automatically
    public AddressDto Address { get; set; } // nested — mapped automatically
    public List<OrderDto> Orders { get; set; }
}

[Mapper]
public partial class UserMapper
{
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    public partial UserDto ToDto(User user);
}
```

Usage:

```csharp
var mapper = new UserMapper();
UserDto dto = mapper.ToDto(user);
```

That’s it. `Address` and `Orders` are discovered and mapped automatically — you don’t need to
declare a method for every nested type (though you can, and it will be reused).

## Usage styles

A mapper is just a `partial` type with `partial` methods, so you can wire it into your code in
whatever way reads best — you are not limited to a single instance-method-on-an-attributed-class
pattern.

```csharp
// 1. Instance methods (shown above)
var dto = new UserMapper().ToDto(user);

// 2. Static methods — no instance to construct or inject
[Mapper]
public static partial class Maps
{
    public static partial UserDto ToDto(User user);
}
UserDto dto = Maps.ToDto(user);

// 3. Extension methods — add `this` to the source parameter and call it fluently
[Mapper]
public static partial class Maps
{
    public static partial UserDto ToDto(this User user);
}
UserDto dto = user.ToDto();

// 4. Update an existing instance — take the target as a second parameter
[Mapper]
public partial class UserMapper
{
    public partial void Update(User source, UserDto target);        // populate in place
    public partial UserDto Merge(User source, UserDto target);      // …or return it for chaining
}
mapper.Update(user, existingDto);
```

Static and extension methods can live in a `static partial class`; update methods work in any
mapper. All four styles share the same conversion engine (renames, nested objects, collections,
enums, …). A mapper type may also be **nested inside another `partial` type**.

## Dependency injection

Mappers are plain classes, so you can inject them anywhere. If your project references
`Microsoft.Extensions.DependencyInjection`, Mapperize generates an `AddMapperize()` extension that
registers every mapper in the assembly — call it once in `Program.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMapperize();                       // Singleton by default (mappers are stateless)
// builder.Services.AddMapperize(ServiceLifetime.Scoped); // …or choose a lifetime
```

Then take the mapper as a constructor dependency:

```csharp
public class UsersController(UserMapper mapper)   // inject the concrete type…
{
    public UserDto Get(User user) => mapper.ToDto(user);
}
```

To depend on (and mock) an **abstraction**, set `GenerateInterface = true`. Mapperize emits an
`I{MapperName}` interface with the mapper's public instance methods, makes the mapper implement it,
and registers the interface alongside the concrete type:

```csharp
[Mapper(GenerateInterface = true)]
public partial class UserMapper
{
    public partial UserDto ToDto(User user);
}
// generated:  public interface IUserMapper { UserDto ToDto(User user); }

public class UsersController(IUserMapper mapper) { /* … */ }   // inject the interface
```

The `AddMapperize()` extension is generated **only** when the DI package is referenced, so the
core Mapperize package stays dependency-free for everyone else. The interface and the concrete type
resolve to the same instance, so a `Singleton` mapper is shared between both.

## Complex types

Mapperize maps rich object graphs without any hand-written plumbing.

### Flattening

A flat target member is resolved from a **nested source path** automatically — the generator walks
the source graph to find a chain of members whose names concatenate to the target name. Every hop
is null-safe: a `null` (or empty nullable) anywhere in the chain yields the target's default value
instead of throwing.

```csharp
public class Order    { public Customer Customer { get; set; } }
public class Customer { public string Name { get; set; } public Address HomeAddress { get; set; } }
public class Address  { public string City { get; set; } }

public class OrderDto
{
    public string CustomerName { get; set; }             // <- Customer.Name
    public string CustomerHomeAddressCity { get; set; }  // <- Customer.HomeAddress.City
}

[Mapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order);
}
```

When the flattened name is ambiguous or you'd rather be explicit, give `[MapProperty]` a **dotted
source path**:

```csharp
[MapProperty("Customer.HomeAddress.City", "City")]
public partial OrderDto ToDto(Order order);
```

### Value converters

Need a conversion the generator doesn't know (say `DateTime` → `string`, or a domain-specific
rule)? Just write an ordinary method on the mapper. Any `TTarget Method(TSource)` is picked up **by
its signature** and used wherever that source/target pair is mapped — including inside nested
objects and collections. A converter takes precedence over the built-in rules, so you can override
them too.

```csharp
[Mapper]
public partial class EventMapper
{
    public partial EventDto ToDto(Event e);

    // used automatically wherever a DateTime maps to a string
    private static string FormatDate(DateTime d) => d.ToString("O");
}
```

### Tuples

Named `ValueTuple`s map to and from objects by element name:

```csharp
[Mapper]
public partial class PersonMapper
{
    public partial (int Id, string Name) ToTuple(Person person);
    public partial Person FromTuple((int Id, string Name) value);
}
```

### The generated code

The generator emits ordinary, readable C# (simplified):

```csharp
public partial UserDto ToDto(User user)
{
    if (user is null) return default;
    return new UserDto
    {
        Id = user.Id,
        Name = user.FullName,
        Age = user.Age,
        Address = user.Address is null ? default : Map_1(user.Address),
        Orders = user.Orders is null ? default
            : System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(user.Orders, x => Map_2(x))),
    };
}
```

## Features

- **Multiple usage styles** — instance, `static`, and extension methods, plus
  map-into-an-existing-instance (`Update`) methods. See [Usage styles](#usage-styles).
- **Dependency-injection ready** — a generated `services.AddMapperize()` registers every mapper,
  and `GenerateInterface = true` emits an interface to inject/mock. See
  [Dependency injection](#dependency-injection).
- **Flat property mapping** by name (case-insensitive by default), across fields and properties.
- **Renames** via `[MapProperty("Source", "Target")]`.
- **Ignore** a target via `[MapperIgnoreTarget("Target")]`.
- **Nested objects** — mapped recursively; helper methods are generated and de-duplicated.
- **Flattening** — a flat target member (`CustomerHomeAddressCity`) is resolved from a nested
  source path (`Customer.HomeAddress.City`) automatically, and null-safely; or spell it out with a
  dotted `[MapProperty("Address.City", "City")]`. See [Complex types](#complex-types).
- **Value converters** — write a plain `TTarget Method(TSource)` on the mapper and it is used
  wherever that conversion is needed (and overrides the built-ins). See [Complex types](#complex-types).
- **Tuples** — named `ValueTuple`s map to and from objects by element name.
- **Collections** — `List<T>`, arrays, `HashSet<T>`, and the read-only/interface variants
  (`IEnumerable<T>`, `IReadOnlyList<T>`, `ICollection<T>`, …).
- **Dictionaries** — `Dictionary<K,V>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`; keys and
  values are converted with the same engine.
- **Enums** — by name (default, order-independent) or by value.
- **Nested and static mapper types** — a `[Mapper]` type may itself be nested inside another
  `partial` type, or be a `static partial class`.
- **Nullable value types** — `int?` → `int` and back, handled safely.
- **Numeric conversions** — implicit widening and explicit narrowing.
- **Constructors & records** — positional records and constructor-only types are supported.
- **Compile-time diagnostics** — unmapped members become build warnings (or errors, if you choose).
- **Zero runtime dependencies** and **full Native AOT / trimming** support.

## Configuration

Everything is configured on the `[Mapper]` attribute:

```csharp
[Mapper(
    CaseInsensitive = true,                               // match member names ignoring case (default)
    UnmappedMemberBehavior = UnmappedMemberBehavior.Warn, // Ignore | Warn (default) | Error
    EnumMappingStrategy = EnumMappingStrategy.ByName)]     // ByName (default) | ByValue
public partial class UserMapper
{
    public partial UserDto ToDto(User user);
}
```

Set `UnmappedMemberBehavior = UnmappedMemberBehavior.Error` to make an accidental un-mapped
property **fail the build** — turning a whole class of silent runtime bugs into compile errors.

## Diagnostics

| ID       | Meaning                                                            |
| -------- | ----------------------------------------------------------------- |
| `MPZ001` | A target member has no matching source member (warning or error). |
| `MPZ002` | A source and target member exist but no conversion is possible.   |
| `MPZ003` | A mapping method has an unsupported signature.                    |
| `MPZ004` | The mapper type (or an enclosing type) is generic or not `partial`. |

## Performance

Mapping is generated as direct assignments, so throughput matches hand-written code and there is
**no startup cost**. Indicative in-process measurements (.NET 8, single object with a nested
object + a 10-item list; run `dotnet run -c Release --project benchmarks/Mapperize.Benchmarks -- --quick`):

| Mapper                    | Time       | Allocated |
| ------------------------- | ---------- | --------- |
| Hand-written              | ~250 ns    | 776 B     |
| **Mapperize**             | **~246 ns**| **792 B** |
| AutoMapper                | ~335 ns    | 896 B     |

Mapperize is on par with hand-written code, ~25–30% faster than AutoMapper on single-object maps,
and allocates less in every scenario measured. For rigorous, isolated numbers run the full
[BenchmarkDotNet](https://benchmarkdotnet.org) suite:

```bash
dotnet run -c Release --project benchmarks/Mapperize.Benchmarks
```

## Security

- **No runtime reflection**, no dynamic code generation, no expression compilation — nothing to
  exploit or to break under trimming/AOT.
- **Deterministic, auditable output**: the mapping code is committed-grade C# you can read and diff.
- For contrast, the AutoMapper version used in the benchmark project (`13.0.1`) carries a
  published high-severity advisory ([GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x)).
  Mapperize ships **zero runtime dependencies**, so its supply-chain surface is minimal.

## How it works

`Mapperize` is a single NuGet package that contains two things:

1. A tiny **runtime** assembly with the attributes (`[Mapper]`, `[MapProperty]`, …).
2. A **Roslyn incremental source generator** (shipped under `analyzers/`) that implements your
   `partial` methods during compilation.

There is no runtime engine — after compilation the attributes aren’t even needed.

## Building & testing

```bash
dotnet build -c Release          # build everything
dotnet test  -c Release          # run the xUnit suite
dotnet run   -c Release --project samples/Mapperize.Sample   # runnable feature tour
```

Per-case example projects live under [`samples/examples/`](samples/examples) — a minimal, runnable
program for each usage style (instance, static, extension, update-in-place, dependency injection).

## Roadmap

- ~~User-defined value converters~~ ✅ shipped — see [Value converters](#value-converters)
- ~~Flattening (`Order.Customer.Name` → `CustomerName`)~~ ✅ shipped — see [Flattening](#flattening)
- ~~Tuple mapping~~ ✅ shipped — see [Tuples](#tuples)
- `before`/`after` mapping hooks
- Deep-copy option for same-type nested references
- Unflattening (`CustomerName` → `Customer.Name`)

Contributions welcome — see the issues on GitHub.

## License

MIT © 2026 Hovhannes Stepanyan. See [LICENSE](LICENSE).
