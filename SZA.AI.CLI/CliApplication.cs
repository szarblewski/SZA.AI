using System.Globalization;
using System.Reflection;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = Parse(args, out var parseError);
        if (parseError is not null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(parseError)}[/]");
            AnsiConsole.WriteLine();
            PrintHelp();
            return 2;
        }

        if (options.ShowHelp || options.Command == "help")
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            AnsiConsole.MarkupLine($"sza {version}");
            return 0;
        }

        var config = CliConfig.Load();

        try
        {
            switch (options.Command)
            {
                case "":
                    return await Commands.InteractiveAsync(config);

                case "start":
                    return await Commands.StartAsync(
                        config,
                        options.Port ?? Commands.ResolvePort(config),
                        options.Detach,
                        options.NoBuild,
                        CancellationToken.None);

                case "stop":
                    return Commands.Stop(config);

                case "status":
                    return await Commands.StatusAsync(config, CancellationToken.None);

                case "providers":
                    return await Commands.ProvidersAsync(config, CancellationToken.None);

                case "models":
                    return await Commands.ModelsAsync(config, CancellationToken.None);

                case "logs":
                    return await Commands.LogsAsync(config, options.Lines, options.Follow, CancellationToken.None);

                case "open":
                    return await Commands.OpenAsync(config, CancellationToken.None);

                case "config":
                    return Commands.Config(config, options.Port, options.ProxyPath, options.Reset, options.Show);

                default:
                    AnsiConsole.MarkupLine($"[red]Comando desconhecido: {Markup.Escape(options.Command)}[/]");
                    PrintHelp();
                    return 2;
            }
        }
        catch (CliException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operação cancelada.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Erro inesperado: {Markup.Escape(ex.Message)}[/]");
            if (options.Verbose)
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static CliOptions Parse(string[] args, out string? error)
    {
        error = null;
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help")
            {
                options.ShowHelp = true;
            }
            else if (arg is "-v" or "--version")
            {
                options.ShowVersion = true;
            }
            else if (arg is "-f" or "--follow")
            {
                options.Follow = true;
            }
            else if (arg == "--detach")
            {
                options.Detach = true;
            }
            else if (arg == "--no-build")
            {
                options.NoBuild = true;
            }
            else if (arg == "--reset")
            {
                options.Reset = true;
            }
            else if (arg == "--show")
            {
                options.Show = true;
            }
            else if (arg == "--verbose")
            {
                options.Verbose = true;
            }
            else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParsePort(arg["--port=".Length..], out var port, out error))
                    return options;
                options.Port = port;
            }
            else if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "A opção --port exige um valor.";
                    return options;
                }

                if (!TryParsePort(args[++i], out var port, out error))
                    return options;
                options.Port = port;
            }
            else if (arg.StartsWith("--proxy=", StringComparison.OrdinalIgnoreCase))
            {
                options.ProxyPath = arg["--proxy=".Length..];
            }
            else if (arg.Equals("--proxy", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "A opção --proxy exige um caminho.";
                    return options;
                }

                options.ProxyPath = args[++i];
            }
            else if (arg.StartsWith("--lines=", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseLines(arg["--lines=".Length..], out var lines, out error))
                    return options;
                options.Lines = lines;
            }
            else if (arg.Equals("--lines", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "A opção --lines exige um valor.";
                    return options;
                }

                if (!TryParseLines(args[++i], out var lines, out error))
                    return options;
                options.Lines = lines;
            }
            else if (arg.StartsWith('-'))
            {
                error = $"Opção desconhecida: {arg}";
                return options;
            }
            else if (options.Command.Length == 0)
            {
                options.Command = arg.ToLowerInvariant();
            }
            else
            {
                error = $"Argumento inesperado: {arg}";
                return options;
            }
        }

        if (options.Command is not ("" or "start" or "stop" or "status" or "providers" or "models" or "logs" or "open" or "config" or "help"))
        {
            error = $"Comando desconhecido: {options.Command}";
        }

        return options;
    }

    private static bool TryParsePort(string value, out int port, out string? error)
    {
        error = null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
        {
            error = $"Porta inválida: {value}";
            return false;
        }

        if (port is < 0 or > 65535)
        {
            error = "A porta deve estar entre 0 e 65535 (0 escolhe uma porta livre).";
            return false;
        }

        return true;
    }

    private static bool TryParseLines(string value, out int lines, out string? error)
    {
        error = null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out lines) || lines <= 0)
        {
            error = $"Quantidade de linhas inválida: {value}";
            return false;
        }

        lines = Math.Min(lines, 10_000);
        return true;
    }

    private static void PrintHelp()
    {
        AnsiConsole.Write(new FigletText("SZA.AI").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]CLI de ativação e gerenciamento do SZA.AI Proxy[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand();

        table.AddColumn(new TableColumn("Comando").NoWrap());
        table.AddColumn("Descrição");

        table.AddRow("[green]sza[/]", "Abre o menu interativo");
        table.AddRow("[green]sza start[/]", "Inicia o proxy com dashboard ao vivo (Ctrl+C encerra)");
        table.AddRow("[green]sza start --detach[/]", "Inicia o proxy em segundo plano");
        table.AddRow("[green]sza stop[/]", "Encerra o proxy iniciado pelo CLI");
        table.AddRow("[green]sza status[/]", "Mostra estado, URL, PID, versão e contagens");
        table.AddRow("[green]sza providers[/]", "Lista os providers configurados no proxy");
        table.AddRow("[green]sza models[/]", "Lista os modelos expostos pelo proxy");
        table.AddRow("[green]sza logs --follow[/]", "Acompanha os logs do proxy");
        table.AddRow("[green]sza open[/]", "Abre a documentação Scalar no navegador");
        table.AddRow("[green]sza config[/]", "Mostra/altera porta e caminho do proxy");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Opções[/]");
        AnsiConsole.MarkupLine("  [grey]--port <n>[/]     Porta do proxy (0 = porta livre)");
        AnsiConsole.MarkupLine("  [grey]--proxy <path>[/] Caminho do .csproj, .exe ou diretório do SZA.AI.Proxy");
        AnsiConsole.MarkupLine("  [grey]--detach[/]       Inicia em segundo plano");
        AnsiConsole.MarkupLine("  [grey]--no-build[/]     Não compila o proxy antes de iniciar");
        AnsiConsole.MarkupLine("  [grey]--lines <n>[/]    Linhas exibidas por [grey]sza logs[/] (padrão: 40)");
        AnsiConsole.MarkupLine("  [grey]--follow[/]       Acompanha novos logs (Ctrl+C encerra)");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Exemplos[/]");
        AnsiConsole.MarkupLine("  [grey]dotnet run --project SZA.AI.CLI -- start --detach --port 11434[/]");
        AnsiConsole.MarkupLine("  [grey]sza config --port 11434[/]");
        AnsiConsole.MarkupLine("  [grey]sza start --proxy C:\\repos\\SZA.AI\\SZA.AI.Proxy\\SZA.AI.Proxy.csproj[/]");
    }

    private sealed class CliOptions
    {
        public string Command { get; set; } = string.Empty;
        public int? Port { get; set; }
        public string? ProxyPath { get; set; }
        public int Lines { get; set; } = 40;
        public bool Detach { get; set; }
        public bool NoBuild { get; set; }
        public bool Follow { get; set; }
        public bool Reset { get; set; }
        public bool Show { get; set; }
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public bool Verbose { get; set; }
    }
}
