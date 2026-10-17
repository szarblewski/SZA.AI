namespace SZA.AI.Proxy.Configurations
{
    public class ProxyOptions
    {
        public const int DefaultPort = 11434;
        public const string PortEnvironmentVariable = "SZA_AI_PORT";

        public int Port { get; set; } = DefaultPort;

        // Priority: --port argument, SZA_AI_PORT, Proxy:Port, default (11434). Port 0 = random free port.
        public static ProxyOptions Resolve(string[] args, IConfiguration configuration)
        {
            var options = new ProxyOptions();
            configuration.GetSection("Proxy").Bind(options);

            if (TryParsePort(Environment.GetEnvironmentVariable(PortEnvironmentVariable), out var envPort))
                options.Port = envPort;

            if (TryParsePort(GetPortArgument(args), out var argPort))
                options.Port = argPort;

            if (options.Port < 0 || options.Port > 65535)
                options.Port = DefaultPort;

            return options;
        }

        private static string? GetPortArgument(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                    return args[i]["--port=".Length..];

                if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return args[i + 1];
            }

            return null;
        }

        private static bool TryParsePort(string? value, out int port)
            => int.TryParse(value, out port) && port >= 0 && port <= 65535;
    }
}
