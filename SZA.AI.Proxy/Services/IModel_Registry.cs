using SZA.AI.Proxy.Models;

namespace SZA.AI.Proxy.Services
{
    public interface IModel_Registry
    {
        Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ModelConfig>> GetByProviderAsync(string providerId, CancellationToken ct = default);
        Task<ModelConfig?> FindByLocalNameAsync(string localName, CancellationToken ct = default);
        Task<ModelConfig?> FindAsync(string providerId, string localName, CancellationToken ct = default);
        Task<ModelConfig> UpsertAsync(ModelConfig model, CancellationToken ct = default);
        Task<bool> DeleteAsync(string providerId, string localName, CancellationToken ct = default);
    }
}
