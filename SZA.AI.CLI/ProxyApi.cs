using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SZA.AI.CLI;

internal sealed class ProviderInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool HasApiKey { get; set; }
    public int? ContextLength { get; set; }
    public int? MaxOutputTokens { get; set; }
    public double? Temperature { get; set; }
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

    public async Task<JsonObject?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/providers/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
    }

    public async Task CreateProviderAsync(JsonObject provider, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("api/providers", provider, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task UpdateProviderAsync(JsonObject provider, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync("api/providers", provider, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<bool> DeleteProviderAsync(string id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"api/providers/{Uri.EscapeDataString(id)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, ct);
        return true;
    }

    public async Task<bool> SetApiKeyAsync(string id, string apiKey, CancellationToken ct = default)
    {
        using var response = await _http.PutAsJsonAsync(
            $"api/providers/{Uri.EscapeDataString(id)}/key",
            new { apiKey },
            JsonOptions,
            ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, ct);
        return true;
    }

    public async Task<bool> DeleteApiKeyAsync(string id, CancellationToken ct = default)
    {
        using var response = await _http.DeleteAsync($"api/providers/{Uri.EscapeDataString(id)}/key", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, ct);
        return true;
    }

    public async Task<JsonArray?> GetProviderModelsAsync(string providerId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/providers/{Uri.EscapeDataString(providerId)}/models", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, ct);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonArray;
    }

    public async Task SaveModelAsync(string providerId, JsonObject model, bool update, CancellationToken ct = default)
    {
        var uri = $"api/providers/{Uri.EscapeDataString(providerId)}/models";
        using var response = update
            ? await _http.PutAsJsonAsync(uri, model, JsonOptions, ct)
            : await _http.PostAsJsonAsync(uri, model, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<bool> DeleteModelAsync(string providerId, string localName, CancellationToken ct = default)
    {
        var escapedName = string.Join('/', localName.Split('/').Select(Uri.EscapeDataString));
        using var response = await _http.DeleteAsync(
            $"api/providers/{Uri.EscapeDataString(providerId)}/models/{escapedName}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        await EnsureSuccessAsync(response, ct);
        return true;
    }

    public async Task<JsonObject?> ImportModelsAsync(string providerId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync(
            $"api/providers/{Uri.EscapeDataString(providerId)}/models/import", null, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, ct);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = (await response.Content.ReadAsStringAsync(ct)).Trim('"', ' ', '\r', '\n');
        throw new CliException(string.IsNullOrEmpty(body)
            ? $"O proxy respondeu {(int)response.StatusCode}."
            : $"O proxy respondeu {(int)response.StatusCode}: {body}");
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
