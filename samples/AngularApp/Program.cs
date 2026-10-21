using System.Runtime.InteropServices;
using AngularApp;

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Desktop apps can be launched from any working directory: load appsettings and wwwroot from the install folder.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.AddDesktopHost();          // replaces Kestrel; no TCP bind
builder.Services.AddControllers();
builder.Services.AddSingleton<OrderStore>();

WebApplication app = builder.Build();
app.UseStaticFiles();              // Angular dist served from app://
app.MapControllers();              // real MVC on ipc://
app.MapGet("/api/metadata", (IHostEnvironment environment) => new AppMetadata(
    Name: "AngularApp",
    Version: typeof(AppMetadata).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
    Environment: environment.EnvironmentName,
    Runtime: RuntimeInformation.FrameworkDescription,
    OperatingSystem: RuntimeInformation.OSDescription));
app.MapFallbackToFile("index.html");
app.RunDesktop();
