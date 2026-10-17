

namespace SZA.AI.Proxy.Routes
{
    public class MapRoutes
    {
        internal static void RoutesBuilder(WebApplication app)
        {
            MapRoutes_Ollama.OllamaRoutesBuilder(app);
            MapRoutes_Providers.ProvidersRoutesBuilder(app);
        }
    }
}
