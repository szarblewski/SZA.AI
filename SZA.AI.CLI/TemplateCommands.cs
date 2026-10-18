using System.Text.Json.Nodes;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class TemplateCommands
{
    public static async Task<int> ListAsync(CliConfig config, CancellationToken ct)
    {
        var url = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(url);

        var templates = await api.GetTemplatesAsync(ct);
        if (templates.Count == 0)
        {
            ConsoleUi.Warning("Nenhum template cadastrado.");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("ID");
        table.AddColumn("Nome");
        table.AddColumn("Base URL");
        table.AddColumn("Rotas");
        foreach (var template in templates)
        {
            var routes = template["routes"] as JsonObject;
            var routeText = routes is null
                ? string.Empty
                : string.Join(Environment.NewLine, routes.Select(r => $"{r.Key} -> {r.Value}"));
            table.AddRow(
                Markup.Escape((string?)template["id"] ?? string.Empty),
                Markup.Escape((string?)template["name"] ?? string.Empty),
                Markup.Escape((string?)template["baseUrl"] ?? string.Empty),
                Markup.Escape(routeText));
        }

        AnsiConsole.Write(table);
        ConsoleUi.Info($"{templates.Count} template(s) em {url}");
        return 0;
    }

    public static async Task<int> AddAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        if (args.Get("url") is null)
            throw new CliException("A opção --url é obrigatória.");

        var url = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(url);

        if (await api.GetTemplateAsync(id, ct) is not null)
            throw new CliException($"O template '{id}' já existe. Use `sza template edit {id}`.");

        var template = new JsonObject { ["id"] = id, ["name"] = id, ["routes"] = new JsonObject() };
        ProviderCommands.ApplyOptions(template, args);

        await api.SaveTemplateAsync(template, false, ct);
        ConsoleUi.Success($"Template '{id}' cadastrado.");
        return 0;
    }

    public static async Task<int> EditAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var url = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(url);

        var template = await api.GetTemplateAsync(id, ct)
            ?? throw new CliException($"Template '{id}' não encontrado.");

        if (!ProviderCommands.ApplyOptions(template, args))
            throw new CliException("Nenhuma alteração informada. Veja `sza template --help`.");

        await api.SaveTemplateAsync(template, true, ct);
        ConsoleUi.Success($"Template '{id}' atualizado.");
        return 0;
    }

    public static async Task<int> RemoveAsync(CliConfig config, CliArguments args, CancellationToken ct)
    {
        var id = args.Args[0];
        var url = await Commands.RequireRunningUrlAsync(config, ct);
        using var api = new ProxyApi(url);

        if (!await api.DeleteTemplateAsync(id, ct))
            throw new CliException($"Template '{id}' não encontrado.");

        ConsoleUi.Success($"Template '{id}' removido.");
        return 0;
    }
}
