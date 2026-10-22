using System.Text.Json;
using AspNetDesktop.Hosting;

// Opens a real window and system WebView, lets wwwroot/index.html exercise app:// and ipc:// with fetch() and the
// aspnetdesktop-client package, and turns the report it posts back into the process exit code.

TimeSpan timeout = TimeSpan.FromSeconds(args.Contains("--no-timeout") ? 3600 : 90);
string expectedPlatform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
string reportPath = Path.Combine(Path.GetTempPath(), "aspnetdesktop-smoke.json");
File.Delete(reportPath);

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = "Production",
});

builder.AddDesktopHost(options =>
{
    options.Title = "AspNetDesktop smoke test";
    options.RequireRequestToken = true;
    options.Width = 900;
    options.Height = 600;
});

WebApplication app = builder.Build();
IHostApplicationLifetime lifetime = app.Lifetime;
bool passed = false;

app.UseStaticFiles();

app.MapGet("/api/metadata", () => Results.Ok(new { name = "smoke", version = "1.0.0", platform = expectedPlatform }));
app.MapPost("/api/orders", (OrderInput input) =>
    Results.Created($"/api/orders/{input.Quantity}", new { id = input.Quantity, input.Product, input.Quantity }));
app.MapPut("/api/orders/{id:int}", (int id, OrderInput input) => Results.Ok(new { id, input.Product }));
app.MapDelete("/api/orders/{id:int}", (int id) => Results.NoContent());
app.MapGet("/api/headers", (HttpContext context) =>
{
    context.Response.Headers["X-Smoke"] = "exposed";
    return Results.Ok(new { echo = context.Request.Headers["X-Echo"].ToString(), origin = context.Request.Headers.Origin.ToString() });
});
app.MapPost("/api/size", async (HttpRequest request) =>
{
    using MemoryStream buffer = new();
    await request.Body.CopyToAsync(buffer);
    return Results.Ok(new { length = buffer.Length });
});
app.MapPost("/api/report", (SmokeReport report) =>
{
    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonSerializerOptions.Web));
    passed = report.Results.Count > 0 && report.Results.All(result => result.Pass);
    lifetime.StopApplication();
    return Results.NoContent();
});
// appsettings.json must ship next to the executable on every OS (inside the .app bundle on macOS).
app.MapGet("/api/config", (IConfiguration configuration) => Results.Ok(new { value = configuration["Smoke:Config"] }));
app.MapFallbackToFile("index.html");

using CancellationTokenSource watchdog = new(timeout);
using CancellationTokenRegistration onTimeout = watchdog.Token.Register(lifetime.StopApplication);

app.RunDesktop();

if (!File.Exists(reportPath))
{
    Console.Error.WriteLine($"FAIL: no report from the {expectedPlatform} WebView within {timeout.TotalSeconds:0}s.");
    return 1;
}

SmokeReport final = JsonSerializer.Deserialize<SmokeReport>(File.ReadAllText(reportPath), JsonSerializerOptions.Web)!;
foreach (SmokeResult result in final.Results)
{
    Console.WriteLine($"{(result.Pass ? "PASS" : "FAIL")}  {result.Name}{(string.IsNullOrEmpty(result.Detail) ? "" : "  - " + result.Detail)}");
}

Console.WriteLine($"WebView: {final.UserAgent}");
Console.WriteLine(passed ? $"All {final.Results.Count} checks passed on {expectedPlatform}." : $"Some checks failed on {expectedPlatform}.");
Console.WriteLine($"Report: {reportPath}");
return passed ? 0 : 1;

internal sealed record OrderInput(string Product, int Quantity);

internal sealed record SmokeResult(string Name, bool Pass, string? Detail);

internal sealed record SmokeReport(string UserAgent, List<SmokeResult> Results);
