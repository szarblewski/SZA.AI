namespace SZA.AI.Proxy.Helpers
{
    // Maps OpenAI-compatible paths to the canonical Ollama path used as key in provider routes.
    public static class RouteAliases
    {
        private static readonly Dictionary<string, string> ToCanonical = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/v1/chat/completions"] = "/api/chat",
            ["/v1/completions"] = "/api/generate",
            ["/v1/embeddings"] = "/api/embed",
            ["/v1/models"] = "/api/tags",
        };

        public static string Canonical(string path) => ToCanonical.TryGetValue(path, out var c) ? c : path;
    }
}
