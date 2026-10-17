using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Providers
{
    public class Models_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet("/providers/{id}/models", async (string id, IProvider_Registry providers, IModel_Registry models, CancellationToken ct) =>
                await providers.GetByIdAsync(id, ct) is null
                    ? Results.NotFound()
                    : Results.Ok(await models.GetByProviderAsync(id, ct)));

            app.MapPost("/providers/{id}/models", (string id, ModelConfig model, IProvider_Registry providers, IModel_Registry models, CancellationToken ct) =>
                Upsert(id, model, providers, models, ct));

            app.MapPut("/providers/{id}/models", (string id, ModelConfig model, IProvider_Registry providers, IModel_Registry models, CancellationToken ct) =>
                Upsert(id, model, providers, models, ct));

            app.MapDelete("/providers/{id}/models/{*localName}", async (string id, string localName, IModel_Registry models, CancellationToken ct) =>
                await models.DeleteAsync(id, localName, ct) ? Results.NoContent() : Results.NotFound());

            // Imports the models exposed by the provider itself (uses the provider's mapped "/api/tags" route).
            app.MapPost("/providers/{id}/models/import", async (string id, IProvider_Registry providers, IModel_Sync sync, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var provider = await providers.GetByIdAsync(id, ct);
                if (provider is null) return Results.NotFound();

                try
                {
                    var result = await sync.ImportAsync(provider, ct);
                    return result is null
                        ? Results.BadRequest("Provider has no mapping for the \"/api/tags\" route (map it to the provider's models endpoint).")
                        : Results.Ok(new { found = result.Found, imported = result.Imported.Count, models = result.Imported });
                }
                catch (HttpRequestException ex)
                {
                    loggerFactory.CreateLogger<Models_EndPoint>().LogError(ex, "Model import failed for provider {ProviderId}", id);
                    return Results.Json(new { error = "Provider request failed." }, statusCode: StatusCodes.Status502BadGateway);
                }
            });
        }

        private static async Task<IResult> Upsert(string id, ModelConfig model, IProvider_Registry providers, IModel_Registry models, CancellationToken ct)
        {
            if (await providers.GetByIdAsync(id, ct) is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(model.LocalName)) return Results.BadRequest("LocalName is required.");

            model.ProviderId = id;
            if (string.IsNullOrWhiteSpace(model.RemoteName)) model.RemoteName = model.LocalName;

            return Results.Ok(await models.UpsertAsync(model, ct));
        }
    }
}
