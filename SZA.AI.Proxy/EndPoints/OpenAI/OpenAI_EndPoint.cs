using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.OpenAI
{
    public class OpenAI_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/chat/completions", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/completions", (IOllama_Service s, HttpRequest req, HttpResponse res, CancellationToken ct) => s.StreamNdjsonToSseHandler(req, res, ct));
            app.MapPost("/embeddings", (IOllama_Service s, HttpRequest req) => s.SimpleEchoJsonHandler(req));
            app.MapGet("/models", async (IModel_Registry models, IModel_Sync sync, CancellationToken ct) =>
            {
                await sync.SyncAllAsync(ct);
                var all = await models.GetAllAsync(ct);
                return Results.Json(new { @object = "list", data = all.Select(m => new { id = m.LocalName, @object = "model", created = 0, owned_by = "sza" }).ToArray() });
            });
        }
    }
}
