using System.Net.Http.Json;
using System.Text.Json;

namespace SZA.AI.CLI;

internal sealed class ProviderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool HasApiKey { get; set; }
}

internal sealed class ModelInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string LocalName { get; set; } = string.Empty;
    public string RemoteName { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool SupportsVision { get; set; }
}

internal sealed class ProxyApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ProxyApi(string baseUrl, TimeSpan? timeout = null)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/version", ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString()
            : null;
    }

    public async Task<List<ProviderInfo>> GetProvidersAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/providers", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<ProviderInfo>>(JsonOptions, ct)
            ?? [];
    }

    public async Task<List<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        var providers = await GetProvidersAsync(ct);
        var models = new List<ModelInfo>();

        foreach (var provider in providers)
        {
            try
            {
                var id = Uri.EscapeDataString(provider.Id);
                using var response = await _http.GetAsync($"api/providers/{id}/models", ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                var providerModels = await response.Content.ReadFromJsonAsync<List<ModelInfo>>(JsonOptions, ct);
                if (providerModels is not null)
                    models.AddRange(providerModels);
            }
            catch (HttpRequestException)
            {
                // Um provider com problema não impede a listagem dos demais.
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout individual do provider.
            }
        }

        return models;
    }

    public static async Task<bool> PingAsync(string baseUrl, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = timeout };
            using var response = await http.GetAsync(baseUrl.TrimEnd('/') + "/api/version", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
