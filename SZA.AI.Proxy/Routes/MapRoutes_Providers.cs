using SZA.AI.Proxy.EndPoints.Providers;
using SZA.AI.Proxy.Helpers;

namespace SZA.AI.Proxy.Routes
{
    public class MapRoutes_Providers
    {
        internal static void ProvidersRoutesBuilder(WebApplication app)
        {
            app.MapGroup("/api")
                .WithTags("Providers")
                .MapEndpoint<Providers_EndPoint>()
                .MapEndpoint<Models_EndPoint>()
                .MapEndpoint<ProviderTemplates_EndPoint>();

            app.MapGroup("/v1")
                .WithTags("Providers OpenAI")
                .MapEndpoint<Providers_EndPoint>()
                .MapEndpoint<Models_EndPoint>()
                .MapEndpoint<ProviderTemplates_EndPoint>();
        }
    }
}
