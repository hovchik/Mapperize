// Example: mapping complex types.
//
// Shows three ways Mapperize handles complex shapes without you writing the plumbing:
//   1. Flattening      — a deep source path (Customer.HomeAddress.City) collapses into a flat
//                        target member (CustomerHomeAddressCity), null-safe at every hop.
//   2. Value converters — a plain method on the mapper (DateTime -> string) is picked up by its
//                        signature and used wherever that conversion is needed.
//   3. Tuples          — named ValueTuples map to and from objects by element name.
using System.Globalization;
using Mapperize;

var mapper = new OrderMapper();

var order = new Order
{
    Id = 100,
    PlacedOn = new DateTime(2026, 9, 10),
    Customer = new Customer
    {
        Name = "Ada Lovelace",
        HomeAddress = new Address { City = "London" },
    },
};

OrderDto dto = mapper.ToDto(order);

Console.WriteLine($"Id           : {dto.Id}");
Console.WriteLine($"CustomerName : {dto.CustomerName}                 (flattened Customer.Name)");
Console.WriteLine($"City         : {dto.CustomerHomeAddressCity}      (flattened Customer.HomeAddress.City)");
Console.WriteLine($"PlacedOn     : {dto.PlacedOn}          (DateTime -> string via a converter)");

// A null anywhere in the chain yields the default instead of throwing.
var noAddress = mapper.ToDto(new Order { Id = 1, Customer = new Customer { Name = "Grace" } });
Console.WriteLine($"Missing city : \"{noAddress.CustomerHomeAddressCity}\" (null link -> default)");

// Tuples map by element name, in both directions.
(int Id, string Name) tuple = mapper.ToTuple(new Person { Id = 7, Name = "Katherine" });
Console.WriteLine($"Tuple        : ({tuple.Id}, {tuple.Name})");

// Smoke test.
return dto is { Id: 100, CustomerName: "Ada Lovelace", CustomerHomeAddressCity: "London", PlacedOn: "2026-09-10" }
       && noAddress.CustomerHomeAddressCity is null
       && tuple is { Id: 7, Name: "Katherine" }
    ? 0 : 1;

// ---- mapper ----------------------------------------------------------------

[Mapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order);

    public partial (int Id, string Name) ToTuple(Person person);

    // A user-defined value converter: any `TTarget Method(TSource)` on the mapper is used for that
    // exact source/target pair — here DateTime -> string.
    private static string FormatDate(DateTime value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// ---- models ----------------------------------------------------------------

public class Address { public string City { get; set; } = ""; }

public class Customer
{
    public string Name { get; set; } = "";
    public Address? HomeAddress { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public DateTime PlacedOn { get; set; }
    public Customer Customer { get; set; } = new();
}

public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";            // <- Customer.Name
    public string? CustomerHomeAddressCity { get; set; }      // <- Customer.HomeAddress.City
    public string PlacedOn { get; set; } = "";                // <- DateTime via converter
}

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
