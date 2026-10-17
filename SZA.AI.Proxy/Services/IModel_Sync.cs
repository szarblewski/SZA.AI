using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public record ModelImportResult(int Found, IReadOnlyList<ModelConfig> Imported);

    public interface IModel_Sync
    {
        // Imports the models exposed by the provider. Null when the provider has no "/api/tags" route mapped.
        Task<ModelImportResult?> ImportAsync(ProviderConfig provider, CancellationToken ct = default);

        // Imports from every active provider that has a "/api/tags" route (throttled; failures are ignored).
        Task SyncAllAsync(CancellationToken ct = default);
    }
}
