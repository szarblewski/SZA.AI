namespace SZA.AI.Proxy.Models
{
    public class ProviderConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public string? ApiKeyRef { get; set; }
        public string AuthHeader { get; set; } = "Authorization";
        public string AuthScheme { get; set; } = "Bearer";
        public Dictionary<string, string> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
