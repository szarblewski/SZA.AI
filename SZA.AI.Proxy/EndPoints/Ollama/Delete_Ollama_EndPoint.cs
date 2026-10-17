using SZA.AI.Proxy.Helpers;

namespace SZA.AI.Proxy.EndPoints.Ollama
{
    public class Delete_Ollama_EndPoint : IEndPoint
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapDelete("/delete", () => Results.NoContent());
        }
    
    }
}
