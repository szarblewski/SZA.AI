using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Providers
{
    public class Providers_EndPoint : IEndPoint
    {
        public record ApiKeyRequest(string? ApiKey);

        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet("/providers", async (IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
            {
                var providers = await registry.GetAllAsync(ct);
                var result = new List<object>();
                foreach (var provider in providers)
                    result.Add(await ToResponseAsync(provider, secrets, ct));
                return Results.Ok(result);
            });

            app.MapGet("/providers/{id}", async (string id, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
            {
                var provider = await registry.GetByIdAsync(id, ct);
                return provider is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(provider, secrets, ct));
            });

            app.MapPost("/providers", async (ProviderConfig provider, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
                string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.BaseUrl)
                    ? Results.BadRequest("Id and BaseUrl are required.")
                    : Results.Ok(await ToResponseAsync(await registry.UpsertAsync(provider, ct), secrets, ct)));

            app.MapPut("/providers", async (ProviderConfig provider, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
                string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.BaseUrl)
                    ? Results.BadRequest("Id and BaseUrl are required.")
                    : Results.Ok(await ToResponseAsync(await registry.UpsertAsync(provider, ct), secrets, ct)));

            app.MapDelete("/providers/{id}", async (string id, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
            {
                var provider = await registry.GetByIdAsync(id, ct);
                if (provider is null || !await registry.DeleteAsync(id, ct)) return Results.NotFound();

                await secrets.DeleteAsync(KeyReference(provider), ct);
                return Results.NoContent();
            });

            // Write-only: the API key is never returned by any endpoint.
            app.MapPut("/providers/{id}/key", async (string id, ApiKeyRequest body, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.ApiKey)) return Results.BadRequest("ApiKey is required.");

                var provider = await registry.GetByIdAsync(id, ct);
                if (provider is null) return Results.NotFound();

                await secrets.SetAsync(KeyReference(provider), body.ApiKey, ct);
                return Results.NoContent();
            });

            app.MapDelete("/providers/{id}/key", async (string id, IProvider_Registry registry, ISecret_Store secrets, CancellationToken ct) =>
            {
                var provider = await registry.GetByIdAsync(id, ct);
                if (provider is null) return Results.NotFound();

                return await secrets.DeleteAsync(KeyReference(provider), ct) ? Results.NoContent() : Results.NotFound();
            });
        }

        private static string KeyReference(ProviderConfig provider)
            => string.IsNullOrWhiteSpace(provider.ApiKeyRef) ? provider.Id : provider.ApiKeyRef;

        private static async Task<object> ToResponseAsync(ProviderConfig provider, ISecret_Store secrets, CancellationToken ct)
            => new
            {
                provider.Id,
                provider.Name,
                provider.BaseUrl,
                provider.Active,
                provider.ApiKeyRef,
                provider.AuthHeader,
                provider.AuthScheme,
                provider.ContextLength,
                provider.MaxOutputTokens,
                provider.Temperature,
                provider.VisionModel,
                provider.VisionPrompt,
                provider.Routes,
                HasApiKey = await secrets.ExistsAsync(KeyReference(provider), ct)
            };
    }
}
