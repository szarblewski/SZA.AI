using SZA.AI.Proxy.Models;
using SZA.AI.Proxy.Providers;

namespace SZA.AI.Proxy.Services
{
    public class Model_Sync : IModel_Sync
    {
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);
        private static readonly SemaphoreSlim SyncLock = new(1, 1);
        private static DateTimeOffset _lastSync = DateTimeOffset.MinValue;

        private readonly IProvider_Registry _providers;
        private readonly IModel_Registry _models;
        private readonly IProviderClient _client;
        private readonly ILogger<Model_Sync> _logger;

        public Model_Sync(IProvider_Registry providers, IModel_Registry models, IProviderClient client, ILogger<Model_Sync> logger)
        {
            _providers = providers;
            _models = models;
            _client = client;
            _logger = logger;
        }

        public async Task<ModelImportResult?> ImportAsync(ProviderConfig provider, CancellationToken ct = default)
        {
            var remoteNames = await _client.ListModelsAsync(provider, ct).ConfigureAwait(false);
            if (remoteNames is null) return null;

            var existing = await _models.GetAllAsync(ct).ConfigureAwait(false);
            var imported = new List<ModelConfig>();

            foreach (var remoteName in remoteNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (existing.Any(m => Same(m.ProviderId, provider.Id)
                                      && (Same(m.RemoteName, remoteName) || Same(m.LocalName, remoteName))))
                    continue;

                // Keep local names unique across providers.
                var nameTaken = existing.Any(m => Same(m.LocalName, remoteName));
                var localName = nameTaken ? $"{provider.Id}:{remoteName}" : remoteName;

                imported.Add(await _models.UpsertAsync(new ModelConfig
                {
                    ProviderId = provider.Id,
                    LocalName = localName,
                    RemoteName = remoteName,
                    Active = true
                }, ct).ConfigureAwait(false));
            }

            return new ModelImportResult(remoteNames.Count, imported);
        }

        public async Task SyncAllAsync(CancellationToken ct = default)
        {
            await SyncLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (DateTimeOffset.UtcNow - _lastSync < SyncInterval) return;

                var providers = await _providers.GetAllAsync(ct).ConfigureAwait(false);
                foreach (var provider in providers.Where(p => p.Active && p.Routes.ContainsKey("/api/tags")))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ProviderTimeout);

                    try
                    {
                        await ImportAsync(provider, timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex, "Model sync failed for provider {ProviderId}", provider.Id);
                    }
                }

                _lastSync = DateTimeOffset.UtcNow;
            }
            finally { SyncLock.Release(); }
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
