using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public interface IProvider_Template_Registry
    {
        Task<IReadOnlyList<ProviderTemplate>> GetAllAsync(CancellationToken ct = default);
        Task<ProviderTemplate?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<ProviderTemplate> UpsertAsync(ProviderTemplate template, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    }
}
