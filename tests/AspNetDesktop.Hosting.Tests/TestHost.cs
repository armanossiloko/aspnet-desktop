using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AspNetDesktop.Hosting.Tests;

/// <summary>
/// A started desktop application with controllers, minimal APIs, static files and the SPA fallback,
/// served from a temporary content root.
/// </summary>
internal sealed class TestHost : IAsyncDisposable
{
    private readonly string _contentRoot;

    private TestHost(WebApplication app, string contentRoot)
    {
        App = app;
        _contentRoot = contentRoot;
    }

    public WebApplication App { get; }

    public DesktopServer Server => App.GetDesktopServer();

    public HttpClient CreateClient(DesktopScheme scheme = DesktopScheme.Ipc) => App.CreateDesktopClient(scheme);

    public static async Task<TestHost> StartAsync(
        Action<DesktopHostOptions>? configure = null,
        Action<WebApplication>? map = null,
        string environment = "Production",
        Action<WebApplicationBuilder>? build = null)
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "aspnetdesktop-tests", Guid.NewGuid().ToString("N"));
        string webRoot = Path.Combine(contentRoot, "wwwroot");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>spa</title>");
        await File.WriteAllTextAsync(Path.Combine(webRoot, "main.js"), "console.log('main');");

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TestHost).Assembly.GetName().Name,
            ContentRootPath = contentRoot,
            EnvironmentName = environment,
        });

        builder.Logging.ClearProviders();
        builder.AddDesktopHost(configure);
        builder.Services.AddControllers();
        build?.Invoke(builder);

        WebApplication app = builder.Build();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapGet("/api/metadata", (IHostEnvironment env) => new Metadata("TestApp", "1.0.0", env.EnvironmentName));
        app.MapGet("/api/search", (string q, int page) => new { q, page });
        app.MapGet("/api/echo-headers", (HttpRequest request) =>
            Results.Ok(new { value = request.Headers["X-Test"].ToString(), scheme = request.Scheme, host = request.Host.Value }));
        app.MapGet("/api/throw", string () => throw new InvalidOperationException("boom"));
        app.MapGet("/api/on-starting", (HttpContext context) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-On-Starting"] = "fired";
                return Task.CompletedTask;
            });
            return Results.Text("hello");
        });
        app.MapGet("/api/both", () => "both").RequireDesktopScheme(DesktopScheme.App | DesktopScheme.Ipc);
        map?.Invoke(app);
        app.MapFallbackToFile("index.html");

        await app.StartAsync();
        return new TestHost(app, contentRoot);
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed record Metadata(string Name, string Version, string Environment);

public sealed record Order(int Id, string Product, int Quantity);

public sealed class CreateOrderRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? Product { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 100)]
    public int Quantity { get; set; }
}

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("{id:int}")]
    public ActionResult<Order> Get(int id) => id == 404 ? NotFound() : new Order(id, "Coffee", 1);

    [HttpPost]
    public ActionResult<Order> Create(CreateOrderRequest request)
    {
        Order order = new(42, request.Product!, request.Quantity);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:int}")]
    public ActionResult<Order> Update(int id, CreateOrderRequest request) => new Order(id, request.Product!, request.Quantity);

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => NoContent();
}
