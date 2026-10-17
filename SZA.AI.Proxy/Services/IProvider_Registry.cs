using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public interface IProvider_Registry
    {
        Task<IReadOnlyList<ProviderConfig>> GetAllAsync(CancellationToken ct = default);
        Task<ProviderConfig?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<ProviderConfig?> GetActiveDefaultAsync(CancellationToken ct = default);
        Task<ProviderConfig> UpsertAsync(ProviderConfig provider, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
        Task<Uri?> ResolveUrlAsync(string? providerId, string localPath, CancellationToken ct = default);
    }
}
