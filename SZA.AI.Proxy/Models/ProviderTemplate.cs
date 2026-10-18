namespace SZA.AI.Proxy.Models
{
    public class ProviderTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string AuthHeader { get; set; } = "Authorization";
        public string AuthScheme { get; set; } = "Bearer";
        public int? ContextLength { get; set; }
        public int? MaxOutputTokens { get; set; }
        public double? Temperature { get; set; }
        public Dictionary<string, string> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
