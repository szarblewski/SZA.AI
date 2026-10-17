namespace SZA.AI.Proxy.Services
{
    public interface IOllama_Service
    {
        Task StreamNdjsonToSseHandler(HttpRequest req, HttpResponse res, CancellationToken ct);
        Task<IResult> SimpleEchoJsonHandler(HttpRequest req);
        Task<IResult> ShowModelAsync(HttpRequest req, CancellationToken ct);
    }
}
