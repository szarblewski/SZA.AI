using System.Text;
using SZA.AI.Proxy.Providers;

namespace SZA.AI.Proxy.Services
{
    public class Ollama_Service : IOllama_Service
    {
        private readonly IProviderClient _providerClient;

        public Ollama_Service(IProviderClient providerClient)
        {
            _providerClient = providerClient;
        }

        // Forwards to the correct provider; provider SSE is streamed back to the client as NDJSON.
        public Task StreamNdjsonToSseHandler(HttpRequest req, HttpResponse res, CancellationToken ct)
            => _providerClient.ForwardAsync(req, res, ct);

        public async Task<IResult> SimpleEchoJsonHandler(HttpRequest req)
        {
            // Read body and echo back as JSON. If empty, return 204.
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms).ConfigureAwait(false);
            if (ms.Length == 0) return Results.NoContent();
            ms.Seek(0, SeekOrigin.Begin);
            using var sr = new StreamReader(ms, Encoding.UTF8);
            var body = await sr.ReadToEndAsync().ConfigureAwait(false);
            return Results.Content(body, "application/json");
        }
    }
}
