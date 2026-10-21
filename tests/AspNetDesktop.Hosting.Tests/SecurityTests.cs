using System.Net;
using AspNetDesktop.Hosting;

namespace AspNetDesktop.Hosting.Tests;

public sealed class SecurityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AppOrigin_IsAllowed_AndGetsCorsHeaders()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await SendWithOrigin(client, HttpMethod.Get, "/api/orders/1", "app://localhost");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("app://localhost", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains("Origin", response.Headers.Vary);
    }

    [Fact]
    public async Task CorsExposesNonSafelistedResponseHeaders()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await SendWithOrigin(client, HttpMethod.Get, "/api/on-starting", "app://localhost");

        string exposed = Assert.Single(response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-On-Starting", exposed);
        Assert.DoesNotContain("Content-Type", exposed);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:4200")]
    [InlineData("null")]
    [InlineData("ipc://localhost")]
    public async Task UnlistedOrigin_IsRejected(string origin)
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await SendWithOrigin(client, HttpMethod.Get, "/api/metadata", origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ConfiguredOrigin_IsAllowed()
    {
        await using TestHost host = await TestHost.StartAsync(o => o.AllowedOrigins.Add("http://localhost:5173/"));
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await SendWithOrigin(client, HttpMethod.Get, "/api/metadata", "http://localhost:5173");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DevServerOrigin_IsAllowedOnlyInDevelopment()
    {
        await using TestHost development = await TestHost.StartAsync(o => o.DevServerUrl = "http://localhost:4200", environment: "Development");
        await using TestHost production = await TestHost.StartAsync(o => o.DevServerUrl = "http://localhost:4200");
        using HttpClient devClient = development.CreateClient();
        using HttpClient prodClient = production.CreateClient();

        HttpResponseMessage allowed = await SendWithOrigin(devClient, HttpMethod.Get, "/api/metadata", "http://localhost:4200");
        HttpResponseMessage rejected = await SendWithOrigin(prodClient, HttpMethod.Get, "/api/metadata", "http://localhost:4200");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal(new Uri("http://localhost:4200"), development.Server.Settings.StartUrl);
        Assert.Equal(new Uri("app://localhost/"), production.Server.Settings.StartUrl);
    }

    [Fact]
    public async Task Preflight_IsAnsweredWithoutReachingThePipeline()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Options, "/api/orders");
        request.Headers.Add("Origin", "app://localhost");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type, x-aspnetdesktop-token");

        HttpResponseMessage response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("app://localhost", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")));
        Assert.Equal("content-type, x-aspnetdesktop-token", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Headers")));
    }

    [Fact]
    public async Task MissingOrigin_CanBeRejected()
    {
        await using TestHost host = await TestHost.StartAsync(o => o.AllowRequestsWithoutOrigin = false);
        using HttpClient client = host.CreateClient();

        HttpResponseMessage withoutOrigin = await client.GetAsync("/api/metadata", Ct);
        HttpResponseMessage withOrigin = await SendWithOrigin(client, HttpMethod.Get, "/api/metadata", "app://localhost");

        Assert.Equal(HttpStatusCode.Forbidden, withoutOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withOrigin.StatusCode);
    }

    [Fact]
    public async Task RequestToken_IsEnforced()
    {
        await using TestHost host = await TestHost.StartAsync(o => o.RequireRequestToken = true);
        string token = host.Server.Settings.RequestToken!;

        DesktopHttpResponse withoutToken = await host.Server.ProcessAsync(
            new DesktopHttpRequest { Method = "GET", Uri = new Uri("ipc://localhost/api/metadata") }, Ct);
        DesktopHttpResponse wrongToken = await host.Server.ProcessAsync(new DesktopHttpRequest
        {
            Method = "GET",
            Uri = new Uri("ipc://localhost/api/metadata"),
            Headers = new Microsoft.AspNetCore.Http.HeaderDictionary { [DesktopSchemes.RequestTokenHeader] = "nope" },
        }, Ct);
        DesktopHttpResponse uiAsset = await host.Server.ProcessAsync(
            new DesktopHttpRequest { Method = "GET", Uri = new Uri("app://localhost/main.js") }, Ct);

        // The .NET client adds the token automatically.
        using HttpClient client = host.CreateClient();
        HttpResponseMessage viaClient = await client.GetAsync("/api/metadata", Ct);

        Assert.Equal(64, token.Length);
        Assert.Equal(403, withoutToken.StatusCode);
        Assert.Equal(403, wrongToken.StatusCode);
        Assert.Equal(200, uiAsset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viaClient.StatusCode);
    }

    [Fact]
    public async Task Rejections_ForAllowedOrigins_AreReadableByThePage()
    {
        await using TestHost host = await TestHost.StartAsync(o => o.RequireRequestToken = true);

        DesktopHttpResponse allowedOrigin = await host.Server.ProcessAsync(new DesktopHttpRequest
        {
            Method = "GET",
            Uri = new Uri("ipc://localhost/api/metadata"),
            Headers = new Microsoft.AspNetCore.Http.HeaderDictionary { ["Origin"] = "app://localhost" },
        }, Ct);
        DesktopHttpResponse foreignOrigin = await host.Server.ProcessAsync(new DesktopHttpRequest
        {
            Method = "GET",
            Uri = new Uri("ipc://localhost/api/metadata"),
            Headers = new Microsoft.AspNetCore.Http.HeaderDictionary { ["Origin"] = "https://evil.example" },
        }, Ct);

        Assert.Equal(403, allowedOrigin.StatusCode);
        Assert.Equal("app://localhost", allowedOrigin.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal(403, foreignOrigin.StatusCode);
        Assert.False(foreignOrigin.Headers.ContainsKey("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task BootstrapScript_PublishesConfig_AndGuardsTokenByOrigin()
    {
        await using TestHost plain = await TestHost.StartAsync();
        await using TestHost secured = await TestHost.StartAsync(o => o.RequireRequestToken = true);

        string plainScript = plain.Server.Settings.CreateBootstrapScript("windows");
        string securedScript = secured.Server.Settings.CreateBootstrapScript("windows");

        Assert.Contains("\"ipcBase\":\"ipc://localhost\"", plainScript);
        Assert.Contains("\"platform\":\"windows\"", plainScript);
        Assert.Contains("__ASPNETDESKTOP__", plainScript);
        Assert.DoesNotContain("token", plainScript);
        Assert.Contains($"c.token=\"{secured.Server.Settings.RequestToken}\"", securedScript);
        Assert.Contains("[\"app://localhost\"].indexOf(window.location.origin)", securedScript);
    }

    private static Task<HttpResponseMessage> SendWithOrigin(HttpClient client, HttpMethod method, string path, string origin)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return client.SendAsync(request, Ct);
    }
}
