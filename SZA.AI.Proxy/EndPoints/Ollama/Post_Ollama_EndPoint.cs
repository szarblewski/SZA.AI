using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Ollama
{
    public class Post_Ollama_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            // Streaming POST endpoints: provider SSE -> client NDJSON, streamed.
            app.MapPost("/api/generate", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/api/chat", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/api/create", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/api/pull", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/api/push", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));

            // Non-streaming POST endpoints (echo back JSON body)
            app.MapPost("/api/embed", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapPost("/v1/systemone", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapPost("/api/show", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapPost("/api/copy", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
        }
    }
}
