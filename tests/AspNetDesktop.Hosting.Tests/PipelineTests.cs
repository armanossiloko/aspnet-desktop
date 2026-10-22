using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace AspNetDesktop.Hosting.Tests;

public sealed class PipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Server_ReplacesKestrel_AndAdvertisesDesktopSchemes()
    {
        await using TestHost host = await TestHost.StartAsync();

        IServer server = host.App.Services.GetRequiredService<IServer>();
        Assert.IsType<DesktopServer>(server);
        Assert.Equal(["app://localhost/", "ipc://localhost/"], server.Features.Get<IServerAddressesFeature>()!.Addresses);
    }

    [Fact]
    public async Task Server_DoesNotBindConfiguredUrls()
    {
        int port = GetFreePort();
        await using TestHost host = await TestHost.StartAsync(build: b => b.WebHost.UseUrls($"http://127.0.0.1:{port}"));

        // If anything had bound the port, this would throw.
        using TcpListener listener = new(IPAddress.Loopback, port);
        listener.Start();
        listener.Stop();
    }

    [Fact]
    public async Task MinimalApi_Get_ReturnsJson()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/metadata", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Metadata? metadata = await response.Content.ReadFromJsonAsync<Metadata>(Ct);
        Assert.Equal(new Metadata("TestApp", "1.0.0", "Production"), metadata);
    }

    [Fact]
    public async Task MinimalApi_BindsQueryString()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        JsonElement result = await client.GetFromJsonAsync<JsonElement>("/api/search?q=caf%C3%A9&page=3", Ct);

        Assert.Equal("café", result.GetProperty("q").GetString());
        Assert.Equal(3, result.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Request_SeesIpcSchemeHostAndHeaders()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/echo-headers");
        request.Headers.Add("X-Test", "hello");

        JsonElement result = await (await client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.Equal("hello", result.GetProperty("value").GetString());
        Assert.Equal("ipc", result.GetProperty("scheme").GetString());
        Assert.Equal("localhost", result.GetProperty("host").GetString());
    }

    [Fact]
    public async Task Controller_Post_BindsJsonBody_AndReturnsCreatedWithIpcLocation()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/orders", new { product = "Tea", quantity = 2 }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Uri("ipc://localhost/api/orders/42"), response.Headers.Location);
        Assert.Equal(new Order(42, "Tea", 2), await response.Content.ReadFromJsonAsync<Order>(Ct));
    }

    [Fact]
    public async Task Controller_InvalidModel_ReturnsValidationProblem()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/orders", new { quantity = 1000 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(problem.GetProperty("errors").TryGetProperty("Product", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("Quantity", out _));
    }

    [Fact]
    public async Task Controller_PutDeleteAndNotFound_Work()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage put = await client.PutAsJsonAsync("/api/orders/7", new { product = "Milk", quantity = 3 }, Ct);
        HttpResponseMessage delete = await client.DeleteAsync("/api/orders/7", Ct);
        HttpResponseMessage missing = await client.GetAsync("/api/orders/404", Ct);

        Assert.Equal(new Order(7, "Milk", 3), await put.Content.ReadFromJsonAsync<Order>(Ct));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_Returns500()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/throw", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task OnStartingCallbacks_RunBeforeHeadersAreFrozen()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/on-starting", Ct);

        Assert.Equal("fired", Assert.Single(response.Headers.GetValues("X-On-Starting")));
        Assert.Equal("hello", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(5, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task Head_ReturnsHeadersWithoutBody()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient(DesktopScheme.App);

        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/main.js"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task StaticFiles_AreServedOnAppScheme()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient(DesktopScheme.App);

        HttpResponseMessage response = await client.GetAsync("/main.js", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("console.log('main');", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task SpaFallback_ServesIndexOnAppScheme()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient(DesktopScheme.App);

        HttpResponseMessage root = await client.GetAsync("/", Ct);
        HttpResponseMessage deepLink = await client.GetAsync("/orders/42", Ct);

        Assert.Contains("<title>spa</title>", await root.Content.ReadAsStringAsync(Ct));
        Assert.Contains("<title>spa</title>", await deepLink.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task SpaFallback_IsNotServedOnIpcScheme()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoints_AreNotReachableFromAppScheme()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient(DesktopScheme.App);

        HttpResponseMessage response = await client.GetAsync("/api/metadata", Ct);

        // The relative call lands on the SPA fallback, never on the API.
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AppScheme_RejectsNonGetMethods()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient client = host.CreateClient(DesktopScheme.App);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/orders", new { product = "Tea", quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("GET, HEAD", response.Content.Headers.Allow.Count > 0 ? string.Join(", ", response.Content.Headers.Allow) : null);
    }

    [Fact]
    public async Task RequireDesktopScheme_OptsEndpointIntoBothSchemes()
    {
        await using TestHost host = await TestHost.StartAsync();
        using HttpClient app = host.CreateClient(DesktopScheme.App);
        using HttpClient ipc = host.CreateClient();

        Assert.Equal("both", await app.GetStringAsync("/api/both", Ct));
        Assert.Equal("both", await ipc.GetStringAsync("/api/both", Ct));
    }

    [Fact]
    public async Task UnknownSchemeOrHost_Returns404()
    {
        await using TestHost host = await TestHost.StartAsync();

        DesktopHttpResponse otherHost = await host.Server.ProcessAsync(
            new DesktopHttpRequest { Method = "GET", Uri = new Uri("ipc://example.com/api/metadata") }, Ct);
        DesktopHttpResponse otherScheme = await host.Server.ProcessAsync(
            new DesktopHttpRequest { Method = "GET", Uri = new Uri("file://localhost/api/metadata") }, Ct);

        Assert.Equal(404, otherHost.StatusCode);
        Assert.Equal(404, otherScheme.StatusCode);
    }

    [Fact]
    public async Task ProcessAsync_AcceptsAdapterStyleBodyWithoutContentLength()
    {
        await using TestHost host = await TestHost.StartAsync();
        HeaderDictionary headers = new() { ["Content-Type"] = "application/json", ["Origin"] = "app://localhost" };

        DesktopHttpResponse response = await host.Server.ProcessAsync(new DesktopHttpRequest
        {
            Method = "POST",
            Uri = new Uri("ipc://localhost/api/orders"),
            Headers = headers,
            Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"product":"Juice","quantity":4}""")),
        }, Ct);

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("Created", response.ReasonPhrase);
        Order? order = JsonSerializer.Deserialize<Order>(response.Body.Span, JsonSerializerOptions.Web);
        Assert.Equal(new Order(42, "Juice", 4), order);
        Assert.Equal(response.Body.Length, response.Headers.ContentLength);
    }

    [Fact]
    public async Task StoppedServer_Returns503()
    {
        TestHost host = await TestHost.StartAsync();
        DesktopServer server = host.Server;
        await host.DisposeAsync();

        DesktopHttpResponse response = await server.ProcessAsync(
            new DesktopHttpRequest { Method = "GET", Uri = new Uri("ipc://localhost/api/metadata") }, Ct);

        Assert.Equal(503, response.StatusCode);
    }

    [Fact]
    public async Task RequestTimeouts_ReplaceRequestAborted_AndTimeOutWith504()
    {
        await using TestHost host = await TestHost.StartAsync(
            build: builder => builder.Services.AddRequestTimeouts(),
            map: app =>
            {
                // UseRequestTimeouts() assigns HttpContext.RequestAborted, which the server must allow.
                app.UseRequestTimeouts();
                app.MapGet("/api/fast", () => "fast").WithRequestTimeout(TimeSpan.FromSeconds(30));
                app.MapGet("/api/slow", async (HttpContext context) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
                    return "slow";
                }).WithRequestTimeout(TimeSpan.FromMilliseconds(100));
            });
        HttpClient client = host.CreateClient();

        HttpResponseMessage fast = await client.GetAsync("/api/fast", Ct);
        HttpResponseMessage slow = await client.GetAsync("/api/slow", Ct);

        Assert.Equal(HttpStatusCode.OK, fast.StatusCode);
        Assert.Equal("fast", await fast.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.GatewayTimeout, slow.StatusCode);
    }

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
