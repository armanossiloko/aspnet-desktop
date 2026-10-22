using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace VueApp;

public sealed record AppMetadata(string Name, string Version, string Environment, string Runtime, string OperatingSystem);

public sealed record Order(int Id, string Product, int Quantity, DateTimeOffset CreatedAt);

public sealed class CreateOrderRequest
{
    [Required]
    [StringLength(60, MinimumLength = 2)]
    public string? Product { get; set; }

    [Range(1, 99)]
    public int Quantity { get; set; } = 1;
}

/// <summary>In-memory storage; a real app would use EF Core, SQLite, files, ...</summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, Order> _orders = new();
    private int _nextId;

    public OrderStore()
    {
        Add("Flat white", 2);
        Add("Croissant", 1);
    }

    public IReadOnlyList<Order> All() => [.. _orders.Values.OrderBy(order => order.Id)];

    public Order? Find(int id) => _orders.GetValueOrDefault(id);

    public Order Add(string product, int quantity)
    {
        Order order = new(Interlocked.Increment(ref _nextId), product, quantity, DateTimeOffset.Now);
        _orders[order.Id] = order;
        return order;
    }

    public bool Remove(int id) => _orders.TryRemove(id, out _);
}
