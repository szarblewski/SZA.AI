using System.Net.Http;
using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Providers
{
    // Forwards an incoming request to the correct provider and streams the response back to the client.
    public interface IProviderClient
    {
        Task ForwardAsync(HttpRequest req, HttpResponse res, CancellationToken ct = default);

        // Lists model names exposed by the provider (via the provider's mapped "/api/tags" route).
        // Returns null when the provider has no mapping for "/api/tags".
        Task<IReadOnlyList<string>?> ListModelsAsync(ProviderConfig provider, CancellationToken ct = default);
    }
}
