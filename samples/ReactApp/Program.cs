using System.Runtime.InteropServices;
using ReactApp;

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Desktop apps can be launched from any working directory: load appsettings and wwwroot from the install folder.
    ContentRootPath = AppContext.BaseDirectory,
    // Release builds always show the built UI from app://, even when a launch profile sets Development
    // (which would load Desktop:DevServerUrl instead).
//-:cnd:noEmit
#if !DEBUG
    EnvironmentName = Environments.Production,
#endif
//+:cnd:noEmit
});

builder.AddDesktopHost();          // replaces Kestrel; no TCP bind
builder.Services.AddControllers();
builder.Services.AddSingleton<OrderStore>();

WebApplication app = builder.Build();
app.UseStaticFiles();              // React (Vite) dist served from app://
app.MapControllers();              // real MVC on ipc://
app.MapGet("/api/metadata", (IHostEnvironment environment) => new AppMetadata(
    Name: "ReactApp",
    Version: typeof(AppMetadata).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
    Environment: environment.EnvironmentName,
    Runtime: RuntimeInformation.FrameworkDescription,
    OperatingSystem: RuntimeInformation.OSDescription));
app.MapFallbackToFile("index.html");
app.RunDesktop();
