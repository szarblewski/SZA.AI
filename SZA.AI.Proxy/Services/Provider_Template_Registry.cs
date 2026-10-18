using System.Text.Json;
using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public class Provider_Template_Registry : IProvider_Template_Registry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "Data", "provider-templates.json");
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<ProviderTemplate>? _cache;

        public async Task<IReadOnlyList<ProviderTemplate>> GetAllAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return (await LoadAsync(ct).ConfigureAwait(false)).ToList();
            }
            finally { _lock.Release(); }
        }

        public async Task<ProviderTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(t => Same(t.Id, id));
        }

        public async Task<ProviderTemplate> UpsertAsync(ProviderTemplate template, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var index = list.FindIndex(t => Same(t.Id, template.Id));
                if (index >= 0) list[index] = template; else list.Add(template);
                await SaveAsync(list, ct).ConfigureAwait(false);
                return template;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var removed = list.RemoveAll(t => Same(t.Id, id)) > 0;
                if (removed) await SaveAsync(list, ct).ConfigureAwait(false);
                return removed;
            }
            finally { _lock.Release(); }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Must be called with the lock held.
        private async Task<List<ProviderTemplate>> LoadAsync(CancellationToken ct)
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = new List<ProviderTemplate>();
                return _cache;
            }

            try
            {
                await using var stream = File.OpenRead(_filePath);
                _cache = await JsonSerializer.DeserializeAsync<List<ProviderTemplate>>(stream, JsonOptions, ct).ConfigureAwait(false) ?? new List<ProviderTemplate>();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Corrupted/locked file: keep a backup and continue with an empty list instead of failing every request.
                try { File.Copy(_filePath, _filePath + ".corrupt", overwrite: true); } catch { }
                _cache = new List<ProviderTemplate>();
            }
            return _cache;
        }

        // Must be called with the lock held. Atomic write: temp file + replace.
        private async Task SaveAsync(List<ProviderTemplate> list, CancellationToken ct)
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
