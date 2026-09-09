using Mapperize;
using Mapperize.Tests.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mapperize.Tests;

// A mapper that also exposes an interface (IInjectableMapper) for injection and mocking.
[Mapper(GenerateInterface = true)]
public partial class InjectableMapper
{
    public partial OrderDto ToDto(Order order);
    public partial void Update(Order source, OrderDto target);
}

// A plain mapper (no interface) — still registered as its concrete type.
[Mapper]
public partial class PlainMapper
{
    public partial OrderDto ToDto(Order order);
}

public class DependencyInjectionTests
{
    [Fact]
    public void AddMapperize_registers_concrete_mapper()
    {
        var provider = new ServiceCollection().AddMapperize().BuildServiceProvider();
        var mapper = provider.GetRequiredService<PlainMapper>();
        Assert.Equal(3, mapper.ToDto(new Order { Id = 3 }).Id);
    }

    [Fact]
    public void AddMapperize_registers_generated_interface()
    {
        var provider = new ServiceCollection().AddMapperize().BuildServiceProvider();
        IInjectableMapper mapper = provider.GetRequiredService<IInjectableMapper>();
        Assert.Equal(5, mapper.ToDto(new Order { Id = 5 }).Id);
    }

    [Fact]
    public void Interface_and_concrete_share_the_same_singleton()
    {
        var provider = new ServiceCollection().AddMapperize().BuildServiceProvider();
        var viaInterface = provider.GetRequiredService<IInjectableMapper>();
        var viaConcrete = provider.GetRequiredService<InjectableMapper>();
        Assert.Same(viaConcrete, viaInterface);
    }

    [Fact]
    public void AddMapperize_honors_requested_lifetime()
    {
        var provider = new ServiceCollection()
            .AddMapperize(ServiceLifetime.Transient)
            .BuildServiceProvider();
        Assert.NotSame(
            provider.GetRequiredService<PlainMapper>(),
            provider.GetRequiredService<PlainMapper>());
    }

    [Fact]
    public void Generated_interface_exposes_all_public_instance_methods()
    {
        // Both the transform and the update method are reachable through the interface.
        IInjectableMapper mapper = new InjectableMapper();
        var target = new OrderDto();
        mapper.Update(new Order { Id = 9, Total = 2m }, target);
        Assert.Equal(9, target.Id);
        Assert.Equal(2m, target.Total);
    }
}
