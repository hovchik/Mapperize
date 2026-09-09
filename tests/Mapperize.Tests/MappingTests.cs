using Mapperize.Tests.Model;
using Xunit;

namespace Mapperize.Tests;

public class MappingTests
{
    private static User SampleUser() => new()
    {
        Id = 7,
        FullName = "Ada Lovelace",
        Age = 36,
        Ticks = 5_000_000_000L,          // > int.MaxValue to prove explicit narrowing wraps
        Score = 42,
        Status = Status.Pending,
        Address = new Address { Street = "1 Analytical Way", City = "London" },
        Orders = new List<Order>
        {
            new() { Id = 1, Total = 9.99m },
            new() { Id = 2, Total = 100m },
        },
        Tags = new[] { "vip", "beta" },
        SecretNote = "classified",
    };

    private readonly DemoMapper _mapper = new();

    [Fact]
    public void Maps_flat_properties()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal(7, dto.Id);
        Assert.Equal(36L, dto.Age);
    }

    [Fact]
    public void Applies_rename()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal("Ada Lovelace", dto.Name);
    }

    [Fact]
    public void Ignores_configured_member()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Null(dto.SecretNote);
    }

    [Fact]
    public void Maps_enum_by_name_not_value()
    {
        var dto = _mapper.ToDto(SampleUser());
        // Status.Pending (value 2) must map to StatusDto.Pending (value 0) by NAME.
        Assert.Equal(StatusDto.Pending, dto.Status);
    }

    [Fact]
    public void Unwraps_nullable_value()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal(42, dto.Score);

        var user = SampleUser();
        user.Score = null;
        Assert.Equal(0, _mapper.ToDto(user).Score);
    }

    [Fact]
    public void Applies_explicit_numeric_narrowing()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal(unchecked((int)5_000_000_000L), dto.Ticks);
    }

    [Fact]
    public void Maps_nested_object()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.NotNull(dto.Address);
        Assert.Equal("1 Analytical Way", dto.Address!.Street);
        Assert.Equal("London", dto.Address.City);
    }

    [Fact]
    public void Nested_null_is_preserved()
    {
        var user = SampleUser();
        user.Address = null;
        Assert.Null(_mapper.ToDto(user).Address);
    }

    [Fact]
    public void Maps_nested_collection()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal(2, dto.Orders.Count);
        Assert.Equal(1, dto.Orders[0].Id);
        Assert.Equal(9.99m, dto.Orders[0].Total);
        Assert.Equal(100m, dto.Orders[1].Total);
    }

    [Fact]
    public void Copies_array()
    {
        var dto = _mapper.ToDto(SampleUser());
        Assert.Equal(new[] { "vip", "beta" }, dto.Tags);
    }

    [Fact]
    public void Maps_top_level_collection_reusing_element_map()
    {
        var users = new List<User> { SampleUser(), SampleUser() };
        var dtos = _mapper.ToDtos(users);
        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d => Assert.Equal("Ada Lovelace", d.Name));
    }

    [Fact]
    public void Maps_to_positional_record_via_constructor()
    {
        var record = _mapper.ToRecord(new Person { Id = 99, Name = "Grace" });
        Assert.Equal(99, record.Id);
        Assert.Equal("Grace", record.Name);
    }

    [Fact]
    public void Null_source_returns_default()
    {
        Assert.Null(_mapper.ToDto(null!));
    }
}
