using System.Text;
using System.Text.Json.Nodes;
using SZA.AI.Proxy.Providers;

namespace SZA.AI.Proxy.Services
{
    public class Ollama_Service : IOllama_Service
    {
        private readonly IProviderClient _providerClient;
        private readonly IModel_Registry _models;
        private readonly IProvider_Registry _providers;

        public Ollama_Service(IProviderClient providerClient, IModel_Registry models, IProvider_Registry providers)
        {
            _providerClient = providerClient;
            _models = models;
            _providers = providers;
        }

        // Forwards to the correct provider; provider SSE is streamed back to the client as NDJSON.
        public Task StreamNdjsonToSseHandler(HttpRequest req, HttpResponse res, CancellationToken ct)
            => _providerClient.ForwardAsync(req, res, ct);

        // Ollama-compatible /api/show: clients read the context size from model_info "<arch>.context_length".
        public async Task<IResult> ShowModelAsync(HttpRequest req, CancellationToken ct)
        {
            JsonNode? body;
            try { body = await JsonNode.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false); }
            catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Invalid JSON body." }); }

            var name = (body?["model"] ?? body?["name"])?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { error = "model is required." });

            var model = await _models.FindByLocalNameAsync(name, ct).ConfigureAwait(false);
            if (model is null)
                return Results.NotFound(new { error = $"model '{name}' not found" });

            var provider = await _providers.GetByIdAsync(model.ProviderId, ct).ConfigureAwait(false);
            var contextLength = model.ContextLength ?? provider?.ContextLength;
            var maxOutput = model.MaxOutputTokens ?? provider?.MaxOutputTokens;
            var temperature = model.Temperature ?? provider?.Temperature;

            var modelInfo = new Dictionary<string, object?> { ["general.architecture"] = "sza" };
            if (contextLength is not null) modelInfo["sza.context_length"] = contextLength;
            if (maxOutput is not null) modelInfo["sza.max_output_tokens"] = maxOutput;

            var parameters = new List<string>();
            if (contextLength is not null) parameters.Add($"num_ctx {contextLength}");
            if (maxOutput is not null) parameters.Add($"num_predict {maxOutput}");
            if (temperature is not null) parameters.Add(FormattableString.Invariant($"temperature {temperature}"));

            return Results.Json(new
            {
                modelfile = string.Empty,
                parameters = string.Join('\n', parameters),
                template = string.Empty,
                details = new { family = "sza", format = "api", parameter_size = string.Empty, quantization_level = string.Empty },
                model_info = modelInfo,
                capabilities = new[] { "completion", "tools", "vision" }
            });
        }

        public async Task<IResult> SimpleEchoJsonHandler(HttpRequest req)
        {
            // Read body and echo back as JSON. If empty, return 204.
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms).ConfigureAwait(false);
            if (ms.Length == 0) return Results.NoContent();
            ms.Seek(0, SeekOrigin.Begin);
            using var sr = new StreamReader(ms, Encoding.UTF8);
            var body = await sr.ReadToEndAsync().ConfigureAwait(false);
            return Results.Content(body, "application/json");
        }
    }
}
