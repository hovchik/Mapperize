using System;
using System.Globalization;
using Mapperize;
using Mapperize.Tests.Model;
using Xunit;

namespace Mapperize.Tests;

// ---------------------------------------------------------------------------
// Complex-type mapping: flattening (auto + explicit dotted paths), user-defined
// value converters, and tuple mapping.
// ---------------------------------------------------------------------------

[Mapper]
public partial class FlattenMapper
{
    // Auto-flattening: CustomerName <- Customer.Name, CustomerHomeAddressCity <- Customer.HomeAddress.City
    public partial FlatOrderDto ToDto(OrderWithCustomer order);

    // Flatten through a nullable reference link (Location may be null).
    public partial NodeDto ToDto(NodeSource node);

    // Explicit dotted source path.
    [MapProperty("Customer.Name", "Buyer")]
    public partial BuyerDto ToBuyer(OrderWithCustomer order);
}

[Mapper]
public partial class ConverterMapper
{
    public partial EventDto ToDto(Event e);

    // User-defined value converters: picked up by signature for the exact source/target pair.
    private static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string PriorityLabel(int priority) => priority >= 5 ? "high" : "low";
}

[Mapper]
public partial class TupleMapper
{
    // Object -> named tuple.
    public partial (int Id, string Name) ToTuple(Person person);

    // Named tuple -> object (record via constructor).
    public partial PersonRecord FromTuple((int Id, string Name) value);

    // A member that is itself a tuple gets an auto-generated element map.
    public partial TupleHolderDto ToDto(TupleHolder holder);
}

public class ComplexTypesTests
{
    // ---- flattening --------------------------------------------------------

    [Fact]
    public void Auto_flattens_nested_member()
    {
        var mapper = new FlattenMapper();
        var dto = mapper.ToDto(new OrderWithCustomer
        {
            Id = 42,
            Customer = new Customer { Name = "Ada", LoyaltyPoints = 99 },
        });
        Assert.Equal(42, dto.Id);
        Assert.Equal("Ada", dto.CustomerName);
        Assert.Equal(99, dto.CustomerLoyaltyPoints);
    }

    [Fact]
    public void Auto_flattens_deeply_nested_member()
    {
        var mapper = new FlattenMapper();
        var dto = mapper.ToDto(new OrderWithCustomer
        {
            Customer = new Customer { HomeAddress = new Address { City = "London" } },
        });
        Assert.Equal("London", dto.CustomerHomeAddressCity);
    }

    [Fact]
    public void Flatten_through_null_link_yields_default()
    {
        var mapper = new FlattenMapper();
        var dto = mapper.ToDto(new NodeSource { Label = "n1", Location = null });
        Assert.Equal("n1", dto.Label);
        Assert.Null(dto.LocationCity);
    }

    [Fact]
    public void Flatten_through_present_link_reads_value()
    {
        var mapper = new FlattenMapper();
        var dto = mapper.ToDto(new NodeSource { Location = new Address { City = "Paris" } });
        Assert.Equal("Paris", dto.LocationCity);
    }

    [Fact]
    public void Explicit_dotted_path_maps_member()
    {
        var mapper = new FlattenMapper();
        var dto = mapper.ToBuyer(new OrderWithCustomer { Id = 1, Customer = new Customer { Name = "Grace" } });
        Assert.Equal("Grace", dto.Buyer);
    }

    // ---- custom value converters ------------------------------------------

    [Fact]
    public void Uses_user_converter_for_datetime_to_string()
    {
        var mapper = new ConverterMapper();
        var dto = mapper.ToDto(new Event { Title = "Launch", When = new DateTime(2026, 9, 10), Priority = 7 });
        Assert.Equal("Launch", dto.Title);
        Assert.Equal("2026-09-10", dto.When);
        Assert.Equal("high", dto.Priority);
    }

    // ---- tuples ------------------------------------------------------------

    [Fact]
    public void Maps_object_to_named_tuple()
    {
        var mapper = new TupleMapper();
        var t = mapper.ToTuple(new Person { Id = 5, Name = "Alan" });
        Assert.Equal(5, t.Id);
        Assert.Equal("Alan", t.Name);
    }

    [Fact]
    public void Maps_named_tuple_to_object()
    {
        var mapper = new TupleMapper();
        var record = mapper.FromTuple((Id: 9, Name: "Katherine"));
        Assert.Equal(9, record.Id);
        Assert.Equal("Katherine", record.Name);
    }

    [Fact]
    public void Maps_member_that_is_a_tuple()
    {
        var mapper = new TupleMapper();
        var dto = mapper.ToDto(new TupleHolder { Person = (Id: 3, Name: "Edsger") });
        Assert.Equal(3, dto.Person.Id);
        Assert.Equal("Edsger", dto.Person.Name);
    }
}
