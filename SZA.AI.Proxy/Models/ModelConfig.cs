namespace SZA.AI.Proxy.Models
{
    public class ModelConfig
    {
        public string ProviderId { get; set; } = string.Empty;
        public string LocalName { get; set; } = string.Empty;
        public string RemoteName { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
    }
}
