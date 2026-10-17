using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Ollama
{
    public class Get_Ollama_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            // GET endpoints (return sample JSON placeholders)
            app.MapGet("/api/tags", async (IModel_Registry models, CancellationToken ct) =>
            {
                var all = await models.GetAllAsync(ct);
                return Results.Json(new
                {
                    models = all.Where(m => m.Active).Select(m => new { name = m.LocalName, model = m.LocalName })
                });
            }).WithOpenApi();
            app.MapGet("/api/ps", () => Results.Json(new { ps = Array.Empty<object>() })).WithOpenApi();
            app.MapGet("/api/version", () => Results.Json(new { version = "1.0" })).WithOpenApi();
            app.MapGet("/api/usage", () => Results.Json(new { usage = 0 })).WithOpenApi();
            app.MapGet("/api/balance", () => Results.Json(new { balance = 0 })).WithOpenApi();
        }   
    
    }
}
