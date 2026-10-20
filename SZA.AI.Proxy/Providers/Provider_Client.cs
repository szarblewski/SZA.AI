using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using SZA.AI.Proxy.Configurations;
using SZA.AI.Proxy.Converters;
using SZA.AI.Proxy.Helpers;
using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Services;

namespace SZA.AI.Proxy.Providers
{
    public class Provider_Client : IProviderClient
    {
        private const string ProviderHeader = "X-Provider";

        // SSE comment line: ignored by every SSE client, but enough traffic to reset their idle timeouts.
        private static readonly byte[] SseKeepAlive = ": keep-alive\n\n"u8.ToArray();

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProvider_Registry _providers;
        private readonly IModel_Registry _models;
        private readonly ISecret_Store _secrets;
        private readonly IImage_Describer _imageDescriber;
        private readonly ProxyOptions _options;
        private readonly ILogger<Provider_Client> _logger;

        public Provider_Client(
            IHttpClientFactory httpClientFactory,
            IProvider_Registry providers,
            IModel_Registry models,
            ISecret_Store secrets,
            IImage_Describer imageDescriber,
            ProxyOptions options,
            ILogger<Provider_Client> logger)
        {
            _httpClientFactory = httpClientFactory;
            _providers = providers;
            _models = models;
            _secrets = secrets;
            _imageDescriber = imageDescriber;
            _options = options;
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

            var url = await _providers.ResolveUrlAsync(provider.Id, RouteAliases.Canonical(req.Path.Value ?? string.Empty), ct).ConfigureAwait(false);
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

                if (model is null || !model.SupportsVision)
                {
                    if (!string.IsNullOrWhiteSpace(provider.VisionModel))
                    {
                        var visionModel = await _models.FindAsync(provider.Id, provider.VisionModel, ct).ConfigureAwait(false);
                        if (visionModel is not null)
                            await _imageDescriber.ReplaceImagesAsync(body, provider, visionModel, ct).ConfigureAwait(false);
                    }
                }

                // Configured temperature is only a default: a value sent by the client wins.
                var temperature = model?.Temperature ?? provider.Temperature;
                if (temperature is not null && body["temperature"] is null)
                    body["temperature"] = temperature;
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
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                _logger.LogError(ex, "Provider {ProviderId} request failed", provider.Id);
                await WriteErrorAsync(res, StatusCodes.Status502BadGateway, "Provider request failed.", ct).ConfigureAwait(false);
                return;
            }

            using (upstreamRes)
            try
            {
                if (!upstreamRes.IsSuccessStatusCode)
                {
                    var errorBody = await upstreamRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("Provider {ProviderId} returned {StatusCode}: {Body}", provider.Id, (int)upstreamRes.StatusCode, errorBody);
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

                if (req.Path.StartsWithSegments("/v1"))
                {
                    res.ContentType = "text/event-stream";
                    await CopySseAsync(upstreamBody, res, provider.Id, ct).ConfigureAwait(false);
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
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                // Upstream dropped mid-stream: headers are already sent, so just log and end the response.
                _logger.LogWarning(ex, "Provider {ProviderId} stream interrupted", provider.Id);
            }
        }

        /// <summary>
        /// Copies the provider SSE stream to the client. There is no overall timeout for generations,
        /// so while the provider stays silent a keep-alive comment is sent to keep client-side idle
        /// timeouts (Copilot CLI, undici, proxies) from dropping the connection.
        /// </summary>
        private async Task CopySseAsync(Stream upstream, HttpResponse res, string providerId, CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(_options.StreamHeartbeatSeconds);
            var keepAlive = interval > TimeSpan.Zero;

            var buffer = new byte[8192];
            Task<int>? pendingRead = null;
            var heartbeats = 0;
            var silence = TimeSpan.Zero;
            var silentSince = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                while (true)
                {
                    pendingRead ??= upstream.ReadAsync(buffer, ct).AsTask();

                    if (keepAlive && await Task.WhenAny(pendingRead, Task.Delay(interval, ct)).ConfigureAwait(false) != pendingRead)
                    {
                        // The delay only loses the race on cancellation; let it propagate instead of faking a heartbeat.
                        ct.ThrowIfCancellationRequested();

                        if (heartbeats == 0)
                            _logger.LogInformation("Provider {ProviderId} stream silent for {Seconds:0}s, sending keep-alives", providerId, interval.TotalSeconds);

                        await res.Body.WriteAsync(SseKeepAlive, ct).ConfigureAwait(false);
                        await res.Body.FlushAsync(ct).ConfigureAwait(false);
                        heartbeats++;
                        continue;
                    }

                    var read = await pendingRead.ConfigureAwait(false);
                    pendingRead = null;

                    if (read <= 0) break;

                    if (heartbeats > 0)
                        silence += System.Diagnostics.Stopwatch.GetElapsedTime(silentSince);

                    silentSince = System.Diagnostics.Stopwatch.GetTimestamp();

                    await res.Body.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    await res.Body.FlushAsync(ct).ConfigureAwait(false);
                }

                if (heartbeats > 0)
                    _logger.LogInformation("Provider {ProviderId} stream kept alive {Heartbeats} time(s) over {Silence:0.0}s of silence", providerId, heartbeats, silence.TotalSeconds);
            }
            finally
            {
                // The read is abandoned when the loop exits early; observe it so it does not surface as unobserved.
                if (pendingRead is { IsCompleted: false })
                    _ = pendingRead.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        public async Task<IReadOnlyList<string>?> ListModelsAsync(ProviderConfig provider, CancellationToken ct = default)
        {
            var url = await _providers.ResolveUrlAsync(provider.Id, "/api/tags", ct).ConfigureAwait(false);
            if (url is null) return null;

            using var upstreamReq = new HttpRequestMessage(HttpMethod.Get, url);
            await AddAuthenticationAsync(upstreamReq, provider, ct).ConfigureAwait(false);

            var client = _httpClientFactory.CreateClient("provider");
            using var upstreamRes = await client.SendAsync(upstreamReq, ct).ConfigureAwait(false);
            upstreamRes.EnsureSuccessStatusCode();

            await using var stream = await upstreamRes.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var root = await JsonNode.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            return ParseModelNames(root);
        }

        // Supports OpenAI style ({ "data": [{ "id" }] }), Ollama style ({ "models": [{ "name" }] }) and plain arrays.
        private static List<string> ParseModelNames(JsonNode? root)
        {
            var items = root switch
            {
                JsonArray array => array,
                JsonObject obj when obj["data"] is JsonArray data => data,
                JsonObject obj when obj["models"] is JsonArray models => models,
                _ => null
            };

            var names = new List<string>();
            if (items is null) return names;

            foreach (var item in items)
            {
                var name = item is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text
                    : (item?["id"] ?? item?["name"] ?? item?["model"])?.GetValue<string>();

                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }

            return names;
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
            if (res.HasStarted) return;
            res.StatusCode = statusCode;
            res.ContentType = "application/json";
            await res.WriteAsJsonAsync(new { error = message }, ct).ConfigureAwait(false);
        }
    }
}
