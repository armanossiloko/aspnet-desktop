using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace AspNetDesktop.Hosting.Routing;

/// <summary>
/// Endpoint metadata restricting an endpoint to the given desktop schemes.
/// </summary>
public interface IDesktopSchemeMetadata
{
    /// <summary>The schemes the endpoint answers on.</summary>
    DesktopScheme Schemes { get; }
}

/// <summary>
/// Restricts a controller, action or endpoint to the given desktop schemes.
/// </summary>
/// <remarks>
/// Without this attribute, endpoints answer on <c>ipc://</c> only, and fallback endpoints
/// (<c>MapFallbackToFile</c>, <c>MapFallback</c>) answer on <c>app://</c> only.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireDesktopSchemeAttribute(DesktopScheme schemes) : Attribute, IDesktopSchemeMetadata
{
    /// <inheritdoc />
    public DesktopScheme Schemes { get; } = schemes;
}

/// <summary>
/// Keeps the API off the UI origin and the SPA fallback off the API origin:
/// a relative <c>fetch('/api/x')</c> from <c>app://</c> never reaches a controller, and an unknown
/// <c>ipc://localhost/api/...</c> route returns 404 instead of <c>index.html</c>.
/// </summary>
internal sealed class DesktopSchemeMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => -100;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => endpoints.Count > 0;

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        DesktopScheme? scheme = httpContext.Features.Get<IDesktopRequestFeature>()?.Scheme;
        if (scheme is null)
        {
            // Not a desktop request (e.g. the app is hosted by another server in tests).
            return Task.CompletedTask;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates.IsValidCandidate(i) && (GetSchemes(candidates[i].Endpoint) & scheme.Value) == 0)
            {
                candidates.SetValidity(i, false);
            }
        }

        return Task.CompletedTask;
    }

    internal static DesktopScheme GetSchemes(Endpoint endpoint)
    {
        IDesktopSchemeMetadata? metadata = endpoint.Metadata.GetMetadata<IDesktopSchemeMetadata>();
        if (metadata is not null)
        {
            return metadata.Schemes;
        }

        // MapFallback* endpoints are the only ones registered with Order = int.MaxValue.
        return endpoint is RouteEndpoint { Order: int.MaxValue } ? DesktopScheme.App : DesktopScheme.Ipc;
    }
}
