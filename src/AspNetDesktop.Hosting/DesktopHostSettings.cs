using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AspNetDesktop.Hosting;

/// <summary>
/// The effective, validated desktop host configuration computed from <see cref="DesktopHostOptions"/> and the
/// hosting environment. Shared by the server (request policy) and the platform adapters (window, navigation).
/// </summary>
public sealed class DesktopHostSettings
{
    private static readonly string[] s_externalSchemes = ["http", "https", "mailto", "tel"];

    private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _uiOrigins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the settings.</summary>
    public DesktopHostSettings(IOptions<DesktopHostOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        DesktopHostOptions value = options.Value;
        Options = value;

        Title = string.IsNullOrWhiteSpace(value.Title) ? environment.ApplicationName : value.Title;
        DevToolsEnabled = value.EnableDevTools ?? environment.IsDevelopment();
        AllowRequestsWithoutOrigin = value.AllowRequestsWithoutOrigin;
        UserDataFolder = string.IsNullOrWhiteSpace(value.UserDataFolder)
            ? Path.Combine(GetLocalDataRoot(), SanitizeFolderName(environment.ApplicationName), "WebView")
            : Path.GetFullPath(value.UserDataFolder);

        _allowedOrigins.Add(DesktopSchemes.AppOrigin);
        _uiOrigins.Add(DesktopSchemes.AppOrigin);

        foreach (string origin in value.AllowedOrigins)
        {
            string normalized = DesktopSchemes.NormalizeOrigin(origin)
                ?? throw Invalid($"'{origin}' in {nameof(DesktopHostOptions.AllowedOrigins)} is not an absolute origin such as http://localhost:4200.");
            if (normalized != DesktopSchemes.IpcOrigin)
            {
                _allowedOrigins.Add(normalized);
            }
        }

        if (environment.IsDevelopment() && !string.IsNullOrWhiteSpace(value.DevServerUrl))
        {
            if (!Uri.TryCreate(value.DevServerUrl, UriKind.Absolute, out Uri? devServer)
                || (devServer.Scheme != Uri.UriSchemeHttp && devServer.Scheme != Uri.UriSchemeHttps))
            {
                throw Invalid($"{nameof(DesktopHostOptions.DevServerUrl)} '{value.DevServerUrl}' must be an absolute http(s) URL.");
            }

            DevServerUri = devServer;
            string devOrigin = DesktopSchemes.GetOrigin(devServer);
            _allowedOrigins.Add(devOrigin);
            _uiOrigins.Add(devOrigin);
            StartUrl = devServer;
        }
        else
        {
            string startPath = string.IsNullOrEmpty(value.StartPath) ? "/" : value.StartPath;
            StartUrl = new Uri(DesktopSchemes.AppBaseUri, startPath.StartsWith('/') ? startPath : "/" + startPath);
        }

        if (value.RequireRequestToken)
        {
            RequestToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }
    }

    /// <summary>The raw options.</summary>
    public DesktopHostOptions Options { get; }

    /// <summary>The window title.</summary>
    public string Title { get; }

    /// <summary>The URL the WebView loads at startup.</summary>
    public Uri StartUrl { get; }

    /// <summary>The dev server in use, when running in Development with <see cref="DesktopHostOptions.DevServerUrl"/>.</summary>
    public Uri? DevServerUri { get; }

    /// <summary>Whether WebView developer tools are enabled.</summary>
    public bool DevToolsEnabled { get; }

    /// <summary>Whether <c>ipc://</c> requests without an <c>Origin</c> header are accepted.</summary>
    public bool AllowRequestsWithoutOrigin { get; }

    /// <summary>The WebView profile folder.</summary>
    public string UserDataFolder { get; }

    /// <summary>The per-process request token, when <see cref="DesktopHostOptions.RequireRequestToken"/> is set.</summary>
    public string? RequestToken { get; }

    /// <summary>Origins allowed to call <c>ipc://localhost</c> (normalized, e.g. <c>app://localhost</c>).</summary>
    public IReadOnlyCollection<string> AllowedOrigins => _allowedOrigins;

    /// <summary>Returns whether <paramref name="origin"/> may call <c>ipc://localhost</c>.</summary>
    public bool IsOriginAllowed(string? origin)
    {
        string? normalized = DesktopSchemes.NormalizeOrigin(origin);
        return normalized is not null && _allowedOrigins.Contains(normalized);
    }

    /// <summary>Returns whether <paramref name="uri"/> belongs to a UI origin (<c>app://localhost</c> or the dev server).</summary>
    public bool IsUiOrigin(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.IsAbsoluteUri && _uiOrigins.Contains(DesktopSchemes.GetOrigin(uri));
    }

    /// <summary>
    /// Decides what to do with a top-level navigation or new-window request to <paramref name="uri"/>.
    /// UI origins stay in the WebView, web links open in the system browser, everything else is blocked.
    /// </summary>
    public DesktopNavigationDecision EvaluateNavigation(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            return DesktopNavigationDecision.Block;
        }

        if (IsUiOrigin(uri) || string.Equals(uri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return DesktopNavigationDecision.Allow;
        }

        return s_externalSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? DesktopNavigationDecision.OpenExternal
            : DesktopNavigationDecision.Block;
    }

    /// <summary>
    /// Creates the script injected at document start that publishes <c>window.__ASPNETDESKTOP__</c>.
    /// The request token is only exposed to documents on a UI origin.
    /// </summary>
    public string CreateBootstrapScript(string platform)
    {
        ArgumentException.ThrowIfNullOrEmpty(platform);

        Dictionary<string, string> config = new()
        {
            ["ipcBase"] = DesktopSchemes.IpcOrigin,
            ["appBase"] = DesktopSchemes.AppOrigin,
            ["platform"] = platform,
        };

        StringBuilder script = new();
        script.Append("(function(){var c=")
            .Append(JsonSerializer.Serialize(config, BootstrapJsonContext.Default.DictionaryStringString))
            .Append(';');

        if (RequestToken is not null)
        {
            script.Append("if(")
                .Append(JsonSerializer.Serialize(_uiOrigins.ToArray(), BootstrapJsonContext.Default.StringArray))
                .Append(".indexOf(window.location.origin)>=0){c.token=")
                .Append(JsonSerializer.Serialize(RequestToken, BootstrapJsonContext.Default.String))
                .Append(";}");
        }

        script.Append("Object.defineProperty(window,'__ASPNETDESKTOP__',{value:Object.freeze(c),writable:false,configurable:false});})();");
        return script.ToString();
    }

    private static OptionsValidationException Invalid(string message) =>
        new(DesktopHostOptions.SectionName, typeof(DesktopHostOptions), [message]);

    private static string GetLocalDataRoot()
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(path) ? Path.GetTempPath() : path;
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "AspNetDesktop";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(ch => invalid.Contains(ch) ? '_' : ch)]);
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(string))]
internal sealed partial class BootstrapJsonContext : JsonSerializerContext;
