using System.Net.Http;

namespace SZA.AI.Proxy.Providers
{
    // Forwards an incoming request to the correct provider and streams the response back to the client.
    public interface IProviderClient
    {
        Task ForwardAsync(HttpRequest req, HttpResponse res, CancellationToken ct = default);
    }
}
