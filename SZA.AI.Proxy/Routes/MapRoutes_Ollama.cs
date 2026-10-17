using SZA.AI.Proxy.EndPoints.Ollama;
using SZA.AI.Proxy.EndPoints.OpenAI;
using SZA.AI.Proxy.Helpers;

namespace SZA.AI.Proxy.Routes
{
    public class MapRoutes_Ollama
    {
        internal static void OllamaRoutesBuilder(WebApplication app)
        {
            app.MapGroup("/v1")
                .WithTags("OpenAI")
                .MapEndpoint<OpenAI_EndPoint>();

            app.MapGroup("/api")
                .WithTags("Ollama Api")
                .MapEndpoint<Delete_Ollama_EndPoint>()
                .MapEndpoint<Get_Ollama_EndPoint>()
                .MapEndpoint<Post_Ollama_EndPoint>();
        }
    }
}
