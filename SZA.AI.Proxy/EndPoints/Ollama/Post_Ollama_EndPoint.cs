using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Ollama
{
    public class Post_Ollama_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            // Streaming POST endpoints: provider SSE -> client NDJSON, streamed.
            app.MapPost("/generate", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/chat", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/create", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/pull", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/push", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));

            // Non-streaming POST endpoints (echo back JSON body)
            app.MapPost("/embed", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapPost("/systemone", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapPost("/show", (IOllama_Service s, HttpRequest req, CancellationToken ct) => s.ShowModelAsync(req, ct));
            app.MapPost("/copy", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
        }
    }
}
