namespace SZA.AI.Proxy.Helpers
{
    public static class EndPoint
    {
        public static IEndpointRouteBuilder MapEndpoint<T>(this IEndpointRouteBuilder app) where T : IEndPoint
        {
            T.Map(app);
            return app;
        }
    }
}
