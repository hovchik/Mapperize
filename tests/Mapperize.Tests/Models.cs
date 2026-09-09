namespace Mapperize.Tests.Model;

public enum Status { Active, Inactive, Pending }

// Deliberately different member order to prove name-based (not value-based) enum mapping.
public enum StatusDto { Pending, Active, Inactive }

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public class AddressDto
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

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
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public string SecretNote { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";       // renamed from FullName
    public long Age { get; set; }                 // int -> long (implicit widening)
    public int Ticks { get; set; }                // long -> int (explicit)
    public int Score { get; set; }                // int? -> int
    public StatusDto Status { get; set; }         // enum mapped by name
    public AddressDto? Address { get; set; }      // nested object (auto helper)
    public List<OrderDto> Orders { get; set; } = new(); // nested collection
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public string? SecretNote { get; set; }       // ignored -> stays null
}

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public record PersonRecord(int Id, string Name);

// ---- nullable complex-struct models (X/Y as fields to exercise field mapping) ----
public struct Point
{
    public int X;
    public int Y;
}

public struct PointDto
{
    public int X;
    public int Y;
}

public class HasPoint
{
    public Point? Location { get; set; }
}

public class HasPointDto
{
    public PointDto? Location { get; set; }
}

// ---- init-only target (update methods must not assign init-only members) ----
public class InitTarget
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
}

// ---- dictionary models ----
public class Catalog
{
    public Dictionary<string, Order> Items { get; set; } = new();
}

public class CatalogDto
{
    public Dictionary<string, OrderDto> Items { get; set; } = new();
}
