// Example: static mapping methods.
//
// Put the mapping methods in a `static partial class`. There is nothing to construct or inject —
// call them directly as `Maps.ToDto(order)`. Great for pure, stateless conversions.
using Mapperize;

var dto = Maps.ToDto(new Order { Id = 42, Total = 9.99m });

Console.WriteLine($"Maps.ToDto(order) => Id={dto.Id}, Total={dto.Total}");

// Nested/collection helpers the generator emits are also static, so they are callable here.
var basket = Maps.ToBasket(new Basket
{
    Orders = new List<Order> { new() { Id = 1, Total = 1m }, new() { Id = 2, Total = 2m } },
});
Console.WriteLine($"Maps.ToBasket(basket) => {basket.Orders.Count} orders");

return dto is { Id: 42 } && basket.Orders.Count == 2 ? 0 : 1;

// ---- mapper ----------------------------------------------------------------

[Mapper]
public static partial class Maps
{
    public static partial OrderDto ToDto(Order order);
    public static partial BasketDto ToBasket(Basket basket);
}

// ---- models ----------------------------------------------------------------

public class Order { public int Id { get; set; } public decimal Total { get; set; } }
public class OrderDto { public int Id { get; set; } public decimal Total { get; set; } }

public class Basket { public List<Order> Orders { get; set; } = new(); }
public class BasketDto { public List<OrderDto> Orders { get; set; } = new(); }
