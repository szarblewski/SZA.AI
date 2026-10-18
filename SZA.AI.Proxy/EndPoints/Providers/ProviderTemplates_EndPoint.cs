using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.EndPoints.Providers
{
    public class ProviderTemplates_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet("/provider-templates", async (IProvider_Template_Registry registry, CancellationToken ct) =>
                Results.Ok(await registry.GetAllAsync(ct)));

            app.MapGet("/provider-templates/{id}", async (string id, IProvider_Template_Registry registry, CancellationToken ct) =>
            {
                var template = await registry.GetByIdAsync(id, ct);
                return template is null ? Results.NotFound() : Results.Ok(template);
            });

            app.MapPost("/provider-templates", (ProviderTemplate template, IProvider_Template_Registry registry, CancellationToken ct) =>
                Upsert(template, registry, ct));

            app.MapPut("/provider-templates", (ProviderTemplate template, IProvider_Template_Registry registry, CancellationToken ct) =>
                Upsert(template, registry, ct));

            app.MapDelete("/provider-templates/{id}", async (string id, IProvider_Template_Registry registry, CancellationToken ct) =>
                await registry.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
        }

        private static async Task<IResult> Upsert(ProviderTemplate template, IProvider_Template_Registry registry, CancellationToken ct)
            => string.IsNullOrWhiteSpace(template.Id)
                ? Results.BadRequest("Id is required.")
                : Results.Ok(await registry.UpsertAsync(template, ct));
    }
}
