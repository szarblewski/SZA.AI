using Spectre.Console;

namespace SZA.AI.CLI;

internal static class ConsoleUi
{
    public static void Banner()
    {
        AnsiConsole.Write(new FigletText("SZA.AI").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]CLI de ativação e gerenciamento do SZA.AI Proxy[/]");
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
    }

    public static Panel StatusPanel(
        string state,
        string stateColor,
        string? url,
        string? pid,
        string? uptime,
        string? version,
        string? providers,
        string? models,
        string? note = null)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        grid.AddRow("[grey]Estado[/]", $"[{stateColor}]{Markup.Escape(state)}[/]");

        if (!string.IsNullOrWhiteSpace(url))
            grid.AddRow("[grey]URL[/]", $"[blue underline]{Markup.Escape(url)}[/]");
        if (!string.IsNullOrWhiteSpace(pid))
            grid.AddRow("[grey]PID[/]", Markup.Escape(pid));
        if (!string.IsNullOrWhiteSpace(uptime))
            grid.AddRow("[grey]Uptime[/]", Markup.Escape(uptime));
        if (!string.IsNullOrWhiteSpace(version))
            grid.AddRow("[grey]Versão[/]", Markup.Escape(version));
        if (!string.IsNullOrWhiteSpace(providers))
            grid.AddRow("[grey]Providers[/]", Markup.Escape(providers));
        if (!string.IsNullOrWhiteSpace(models))
            grid.AddRow("[grey]Modelos[/]", Markup.Escape(models));
        if (!string.IsNullOrWhiteSpace(note))
            grid.AddRow("[grey]Obs.[/]", $"[grey]{Markup.Escape(note)}[/]");

        return new Panel(grid)
        {
            Header = new PanelHeader("SZA.AI Proxy"),
            Border = BoxBorder.Rounded,
            Expand = true
        };
    }

    public static Panel LogPanel(IReadOnlyCollection<string> lines)
    {
        var content = lines.Count == 0
            ? "Aguardando logs..."
            : string.Join(Environment.NewLine, lines);

        return new Panel(new Text(content))
        {
            Header = new PanelHeader("Logs em tempo real"),
            Border = BoxBorder.Rounded,
            Expand = true
        };
    }

    public static Table ProvidersTable(IReadOnlyList<ProviderInfo> providers)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn("ID");
        table.AddColumn("Nome");
        table.AddColumn("Base URL");
        table.AddColumn(new TableColumn("Ativo").Centered());
        table.AddColumn(new TableColumn("Chave").Centered());

        foreach (var provider in providers)
        {
            table.AddRow(
                Markup.Escape(provider.Id),
                Markup.Escape(string.IsNullOrWhiteSpace(provider.Name) ? provider.Id : provider.Name),
                Markup.Escape(provider.BaseUrl),
                provider.Active ? "[green]sim[/]" : "[red]não[/]",
                provider.HasApiKey ? "[green]sim[/]" : "[yellow]não[/]");
        }

        return table;
    }

    public static Table ModelsTable(IReadOnlyList<ModelInfo> models)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn("Provider");
        table.AddColumn("Modelo local");
        table.AddColumn("Modelo remoto");
        table.AddColumn(new TableColumn("Ativo").Centered());
        table.AddColumn(new TableColumn("Visão").Centered());

        foreach (var model in models)
        {
            table.AddRow(
                Markup.Escape(model.ProviderId),
                Markup.Escape(model.LocalName),
                Markup.Escape(string.IsNullOrWhiteSpace(model.RemoteName) ? model.LocalName : model.RemoteName),
                model.Active ? "[green]sim[/]" : "[red]não[/]",
                model.SupportsVision ? "[green]sim[/]" : "[grey]não[/]");
        }

        return table;
    }

    public static Table ConfigTable(CliConfig config, ProxyLocation? location)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn(new TableColumn("Configuração").NoWrap());
        table.AddColumn("Valor");

        table.AddRow("Arquivo", Markup.Escape(CliConfig.FilePath));
        table.AddRow("Porta padrão", Markup.Escape(config.Port?.ToString() ?? $"{Commands.DefaultPort} (padrão)"));
        table.AddRow("Caminho do proxy", Markup.Escape(config.ProxyPath ?? "(detecção automática)"));

        if (location is null)
        {
            table.AddRow("Detectado", "[red]não encontrado[/]");
        }
        else
        {
            table.AddRow("Projeto", Markup.Escape(location.ProjectPath));
            table.AddRow("Executável", File.Exists(location.ExePath)
                ? Markup.Escape(location.ExePath)
                : $"[yellow]{Markup.Escape(location.ExePath)} (não compilado)[/]");
        }

        return table;
    }

    public static void Error(string message) => AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");

    public static void Success(string message) => AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");

    public static void Warning(string message) => AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");

    public static void Info(string message) => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime:hh\\:mm\\:ss}";

        return uptime.ToString(@"hh\:mm\:ss");
    }
}
