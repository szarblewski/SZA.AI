using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Services;
using System.Linq;

namespace SZA.AI.Proxy.EndPoints.Ollama
{
    public class Get_Ollama_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            // GET endpoints (return sample JSON placeholders)
            app.MapGet("/tags", async (IModel_Registry models, IModel_Sync sync, CancellationToken ct) =>
            {
                await sync.SyncAllAsync(ct);
                var all = await models.GetAllAsync(ct);
                return Results.Json(new { models = all.Select(m => new { name = m.LocalName, model = m.LocalName, modified_at = DateTime.UtcNow, size = 0L, digest = m.LocalName, details = new { family = "sza", format = "api", parameter_size = string.Empty, quantization_level = string.Empty } }).ToArray() });
            });
            app.MapGet("/ps", () => Results.Json(new { models = Array.Empty<object>() }));
            app.MapGet("/version", () => Results.Json(new { version = "1.0" }));
            app.MapGet("/usage", () => Results.Json(new { usage = 0 }));
            app.MapGet("/balance", () => Results.Json(new { balance = 0 }));
        }   
    
    }
}
