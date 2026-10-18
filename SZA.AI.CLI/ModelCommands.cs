using System.Text.Json.Nodes;

namespace SZA.AI.CLI;

internal static class ModelCommands
{
    public static async Task<int> AddAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var providerId = args.Args[0];
        var localName = args.Args[1];

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        var models = await api.GetProviderModelsAsync(providerId, ct)
            ?? throw new CliException($"Provider '{providerId}' não encontrado.");

        if (Find(models, localName) is not null)
            throw new CliException($"O modelo '{localName}' já existe em '{providerId}'. Use `sza model edit`.");

        var model = new JsonObject
        {
            ["localName"] = localName,
            ["remoteName"] = localName,
            ["active"] = true,
            ["supportsVision"] = false,
        };
        ApplyOptions(model, args);

        await api.SaveModelAsync(providerId, model, update: false, ct);
        ConsoleUi.Success($"Modelo '{localName}' cadastrado em '{providerId}'.");
        return 0;
    }

    public static async Task<int> EditAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var providerId = args.Args[0];
        var localName = args.Args[1];

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        var models = await api.GetProviderModelsAsync(providerId, ct)
            ?? throw new CliException($"Provider '{providerId}' não encontrado.");

        var model = Find(models, localName)
            ?? throw new CliException($"Modelo '{localName}' não encontrado em '{providerId}'.");

        if (!ApplyOptions(model, args))
            throw new CliException("Nenhuma alteração informada. Veja `sza model --help`.");

        await api.SaveModelAsync(providerId, model, update: true, ct);
        ConsoleUi.Success($"Modelo '{localName}' atualizado.");
        return 0;
    }

    public static async Task<int> RemoveAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var providerId = args.Args[0];
        var localName = args.Args[1];

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl);

        if (!await api.DeleteModelAsync(providerId, localName, ct))
            throw new CliException($"Modelo '{localName}' não encontrado em '{providerId}'.");

        ConsoleUi.Success($"Modelo '{localName}' removido de '{providerId}'.");
        return 0;
    }

    public static async Task<int> ImportAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var providerId = args.Args[0];

        var proxyUrl = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(proxyUrl, TimeSpan.FromSeconds(60));

        var result = await api.ImportModelsAsync(providerId, ct)
            ?? throw new CliException($"Provider '{providerId}' não encontrado.");

        var found = (int?)result["found"] ?? 0;
        var imported = (int?)result["imported"] ?? 0;
        ConsoleUi.Success($"{imported} de {found} modelo(s) importado(s) de '{providerId}'.");
        return 0;
    }

    private static JsonObject? Find(JsonArray models, string localName) =>
        models
            .OfType<JsonObject>()
            .FirstOrDefault(m => string.Equals((string?)m["localName"], localName, StringComparison.OrdinalIgnoreCase));

    private static bool ApplyOptions(JsonObject model, CliArguments args)
    {
        var changed = false;

        void Set(string property, JsonNode? value)
        {
            model[property] = value;
            changed = true;
        }

        if (args.Has("active") && args.Has("inactive"))
            throw new CliException("Use apenas uma das opções --active ou --inactive.");
        if (args.Has("vision") && args.Has("no-vision"))
            throw new CliException("Use apenas uma das opções --vision ou --no-vision.");

        if (args.Get("remote") is { } remote) Set("remoteName", remote);

        if (args.GetInt("context-length", 1, int.MaxValue) is { } context)
            Set("contextLength", JsonValue.Create(context));
        if (args.GetInt("max-output", 1, int.MaxValue) is { } maxOutput)
            Set("maxOutputTokens", JsonValue.Create(maxOutput));
        if (args.GetDouble("temperature", 0, 2) is { } temperature)
            Set("temperature", JsonValue.Create(temperature));

        if (args.Has("active")) Set("active", JsonValue.Create(true));
        if (args.Has("inactive")) Set("active", JsonValue.Create(false));
        if (args.Has("vision")) Set("supportsVision", JsonValue.Create(true));
        if (args.Has("no-vision")) Set("supportsVision", JsonValue.Create(false));

        return changed;
    }
}
