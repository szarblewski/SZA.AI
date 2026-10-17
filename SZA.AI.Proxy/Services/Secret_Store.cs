using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace SZA.AI.Proxy.Services
{
    public class Secret_Store : ISecret_Store
    {
        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "Data", "secrets.json");
        private readonly IDataProtector _protector;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Dictionary<string, string>? _cache;

        public Secret_Store(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("SZA.AI.Proxy.ProviderKeys");
        }

        public async Task<string?> GetAsync(string reference, CancellationToken ct = default)
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariableName(reference));
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var secrets = await LoadAsync(ct).ConfigureAwait(false);
                if (!secrets.TryGetValue(reference, out var protectedValue)) return null;

                try { return _protector.Unprotect(protectedValue); }
                catch (System.Security.Cryptography.CryptographicException) { return null; }
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> ExistsAsync(string reference, CancellationToken ct = default)
            => !string.IsNullOrEmpty(await GetAsync(reference, ct).ConfigureAwait(false));

        public async Task SetAsync(string reference, string value, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var secrets = await LoadAsync(ct).ConfigureAwait(false);
                secrets[reference] = _protector.Protect(value);
                await SaveAsync(secrets, ct).ConfigureAwait(false);
            }
            finally { _lock.Release(); }
        }

        public async Task<bool> DeleteAsync(string reference, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var secrets = await LoadAsync(ct).ConfigureAwait(false);
                var removed = secrets.Remove(reference);
                if (removed) await SaveAsync(secrets, ct).ConfigureAwait(false);
                return removed;
            }
            finally { _lock.Release(); }
        }

        private static string EnvironmentVariableName(string reference)
            => "SZA_AI_KEY_" + Regex.Replace(reference, "[^a-zA-Z0-9]", "_").ToUpperInvariant();

        // Must be called with the lock held.
        private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
        {
            if (_cache is not null) return _cache;

            if (!File.Exists(_filePath))
            {
                _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return _cache;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: ct).ConfigureAwait(false);
            _cache = new Dictionary<string, string>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
            return _cache;
        }

        // Must be called with the lock held. Atomic write: temp file + replace.
        private async Task SaveAsync(Dictionary<string, string> secrets, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";

            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, secrets, new JsonSerializerOptions { WriteIndented = true }, ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            _cache = secrets;
        }
    }
}
