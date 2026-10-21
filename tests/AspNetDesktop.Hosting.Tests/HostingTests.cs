using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetDesktop.Hosting.Tests;

public sealed class HostingTests
{
    [Fact]
    public void RunDesktop_StartsHost_RunsPlatform_AndStopsHostWhenWindowCloses()
    {
        WebApplication app = CreateApp();
        IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        RecordingPlatform platform = new(context =>
        {
            DesktopHttpResponse response = context.RequestHandler
                .ProcessAsync(new DesktopHttpRequest { Method = "GET", Uri = new Uri("ipc://localhost/ping") })
                .GetAwaiter().GetResult();
            return response.StatusCode;
        });

        app.RunDesktop(platform);

        Assert.Equal(200, platform.Result);
        Assert.Equal(new Uri("app://localhost/"), platform.Context!.Settings.StartUrl);
        Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
    }

    [Fact]
    public void RunDesktop_ReturnsWhenApplicationStops()
    {
        WebApplication app = CreateApp();
        IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        RecordingPlatform platform = new(context =>
        {
            // A real adapter closes its window when the application is asked to stop.
            Task.Run(lifetime.StopApplication);
            context.ApplicationStopping.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            return context.ApplicationStopping.IsCancellationRequested ? 1 : 0;
        });

        app.RunDesktop(platform);

        Assert.Equal(1, platform.Result);
    }

    [Fact]
    public void RunDesktop_WithoutDesktopHost_Throws()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        WebApplication app = builder.Build();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => app.RunDesktop(new RecordingPlatform(_ => 0)));
        Assert.Contains("AddDesktopHost", error.Message);
    }

    [Fact]
    public void Options_AreBoundFromDesktopConfigurationSection()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Desktop:Title"] = "Configured",
            ["Desktop:Width"] = "900",
            ["Desktop:DevServerUrl"] = "http://localhost:5173",
            ["Desktop:AllowedOrigins:0"] = "http://localhost:3000",
        });
        builder.AddDesktopHost(o => o.Height = 600);
        using WebApplication app = builder.Build();

        DesktopHostSettings settings = app.Services.GetRequiredService<DesktopHostSettings>();

        Assert.Equal("Configured", settings.Title);
        Assert.Equal(900, settings.Options.Width);
        Assert.Equal(600, settings.Options.Height);
        Assert.True(settings.DevToolsEnabled);
        Assert.Equal(new Uri("http://localhost:5173"), settings.StartUrl);
        Assert.True(settings.IsOriginAllowed("http://localhost:3000"));
        Assert.True(settings.IsOriginAllowed("http://localhost:5173"));
    }

    [Fact]
    public void InvalidAllowedOrigin_FailsFast()
    {
        Assert.Throws<OptionsValidationException>(() => CreateSettings(o => o.AllowedOrigins.Add("not a url")));
    }

    [Theory]
    [InlineData("app://localhost/orders/1", DesktopNavigationDecision.Allow)]
    [InlineData("about:blank", DesktopNavigationDecision.Allow)]
    [InlineData("http://localhost:4200/home", DesktopNavigationDecision.Allow)]
    [InlineData("https://angular.dev/", DesktopNavigationDecision.OpenExternal)]
    [InlineData("http://localhost:5000/", DesktopNavigationDecision.OpenExternal)]
    [InlineData("mailto:someone@example.com", DesktopNavigationDecision.OpenExternal)]
    [InlineData("ipc://localhost/api/metadata", DesktopNavigationDecision.Block)]
    [InlineData("file:///C:/Windows/win.ini", DesktopNavigationDecision.Block)]
    [InlineData("javascript:alert(1)", DesktopNavigationDecision.Block)]
    public void NavigationPolicy_KeepsWebViewOnUiOrigins(string url, DesktopNavigationDecision expected)
    {
        DesktopHostSettings settings = CreateSettings(o => o.DevServerUrl = "http://localhost:4200", "Development");

        Assert.Equal(expected, settings.EvaluateNavigation(new Uri(url)));
    }

    private static DesktopHostSettings CreateSettings(Action<DesktopHostOptions> configure, string environment = "Production")
    {
        DesktopHostOptions options = new();
        configure(options);
        return new DesktopHostSettings(Options.Create(options), new TestEnvironment(environment));
    }

    private static WebApplication CreateApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.AddDesktopHost();
        WebApplication app = builder.Build();
        app.MapGet("/ping", () => "pong");
        return app;
    }

    private sealed class RecordingPlatform(Func<DesktopPlatformContext, int> run) : IDesktopPlatform
    {
        public string Name => "test";

        public DesktopPlatformContext? Context { get; private set; }

        public int Result { get; private set; }

        public void Run(DesktopPlatformContext context)
        {
            Context = context;
            Result = run(context);
        }
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "TestApp";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
