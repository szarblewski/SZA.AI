using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public class Image_Describer : IImage_Describer
    {
        private const string DefaultPrompt =
            "Descreva esta imagem em português com o máximo de detalhes úteis: transcreva todo o texto visível, " +
            "código e mensagens de erro, descreva diagramas, layout e elementos de interface. " +
            "Seja objetivo e fiel ao conteúdo, sem inventar informações.";

        private const int MaxImagesPerRequest = 5;
        private const int MaxParallel = 3;
        private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProvider_Registry _providers;
        private readonly ISecret_Store _secrets;
        private readonly IMemoryCache _cache;
        private readonly ILogger<Image_Describer> _logger;

        public Image_Describer(
            IHttpClientFactory httpClientFactory,
            IProvider_Registry providers,
            ISecret_Store secrets,
            IMemoryCache cache,
            ILogger<Image_Describer> logger)
        {
            _httpClientFactory = httpClientFactory;
            _providers = providers;
            _secrets = secrets;
            _cache = cache;
            _logger = logger;
        }

        private sealed record ImageSlot(JsonObject Message, string ImageUrl, JsonNode? Part);

        public async Task ReplaceImagesAsync(JsonNode body, ProviderConfig provider, ModelConfig visionModel, CancellationToken ct = default)
        {
            if (body["messages"] is not JsonArray messages) return;

            var slots = CollectImages(messages);
            if (slots.Count == 0) return;

            var prompt = string.IsNullOrWhiteSpace(provider.VisionPrompt) ? DefaultPrompt : provider.VisionPrompt;
            using var gate = new SemaphoreSlim(MaxParallel);

            var labels = await Task.WhenAll(slots.Select(async (slot, index) =>
            {
                var number = index + 1;
                if (number > MaxImagesPerRequest) return "[imagem ignorada: limite de imagens]";

                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var description = await DescribeAsync(provider, visionModel, slot.ImageUrl, prompt, ct).ConfigureAwait(false);
                    return description is null
                        ? "[imagem não pôde ser processada]"
                        : $"[Imagem {number}: {description}]";
                }
                finally { gate.Release(); }
            })).ConfigureAwait(false);

            var ollamaLabels = new Dictionary<JsonObject, List<string>>();

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (slot.Part is null)
                {
                    // Ollama style: collect the labels per message and handle after the loop.
                    if (!ollamaLabels.TryGetValue(slot.Message, out var list))
                        ollamaLabels[slot.Message] = list = new List<string>();
                    list.Add(labels[i]);
                }
                else
                {
                    // OpenAI style: replace the image part by a text part.
                    slot.Part.ReplaceWith(new JsonObject { ["type"] = "text", ["text"] = labels[i] });
                }
            }

            foreach (var (message, list) in ollamaLabels)
            {
                message.Remove("images");
                var text = string.Join('\n', list);

                switch (message["content"])
                {
                    case JsonArray parts:
                        parts.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                        break;
                    case JsonNode existing when !string.IsNullOrEmpty(existing.ToString()):
                        message["content"] = existing + "\n" + text;
                        break;
                    default:
                        message["content"] = text;
                        break;
                }
            }
        }

        private static List<ImageSlot> CollectImages(JsonArray messages)
        {
            var slots = new List<ImageSlot>();

            foreach (var node in messages)
            {
                if (node is not JsonObject message) continue;

                // Ollama: "images": [ "<base64>", ... ]
                if (message["images"] is JsonArray images)
                {
                    foreach (var image in images)
                    {
                        var value = image?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                            slots.Add(new ImageSlot(message, ToImageUrl(value), null));
                    }
                }

                // OpenAI: "content": [ { "type": "image_url", "image_url": { "url": "..." } }, ... ]
                if (message["content"] is JsonArray parts)
                {
                    foreach (var part in parts.ToList())
                    {
                        if (part is not JsonObject obj) continue;

                        var url = obj["type"]?.ToString() switch
                        {
                            "image_url" => obj["image_url"] is JsonObject inner ? inner["url"]?.ToString() : obj["image_url"]?.ToString(),
                            "image" when obj["source"] is JsonObject source && source["data"] is not null =>
                                $"data:{source["media_type"] ?? "image/png"};base64,{source["data"]}",
                            _ => null
                        };

                        if (!string.IsNullOrWhiteSpace(url))
                            slots.Add(new ImageSlot(message, url, part));
                    }
                }
            }

            return slots;
        }

        private async Task<string?> DescribeAsync(ProviderConfig provider, ModelConfig visionModel, string imageUrl, string prompt, CancellationToken ct)
        {
            var cacheKey = "img:" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{provider.Id}|{visionModel.RemoteName}|{prompt}|{imageUrl}")));

            if (_cache.TryGetValue(cacheKey, out string? cached)) return cached;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ImageTimeout);

            try
            {
                var url = await _providers.ResolveUrlAsync(provider.Id, "/api/chat", timeout.Token).ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Provider has no mapping for \"/api/chat\".");

                var request = new JsonObject
                {
                    ["model"] = visionModel.RemoteName,
                    ["stream"] = false,
                    ["messages"] = new JsonArray(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray(
                            new JsonObject { ["type"] = "text", ["text"] = prompt },
                            new JsonObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JsonObject { ["url"] = imageUrl }
                            })
                    })
                };

                using var upstreamReq = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json")
                };

                await AddAuthenticationAsync(upstreamReq, provider, timeout.Token).ConfigureAwait(false);

                var client = _httpClientFactory.CreateClient("provider");
                using var upstreamRes = await client.SendAsync(upstreamReq, timeout.Token).ConfigureAwait(false);
                upstreamRes.EnsureSuccessStatusCode();

                await using var stream = await upstreamRes.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var root = await JsonNode.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);

                var description = ((root?["choices"] as JsonArray)?.FirstOrDefault()?["message"]?["content"]
                                   ?? root?["message"]?["content"])?.ToString();

                if (string.IsNullOrWhiteSpace(description))
                    throw new InvalidOperationException("Vision model returned an empty description.");

                description = description.Trim();
                _cache.Set(cacheKey, description, CacheDuration);
                return description;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Image description failed for provider {ProviderId} using model {Model}", provider.Id, visionModel.LocalName);
                return null;
            }
        }

        private async Task AddAuthenticationAsync(HttpRequestMessage request, ProviderConfig provider, CancellationToken ct)
        {
            var reference = string.IsNullOrWhiteSpace(provider.ApiKeyRef) ? provider.Id : provider.ApiKeyRef;
            var key = await _secrets.GetAsync(reference, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(key)) return;

            var value = string.IsNullOrWhiteSpace(provider.AuthScheme) ? key : $"{provider.AuthScheme} {key}";
            request.Headers.TryAddWithoutValidation(provider.AuthHeader, value);
        }

        // Ollama sends raw base64; the vision model expects a data URL (or http URL).
        private static string ToImageUrl(string value)
        {
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value;

            var mime = value switch
            {
                _ when value.StartsWith("/9j/") => "image/jpeg",
                _ when value.StartsWith("R0lGOD") => "image/gif",
                _ when value.StartsWith("UklGR") => "image/webp",
                _ => "image/png"
            };

            return $"data:{mime};base64,{value}";
        }
    }
}
