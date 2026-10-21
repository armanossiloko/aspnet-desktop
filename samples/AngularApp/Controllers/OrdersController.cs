using Microsoft.AspNetCore.Mvc;

namespace AngularApp.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderStore store) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<Order> List() => store.All();

    [HttpGet("{id:int}")]
    public ActionResult<Order> Get(int id) => store.Find(id) is { } order ? order : NotFound();

    [HttpPost]
    public ActionResult<Order> Create(CreateOrderRequest request)
    {
        Order order = store.Add(request.Product!.Trim(), request.Quantity);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => store.Remove(id) ? NoContent() : NotFound();
}
