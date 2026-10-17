namespace SZA.AI.Proxy.Services
{
    public interface ISecret_Store
    {
        Task<string?> GetAsync(string reference, CancellationToken ct = default);
        Task<bool> ExistsAsync(string reference, CancellationToken ct = default);
        Task SetAsync(string reference, string value, CancellationToken ct = default);
        Task<bool> DeleteAsync(string reference, CancellationToken ct = default);
    }
}
