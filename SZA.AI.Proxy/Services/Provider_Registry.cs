using System.Text.Json;
using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public class Provider_Registry : IProvider_Registry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "Data", "providers.json");
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<ProviderConfig>? _cache;

        public async Task<IReadOnlyList<ProviderConfig>> GetAllAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return (await LoadAsync(ct).ConfigureAwait(false)).ToList();
            }
            finally { _lock.Release(); }
        }

        public async Task<ProviderConfig?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<ProviderConfig?> GetActiveDefaultAsync(CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(p => p.Active);
        }

        public async Task<ProviderConfig> UpsertAsync(ProviderConfig provider, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var index = list.FindIndex(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) list[index] = provider; else list.Add(provider);
                await SaveAsync(list, ct).ConfigureAwait(false);
                return provider;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var removed = list.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
                if (removed) await SaveAsync(list, ct).ConfigureAwait(false);
                return removed;
            }
            finally { _lock.Release(); }
        }

        public async Task<Uri?> ResolveUrlAsync(string? providerId, string localPath, CancellationToken ct = default)
        {
            var provider = string.IsNullOrWhiteSpace(providerId)
                ? await GetActiveDefaultAsync(ct).ConfigureAwait(false)
                : await GetByIdAsync(providerId, ct).ConfigureAwait(false);

            if (provider is null || !provider.Active) return null;
            if (!provider.Routes.TryGetValue(localPath, out var remotePath)) return null;

            return new Uri(new Uri(provider.BaseUrl.TrimEnd('/') + "/"), remotePath.TrimStart('/'));
        }

        // Must be called with the lock held.
        private async Task<List<ProviderConfig>> LoadAsync(CancellationToken ct)
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = new List<ProviderConfig>();
                return _cache;
            }

            try

            {

                await using var stream = File.OpenRead(_filePath);

                _cache = await JsonSerializer.DeserializeAsync<List<ProviderConfig>>(stream, JsonOptions, ct).ConfigureAwait(false) ?? new List<ProviderConfig>();

            }

            catch (Exception ex) when (ex is JsonException or IOException)

            {

                // Corrupted/locked file: keep a backup and continue with an empty list instead of failing every request.

                try { File.Copy(_filePath, _filePath + ".corrupt", overwrite: true); } catch { }

                _cache = new List<ProviderConfig>();

            }
            return _cache;
        }

        // Must be called with the lock held. Atomic write: temp file + replace.
        private async Task SaveAsync(List<ProviderConfig> list, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, list, JsonOptions, ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            _cache = list;
        }
    }
}
