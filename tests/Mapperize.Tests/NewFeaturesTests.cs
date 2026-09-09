using Mapperize;
using Mapperize.Tests.Model;
using Xunit;

namespace Mapperize.Tests;

// ---------------------------------------------------------------------------
// Alternative ways to use Mapperize: static methods, extension methods,
// map-into-existing-instance ("update") methods, and nested mapper types.
// ---------------------------------------------------------------------------

[Mapper]
public static partial class StaticMapper
{
    // Called as StaticMapper.ToOrder(order) — no instance required.
    public static partial OrderDto ToOrder(Order order);

    // Called fluently as address.ToDto() — an extension method.
    public static partial AddressDto ToDto(this Address address);

    // A static method that needs nested/collection helpers — the generated Map_N helpers
    // must therefore also be static so they are callable from this static context.
    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.SecretNote))]
    public static partial UserDto ToDto(this User user);
}

[Mapper]
public partial class UpdateMapper
{
    // void update: populate an existing target in place.
    public partial void Apply(Order source, OrderDto target);

    // fluent update: populate and return the same target.
    public partial OrderDto Merge(Order source, OrderDto target);

    [MapProperty(nameof(User.FullName), nameof(UserDto.Name))]
    [MapperIgnoreTarget(nameof(UserDto.SecretNote))]
    public partial void ApplyUser(User source, UserDto target);

    // Target has an init-only member (Id) that an update cannot assign; only Name is updated.
    [MapperIgnoreTarget(nameof(InitTarget.Id))]
    public partial void ApplyInit(Person source, InitTarget target);
}

[Mapper]
public partial class OverloadMapper
{
    // A transform and an update that share a name AND source/target type pair — both must be emitted.
    public partial OrderDto Map(Order source);
    public partial OrderDto Map(Order source, OrderDto target);
}

[Mapper]
public partial class DictMapper
{
    public partial CatalogDto ToDto(Catalog catalog);
}

[Mapper]
public partial class StructMapper
{
    public partial HasPointDto ToDto(HasPoint source);
}

public partial class Outer
{
    [Mapper]
    public partial class NestedMapper
    {
        public partial OrderDto ToDto(Order order);
    }
}

public class NewFeaturesTests
{
    [Fact]
    public void Static_method_maps_without_instance()
    {
        var dto = StaticMapper.ToOrder(new Order { Id = 3, Total = 5m });
        Assert.Equal(3, dto.Id);
        Assert.Equal(5m, dto.Total);
    }

    [Fact]
    public void Extension_method_maps_fluently()
    {
        var address = new Address { Street = "Main", City = "Metropolis" };
        var dto = address.ToDto();          // extension-method call syntax
        Assert.Equal("Main", dto.Street);
        Assert.Equal("Metropolis", dto.City);
    }

    [Fact]
    public void Static_extension_maps_nested_via_static_helpers()
    {
        var user = new User
        {
            FullName = "Grace",
            Address = new Address { City = "London" },
            Orders = new List<Order> { new() { Id = 1 } },
        };
        var dto = user.ToDto();             // static extension using static Map_N helpers
        Assert.Equal("Grace", dto.Name);
        Assert.Equal("London", dto.Address!.City);
        Assert.Single(dto.Orders);
    }

    [Fact]
    public void Update_populates_existing_instance()
    {
        var mapper = new UpdateMapper();
        var target = new OrderDto { Id = 99, Total = 0m };
        mapper.Apply(new Order { Id = 1, Total = 42m }, target);
        Assert.Equal(1, target.Id);
        Assert.Equal(42m, target.Total);
    }

    [Fact]
    public void Update_returns_same_instance_when_non_void()
    {
        var mapper = new UpdateMapper();
        var target = new OrderDto();
        var returned = mapper.Merge(new Order { Id = 7, Total = 1m }, target);
        Assert.Same(target, returned);
        Assert.Equal(7, returned.Id);
    }

    [Fact]
    public void Update_honors_rename_and_ignore()
    {
        var mapper = new UpdateMapper();
        var target = new UserDto { SecretNote = "keep-me" };
        mapper.ApplyUser(new User { FullName = "Ada", SecretNote = "leak" }, target);
        Assert.Equal("Ada", target.Name);       // renamed
        Assert.Equal("keep-me", target.SecretNote); // ignored -> untouched
    }

    [Fact]
    public void Update_skips_init_only_members()
    {
        var mapper = new UpdateMapper();
        var target = new InitTarget { Id = 5, Name = "old" };
        mapper.ApplyInit(new Person { Id = 9, Name = "new" }, target);
        Assert.Equal(5, target.Id);      // init-only: left untouched by the update
        Assert.Equal("new", target.Name); // regular setter: updated
    }

    [Fact]
    public void Update_null_target_is_a_noop()
    {
        var mapper = new UpdateMapper();
        mapper.Apply(new Order { Id = 1 }, null!); // must not throw
    }

    [Fact]
    public void Transform_and_update_overloads_coexist()
    {
        var mapper = new OverloadMapper();
        var created = mapper.Map(new Order { Id = 1, Total = 3m });
        Assert.Equal(1, created.Id);

        var target = new OrderDto();
        var same = mapper.Map(new Order { Id = 2, Total = 4m }, target);
        Assert.Same(target, same);
        Assert.Equal(2, target.Id);
    }

    [Fact]
    public void Dictionary_is_mapped_with_element_conversion()
    {
        var mapper = new DictMapper();
        var dto = mapper.ToDto(new Catalog
        {
            Items = { ["a"] = new Order { Id = 1, Total = 1m }, ["b"] = new Order { Id = 2, Total = 2m } },
        });
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(1, dto.Items["a"].Id);
        Assert.Equal(2m, dto.Items["b"].Total);
    }

    [Fact]
    public void Nullable_complex_struct_keeps_its_value()
    {
        var mapper = new StructMapper();
        var dto = mapper.ToDto(new HasPoint { Location = new Point { X = 3, Y = 4 } });
        Assert.NotNull(dto.Location);
        Assert.Equal(3, dto.Location!.Value.X);
        Assert.Equal(4, dto.Location.Value.Y);
    }

    [Fact]
    public void Nullable_complex_struct_null_is_preserved()
    {
        var mapper = new StructMapper();
        var dto = mapper.ToDto(new HasPoint { Location = null });
        Assert.Null(dto.Location);
    }

    [Fact]
    public void Nested_mapper_type_is_supported()
    {
        var mapper = new Outer.NestedMapper();
        var dto = mapper.ToDto(new Order { Id = 5, Total = 9m });
        Assert.Equal(5, dto.Id);
        Assert.Equal(9m, dto.Total);
    }
}
