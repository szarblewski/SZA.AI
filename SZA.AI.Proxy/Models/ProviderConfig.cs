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
        public int? ContextLength { get; set; }
        public int? MaxOutputTokens { get; set; }
        public double? Temperature { get; set; }
        public string? VisionModel { get; set; }
        public string? VisionPrompt { get; set; }
        public Dictionary<string, string> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
