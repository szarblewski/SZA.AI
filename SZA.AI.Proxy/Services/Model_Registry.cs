using System.Text.Json;
using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public class Model_Registry : IModel_Registry
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "Data", "models.json");
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<ModelConfig>? _cache;

        public async Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return (await LoadAsync(ct).ConfigureAwait(false)).ToList();
            }
            finally { _lock.Release(); }
        }

        public async Task<IReadOnlyList<ModelConfig>> GetByProviderAsync(string providerId, CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.Where(m => Same(m.ProviderId, providerId)).ToList();
        }

        public async Task<ModelConfig?> FindByLocalNameAsync(string localName, CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(m => m.Active && Same(m.LocalName, localName));
        }

        public async Task<ModelConfig?> FindAsync(string providerId, string localName, CancellationToken ct = default)
        {
            var all = await GetAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(m => Same(m.ProviderId, providerId) && Same(m.LocalName, localName));
        }

        public async Task<ModelConfig> UpsertAsync(ModelConfig model, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var index = list.FindIndex(m => Same(m.ProviderId, model.ProviderId) && Same(m.LocalName, model.LocalName));
                if (index >= 0) list[index] = model; else list.Add(model);
                await SaveAsync(list, ct).ConfigureAwait(false);
                return model;
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteAsync(string providerId, string localName, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var list = await LoadAsync(ct).ConfigureAwait(false);
                var removed = list.RemoveAll(m => Same(m.ProviderId, providerId) && Same(m.LocalName, localName)) > 0;
                if (removed) await SaveAsync(list, ct).ConfigureAwait(false);
                return removed;
            }
            finally { _lock.Release(); }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Must be called with the lock held.
        private async Task<List<ModelConfig>> LoadAsync(CancellationToken ct)
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = new List<ModelConfig>();
                return _cache;
            }

            await using var stream = File.OpenRead(_filePath);
            _cache = await JsonSerializer.DeserializeAsync<List<ModelConfig>>(stream, JsonOptions, ct).ConfigureAwait(false)
                     ?? new List<ModelConfig>();
            return _cache;
        }

        // Must be called with the lock held. Atomic write: temp file + replace.
        private async Task SaveAsync(List<ModelConfig> list, CancellationToken ct)
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
