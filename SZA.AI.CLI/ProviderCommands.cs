using System.Text.Json.Nodes;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class ProviderCommands
{
    public static async Task<int> AddAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        if (args.Get("url") is null)
            throw new CliException("A opção --url é obrigatória.");

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        if (await api.GetProviderAsync(id, ct) is not null)
            throw new CliException($"O provider '{id}' já existe. Use `sza provider edit {id}`.");

        var provider = new JsonObject
        {
            ["id"] = id,
            ["name"] = id,
            ["active"] = true,
        };
        ApplyOptions(provider, args);

        await api.CreateProviderAsync(provider, ct);

        var key = args.Get("api-key");
        if (!string.IsNullOrEmpty(key))
            await api.SetApiKeyAsync(id, key, ct);

        ConsoleUi.Success($"Provider '{id}' cadastrado.");
        if (string.IsNullOrEmpty(key))
            ConsoleUi.Info($"Defina a chave com `sza provider set-key {id}`.");

        return 0;
    }

    public static async Task<int> EditAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        var provider = await api.GetProviderAsync(id, ct)
            ?? throw new CliException($"Provider '{id}' não encontrado.");
        provider.Remove("hasApiKey");

        var changed = ApplyOptions(provider, args);
        var key = args.Get("api-key");

        if (!changed && key is null)
            throw new CliException("Nenhuma alteração informada. Veja `sza provider --help`.");

        if (changed)
            await api.UpdateProviderAsync(provider, ct);

        if (!string.IsNullOrEmpty(key))
            await api.SetApiKeyAsync(id, key, ct);

        ConsoleUi.Success($"Provider '{id}' atualizado.");
        return 0;
    }

    public static async Task<int> RemoveAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        if (!await api.DeleteProviderAsync(id, ct))
            throw new CliException($"Provider '{id}' não encontrado.");

        ConsoleUi.Success($"Provider '{id}' removido.");
        return 0;
    }

    public static async Task<int> SetKeyAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var key = args.Get("api-key") ?? ReadSecret();
        if (string.IsNullOrWhiteSpace(key))
            throw new CliException("A API key não pode ser vazia.");

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        if (!await api.SetApiKeyAsync(id, key.Trim(), ct))
            throw new CliException($"Provider '{id}' não encontrado.");

        ConsoleUi.Success($"API key do provider '{id}' definida.");
        return 0;
    }

    public static async Task<int> RemoveKeyAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        if (!await api.DeleteApiKeyAsync(id, ct))
            throw new CliException($"Provider '{id}' não encontrado ou sem API key.");

        ConsoleUi.Success($"API key do provider '{id}' removida.");
        return 0;
    }

    private static string? ReadSecret() =>
        Console.IsInputRedirected
            ? Console.In.ReadLine()
            : AnsiConsole.Prompt(new TextPrompt<string>("API key:").Secret());

    private static bool ApplyOptions(JsonObject provider, CliArguments args)
    {
        var changed = false;

        void Set(string property, JsonNode? value)
        {
            provider[property] = value;
            changed = true;
        }

        if (args.Has("active") && args.Has("inactive"))
            throw new CliException("Use apenas uma das opções --active ou --inactive.");

        var url = args.Get("url");
        if (url is not null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                throw new CliException($"URL inválida: {url} (use http:// ou https://).");
            }

            Set("baseUrl", url);
        }

        if (args.Get("name") is { } name) Set("name", name);
        if (args.Get("auth-header") is { } header) Set("authHeader", header);
        if (args.Get("auth-scheme") is { } scheme) Set("authScheme", scheme);
        if (args.Get("vision-model") is { } visionModel) Set("visionModel", visionModel);
        if (args.Get("vision-prompt") is { } visionPrompt) Set("visionPrompt", visionPrompt);

        if (args.GetInt("context-length", 1, int.MaxValue) is { } context)
            Set("contextLength", JsonValue.Create(context));
        if (args.GetInt("max-output", 1, int.MaxValue) is { } maxOutput)
            Set("maxOutputTokens", JsonValue.Create(maxOutput));
        if (args.GetDouble("temperature", 0, 2) is { } temperature)
            Set("temperature", JsonValue.Create(temperature));

        if (args.Has("active")) Set("active", JsonValue.Create(true));
        if (args.Has("inactive")) Set("active", JsonValue.Create(false));

        var routeArgs = args.GetAll("route");
        if (routeArgs.Count > 0)
        {
            var routes = provider["routes"] as JsonObject;
            var created = routes is null;
            routes ??= new JsonObject();

            foreach (var route in routeArgs)
            {
                var separator = route.IndexOf('=');
                if (separator <= 0 || separator == route.Length - 1)
                    throw new CliException($"Rota inválida: {route} (use rota=destino).");

                routes[route[..separator].Trim()] = route[(separator + 1)..].Trim();
            }

            if (created)
                provider["routes"] = routes;

            changed = true;
        }

        return changed;
    }
}
