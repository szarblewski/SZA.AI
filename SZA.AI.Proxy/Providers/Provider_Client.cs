using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using SZA.AI.Proxy.Converters;
using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.Providers
{
    public class Provider_Client : IProviderClient
    {
        private const string ProviderHeader = "X-Provider";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProvider_Registry _providers;
        private readonly IModel_Registry _models;
        private readonly ISecret_Store _secrets;
        private readonly ILogger<Provider_Client> _logger;

        public Provider_Client(
            IHttpClientFactory httpClientFactory,
            IProvider_Registry providers,
            IModel_Registry models,
            ISecret_Store secrets,
            ILogger<Provider_Client> logger)
        {
            _httpClientFactory = httpClientFactory;
            _providers = providers;
            _models = models;
            _secrets = secrets;
            _logger = logger;
        }

        public async Task ForwardAsync(HttpRequest req, HttpResponse res, CancellationToken ct = default)
        {
            var body = await ReadBodyAsync(req, ct).ConfigureAwait(false);
            var modelName = body?["model"]?.GetValue<string>();

            var provider = await SelectProviderAsync(req, modelName, ct).ConfigureAwait(false);
            if (provider is null)
            {
                await WriteErrorAsync(res, StatusCodes.Status404NotFound, "Provider not found.", ct).ConfigureAwait(false);
                return;
            }

            var url = await _providers.ResolveUrlAsync(provider.Id, req.Path.Value ?? string.Empty, ct).ConfigureAwait(false);
            if (url is null)
            {
                await WriteErrorAsync(res, StatusCodes.Status404NotFound, "Route mapping not found for provider.", ct).ConfigureAwait(false);
                return;
            }

            if (body is not null && !string.IsNullOrWhiteSpace(modelName))
            {
                var model = await _models.FindAsync(provider.Id, modelName, ct).ConfigureAwait(false);
                if (model is not null)
                {
                    if (!model.Active)
                    {
                        await WriteErrorAsync(res, StatusCodes.Status404NotFound, "Model is not active.", ct).ConfigureAwait(false);
                        return;
                    }

                    body["model"] = model.RemoteName;
                }
            }

            using var upstreamReq = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body?.ToJsonString() ?? string.Empty, Encoding.UTF8, "application/json")
            };

            await AddAuthenticationAsync(upstreamReq, provider, ct).ConfigureAwait(false);

            var client = _httpClientFactory.CreateClient("provider");

            HttpResponseMessage upstreamRes;
            try
            {
                upstreamRes = await client.SendAsync(upstreamReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Provider {ProviderId} request failed", provider.Id);
                await WriteErrorAsync(res, StatusCodes.Status502BadGateway, "Provider request failed.", ct).ConfigureAwait(false);
                return;
            }

            using (upstreamRes)
            {
                if (!upstreamRes.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Provider {ProviderId} returned {StatusCode}", provider.Id, (int)upstreamRes.StatusCode);
                    await WriteErrorAsync(res, StatusCodes.Status502BadGateway,
                        $"Provider returned status {(int)upstreamRes.StatusCode}.", ct).ConfigureAwait(false);
                    return;
                }

                await using var upstreamBody = await upstreamRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var isSse = upstreamRes.Content.Headers.ContentType?.MediaType == "text/event-stream";

                res.StatusCode = StatusCodes.Status200OK;
                res.Headers["Cache-Control"] = "no-cache";
                res.Headers["X-Accel-Buffering"] = "no";

                if (!isSse)
                {
                    res.ContentType = "application/json";
                    await upstreamBody.CopyToAsync(res.Body, ct).ConfigureAwait(false);
                    return;
                }

                // Provider SSE -> client NDJSON, one chunk at a time.
                res.ContentType = "application/x-ndjson";

                await foreach (var json in StreamFormatConverter.SseToNdjsonLinesAsync(upstreamBody, ct).WithCancellation(ct))
                {
                    await res.Body.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), ct).ConfigureAwait(false);
                    await res.Body.FlushAsync(ct).ConfigureAwait(false);
                }
            }
        }

        // 1) X-Provider header, 2) provider that owns the requested model, 3) first active provider.
        private async Task<ProviderConfig?> SelectProviderAsync(HttpRequest req, string? modelName, CancellationToken ct)
        {
            if (req.Headers.TryGetValue(ProviderHeader, out var header) && !string.IsNullOrWhiteSpace(header))
                return await _providers.GetByIdAsync(header.ToString(), ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(modelName))
            {
                var model = await _models.FindByLocalNameAsync(modelName, ct).ConfigureAwait(false);
                if (model is not null)
                    return await _providers.GetByIdAsync(model.ProviderId, ct).ConfigureAwait(false);
            }

            return await _providers.GetActiveDefaultAsync(ct).ConfigureAwait(false);
        }

        private async Task AddAuthenticationAsync(HttpRequestMessage upstreamReq, ProviderConfig provider, CancellationToken ct)
        {
            var reference = string.IsNullOrWhiteSpace(provider.ApiKeyRef) ? provider.Id : provider.ApiKeyRef;
            var key = await _secrets.GetAsync(reference, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(key)) return;

            var value = string.IsNullOrWhiteSpace(provider.AuthScheme) ? key : $"{provider.AuthScheme} {key}";
            upstreamReq.Headers.TryAddWithoutValidation(provider.AuthHeader, value);
        }

        private static async Task<JsonNode?> ReadBodyAsync(HttpRequest req, CancellationToken ct)
        {
            req.EnableBuffering();
            req.Body.Position = 0;

            try
            {
                return await JsonNode.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
            finally
            {
                req.Body.Position = 0;
            }
        }

        private static async Task WriteErrorAsync(HttpResponse res, int statusCode, string message, CancellationToken ct)
        {
            res.StatusCode = statusCode;
            res.ContentType = "application/json";
            await res.WriteAsJsonAsync(new { error = message }, ct).ConfigureAwait(false);
        }
    }
}
