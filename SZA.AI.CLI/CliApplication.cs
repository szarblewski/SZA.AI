using System.Reflection;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class CliApplication
{
    // grupo -> subcomando -> quantidade de argumentos posicionais
    private static readonly Dictionary<string, Dictionary<string, int>> Groups = new(StringComparer.Ordinal)
    {
        ["proxy"] = new() { ["start"] = 0, ["stop"] = 0, ["status"] = 0, ["logs"] = 0, ["open"] = 0, ["autostart"] = 1 },
        ["provider"] = new() { ["ls"] = 0, ["add"] = 1, ["edit"] = 1, ["rm"] = 1, ["set-key"] = 1, ["rm-key"] = 1 },
        ["model"] = new() { ["ls"] = 0, ["add"] = 2, ["edit"] = 2, ["rm"] = 2, ["import"] = 1 },
        ["template"] = new() { ["ls"] = 0, ["add"] = 1, ["edit"] = 1, ["rm"] = 1 },
        ["config"] = new() { ["show"] = 0, ["set"] = 0, ["reset"] = 0 },
    };

    private static readonly string[] TopLevel = ["install", "uninstall", "version", "help"];

    private static readonly HashSet<string> ValuedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "port", "proxy", "lines", "path", "url", "name", "api-key", "auth-header", "auth-scheme",
        "context-length", "max-output", "temperature", "vision-model", "vision-prompt", "route", "remove-route", "remote", "template",
    };

    private static readonly HashSet<string> FlagOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "help", "version", "follow", "detach", "no-build", "reset-data", "verbose",
        "active", "inactive", "vision", "no-vision",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = Parse(args, out var parseError);
        if (parseError is not null)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(parseError)}[/]");
            AnsiConsole.MarkupLine("[grey]Use `sza --help` para ver os comandos disponíveis.[/]");
            return 2;
        }

        var options = parsed.Arguments;
        var group = parsed.Group;
        var sub = parsed.Subcommand;

        if (options.Has("version") || group == "version")
        {
            var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational?.Split('+')[0] ?? "1.0.0";
            AnsiConsole.MarkupLine($"sza {version}");
            return 0;
        }

        if (group.Length == 0 || group == "help")
        {
            PrintHelp(group == "help" ? sub : string.Empty);
            return 0;
        }

        if (options.Has("help") || (Groups.ContainsKey(group) && sub.Length == 0))
        {
            PrintHelp(group);
            return 0;
        }

        var config = CliConfig.Load();

        try
        {
            switch (group, sub)
            {
                case ("proxy", "start"):
                    return await Commands.StartAsync(
                        config,
                        options.GetInt("port", 0, 65535) ?? Commands.ResolvePort(config),
                        options.Has("detach"),
                        options.Has("no-build"),
                        options.Has("reset-data"),
                        CancellationToken.None);

                case ("proxy", "stop"):
                    return Commands.Stop(config);

                case ("proxy", "status"):
                    return await Commands.StatusAsync(config, CancellationToken.None);

                case ("proxy", "logs"):
                    return await Commands.LogsAsync(
                        config,
                        options.GetInt("lines", 1, 10_000) ?? 40,
                        options.Has("follow"),
                        CancellationToken.None);

                case ("proxy", "autostart"):
                    return AutostartCommands.Run(options.Args.FirstOrDefault());

                case ("proxy", "open"):
                    return await Commands.OpenAsync(config, CancellationToken.None);

                case ("provider", "ls"):
                    return await Commands.ProvidersAsync(config, CancellationToken.None);

                case ("provider", "add"):
                    return await ProviderCommands.AddAsync(config, options, CancellationToken.None);

                case ("provider", "edit"):
                    return await ProviderCommands.EditAsync(config, options, CancellationToken.None);

                case ("provider", "rm"):
                    return await ProviderCommands.RemoveAsync(config, options, CancellationToken.None);

                case ("provider", "set-key"):
                    return await ProviderCommands.SetKeyAsync(config, options, CancellationToken.None);

                case ("provider", "rm-key"):
                    return await ProviderCommands.RemoveKeyAsync(config, options, CancellationToken.None);

                case ("template", "ls"):
                    return await TemplateCommands.ListAsync(config, CancellationToken.None);

                case ("template", "add"):
                    return await TemplateCommands.AddAsync(config, options, CancellationToken.None);

                case ("template", "edit"):
                    return await TemplateCommands.EditAsync(config, options, CancellationToken.None);

                case ("template", "rm"):
                    return await TemplateCommands.RemoveAsync(config, options, CancellationToken.None);

                case ("model", "ls"):
                    return await Commands.ModelsAsync(config, CancellationToken.None);

                case ("model", "add"):
                    return await ModelCommands.AddAsync(config, options, CancellationToken.None);

                case ("model", "edit"):
                    return await ModelCommands.EditAsync(config, options, CancellationToken.None);

                case ("model", "rm"):
                    return await ModelCommands.RemoveAsync(config, options, CancellationToken.None);

                case ("model", "import"):
                    return await ModelCommands.ImportAsync(config, options, CancellationToken.None);

                case ("config", "show"):
                    return Commands.Config(config, null, null, false, true);

                case ("config", "set"):
                    return Commands.Config(config, options.GetInt("port", 0, 65535), options.Get("proxy"), false, false);

                case ("config", "reset"):
                    return Commands.Config(config, null, null, true, false);

                case ("install", _):
                    return InstallCommands.Install(options.Get("path"));

                case ("uninstall", _):
                    return InstallCommands.Uninstall(options.Get("path"));

                default:
                    AnsiConsole.MarkupLine($"[red]Comando desconhecido: {Markup.Escape(group)} {Markup.Escape(sub)}[/]");
                    return 2;
            }
        }
        catch (CliException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]Falha ao falar com o proxy: {Markup.Escape(ex.Message)}[/]");
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
            if (options.Has("verbose"))
                AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private sealed record ParsedCommand(string Group, string Subcommand, CliArguments Arguments);

    private static ParsedCommand Parse(string[] args, out string? error)
    {
        error = null;
        var options = new CliArguments();
        var positionals = new List<string>();
        var empty = new ParsedCommand(string.Empty, string.Empty, options);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h": options.AddFlag("help"); continue;
                case "-v": options.AddFlag("version"); continue;
                case "-f": options.AddFlag("follow"); continue;
                case "-d": options.AddFlag("detach"); continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (arg.StartsWith('-') && arg.Length > 1)
                {
                    error = $"Opção desconhecida: {arg}";
                    return empty;
                }

                positionals.Add(arg);
                continue;
            }

            var body = arg[2..];
            string? inlineValue = null;
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                inlineValue = body[(eq + 1)..];
                body = body[..eq];
            }

            if (ValuedOptions.Contains(body))
            {
                if (inlineValue is null)
                {
                    if (i + 1 >= args.Length)
                    {
                        error = $"A opção --{body} exige um valor.";
                        return empty;
                    }

                    inlineValue = args[++i];
                }

                options.AddValue(body, inlineValue);
            }
            else if (FlagOptions.Contains(body) && inlineValue is null)
            {
                options.AddFlag(body);
            }
            else
            {
                error = $"Opção desconhecida: {arg}";
                return empty;
            }
        }

        if (positionals.Count == 0)
            return empty;

        var group = positionals[0].ToLowerInvariant();
        var sub = string.Empty;

        if (Groups.TryGetValue(group, out var subs))
        {
            if (positionals.Count > 1)
            {
                sub = positionals[1].ToLowerInvariant();
                if (!subs.TryGetValue(sub, out var expected))
                {
                    error = $"Comando desconhecido: {group} {sub}";
                    return empty;
                }

                var extra = positionals.Count - 2;
                if (extra != expected && !options.Has("help"))
                {
                    error = $"O comando '{group} {sub}' espera {expected} argumento(s), mas recebeu {extra}.";
                    return empty;
                }
            }
        }
        else if (TopLevel.Contains(group))
        {
            var max = group == "help" ? 1 : 0;
            if (positionals.Count - 1 > max)
            {
                error = $"Argumento inesperado: {positionals[max + 1]}";
                return empty;
            }

            sub = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : string.Empty;
        }
        else
        {
            error = $"Comando desconhecido: {group}";
            return empty;
        }

        options.Args = positionals.Skip(2).ToList();
        return new ParsedCommand(group, sub, options);
    }

    private static void PrintHelp(string group)
    {
        if (Groups.ContainsKey(group))
        {
            PrintGroupHelp(group);
            return;
        }

        AnsiConsole.MarkupLine("[bold]Uso:[/]  sza [[OPÇÕES]] COMANDO");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("CLI de ativação e gerenciamento do SZA.AI Proxy");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Comandos de gerenciamento:[/]");
        AnsiConsole.MarkupLine("  [green]proxy[/]       Inicia, para e monitora o proxy");
        AnsiConsole.MarkupLine("  [green]provider[/]    Cadastra, edita e remove providers e suas chaves");
        AnsiConsole.MarkupLine("  [green]template[/]    Mantém os templates de provider (rotas e padrões)");
        AnsiConsole.MarkupLine("  [green]model[/]       Cadastra, edita, remove e importa modelos");
        AnsiConsole.MarkupLine("  [green]config[/]      Mostra e altera a configuração do CLI");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Comandos:[/]");
        AnsiConsole.MarkupLine("  [green]install[/]     Instala o sza no PATH do usuário");
        AnsiConsole.MarkupLine("  [green]uninstall[/]   Remove o sza instalado");
        AnsiConsole.MarkupLine("  [green]version[/]     Mostra a versão");
        AnsiConsole.MarkupLine("  [green]help[/]        Mostra a ajuda");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Opções:[/]");
        AnsiConsole.MarkupLine("  [grey]-h, --help[/]      Mostra a ajuda");
        AnsiConsole.MarkupLine("  [grey]-v, --version[/]   Mostra a versão");
        AnsiConsole.MarkupLine("  [grey]--verbose[/]       Mostra detalhes de erros");
        AnsiConsole.MarkupLine("  [grey]--path <dir>[/]    Diretório de instalação [[install, uninstall]]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("Execute 'sza COMANDO --help' para mais informações sobre um comando.");
    }

    private static void PrintGroupHelp(string group)
    {
        AnsiConsole.MarkupLine($"[bold]Uso:[/]  sza {group} COMANDO [[OPÇÕES]]");
        AnsiConsole.WriteLine();

        switch (group)
        {
            case "proxy":
                AnsiConsole.MarkupLine("[bold]Comandos:[/]");
                AnsiConsole.MarkupLine("  [green]start[/]    Inicia o proxy (Ctrl+C encerra)");
                AnsiConsole.MarkupLine("  [green]stop[/]     Encerra o proxy iniciado pelo CLI");
                AnsiConsole.MarkupLine("  [green]status[/]   Mostra estado, URL, PID, versão e contagens");
                AnsiConsole.MarkupLine("  [green]logs[/]     Mostra os logs do proxy");
                AnsiConsole.MarkupLine("  [green]open[/]     Abre a documentação Scalar no navegador");
                AnsiConsole.MarkupLine("  [green]autostart on|off|status[/]  Inicia o proxy junto com o Windows");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Opções:[/]");
                AnsiConsole.MarkupLine("  [grey]--port <n>[/]     Porta do proxy (0 = porta livre) [[start]]");
                AnsiConsole.MarkupLine("  [grey]-d, --detach[/]   Inicia em segundo plano [[start]]");
                AnsiConsole.MarkupLine("  [grey]--no-build[/]     Não compila o proxy antes de iniciar [[start]]");
                AnsiConsole.MarkupLine("  [grey]--reset-data[/]   Apaga providers, modelos e chaves salvos e recomeça em branco [[start]]");
                AnsiConsole.MarkupLine("  [grey]--lines <n>[/]    Linhas exibidas (padrão: 40) [[logs]]");
                AnsiConsole.MarkupLine("  [grey]-f, --follow[/]   Acompanha novos logs [[logs]]");
                break;

            case "provider":
                AnsiConsole.MarkupLine("[bold]Comandos:[/]");
                AnsiConsole.MarkupLine("  [green]ls[/]                Lista os providers");
                AnsiConsole.MarkupLine("  [green]add <id>[/]          Cadastra um provider (exige --url)");
                AnsiConsole.MarkupLine("  [green]edit <id>[/]         Altera um provider existente");
                AnsiConsole.MarkupLine("  [green]rm <id>[/]           Remove o provider e sua chave");
                AnsiConsole.MarkupLine("  [green]set-key <id>[/]      Define a API key (pede a chave se --api-key for omitido)");
                AnsiConsole.MarkupLine("  [green]rm-key <id>[/]       Remove a API key");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Opções de add/edit:[/]");
                AnsiConsole.MarkupLine("  [grey]--url <url>[/]            Base URL do provider (ou use --template)");
                AnsiConsole.MarkupLine("  [grey]--template <id>[/]        Template com rotas e padrões (padrão: template com o mesmo id)");
                AnsiConsole.MarkupLine("  [grey]--name <nome>[/]          Nome de exibição");
                AnsiConsole.MarkupLine("  [grey]--auth-header <h>[/]      Cabeçalho de autenticação (padrão: Authorization)");
                AnsiConsole.MarkupLine("  [grey]--auth-scheme <s>[/]      Esquema de autenticação (padrão: Bearer)");
                AnsiConsole.MarkupLine("  [grey]--context-length <n>[/]   Tamanho de contexto (padrão: 1000000)");
                AnsiConsole.MarkupLine("  [grey]--max-output <n>[/]       Máximo de tokens de saída (padrão: 384000)");
                AnsiConsole.MarkupLine("  [grey]--temperature <t>[/]      Temperatura de 0 a 2 (padrão: 0.2)");
                AnsiConsole.MarkupLine("  [grey]--vision-model <m>[/]     Modelo de visão (opcional, vazio por padrão)");
                AnsiConsole.MarkupLine("  [grey]--vision-prompt <p>[/]    Prompt de visão (opcional, vazio por padrão)");
                AnsiConsole.MarkupLine("  [grey]--route <rota=destino>[/] Mapeia uma rota (repetível)");
                AnsiConsole.MarkupLine("  [grey]--remove-route <rota>[/]  Remove uma rota (repetível)");
                AnsiConsole.MarkupLine("  [grey]--active | --inactive[/]  Ativa ou desativa (padrão: --active)");
                break;

            case "template":
                AnsiConsole.MarkupLine("[bold]Comandos:[/]");
                AnsiConsole.MarkupLine("  [green]ls[/]               Lista os templates");
                AnsiConsole.MarkupLine("  [green]add <id>[/]         Cadastra um template (exige --url)");
                AnsiConsole.MarkupLine("  [green]edit <id>[/]        Altera um template (--route sobrescreve/adiciona rotas)");
                AnsiConsole.MarkupLine("  [green]rm <id>[/]          Remove o template");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Opções de add/edit:[/]");
                AnsiConsole.MarkupLine("  [grey]--url <url>[/]            Base URL");
                AnsiConsole.MarkupLine("  [grey]--name <nome>[/]          Nome de exibição");
                AnsiConsole.MarkupLine("  [grey]--auth-header <h>[/]      Cabeçalho de autenticação");
                AnsiConsole.MarkupLine("  [grey]--auth-scheme <s>[/]      Esquema de autenticação");
                AnsiConsole.MarkupLine("  [grey]--context-length <n>[/]   Tamanho de contexto");
                AnsiConsole.MarkupLine("  [grey]--max-output <n>[/]       Máximo de tokens de saída");
                AnsiConsole.MarkupLine("  [grey]--temperature <t>[/]      Temperatura (0 a 2)");
                AnsiConsole.MarkupLine("  [grey]--route <rota=destino>[/] Mapeia uma rota (repetível)");
                AnsiConsole.MarkupLine("  [grey]--remove-route <rota>[/]  Remove uma rota (repetível)");
                break;

            case "model":
                AnsiConsole.MarkupLine("[bold]Comandos:[/]");
                AnsiConsole.MarkupLine("  [green]ls[/]                           Lista os modelos");
                AnsiConsole.MarkupLine("  [green]add <provider> <local>[/]       Cadastra um modelo");
                AnsiConsole.MarkupLine("  [green]edit <provider> <local>[/]      Altera um modelo");
                AnsiConsole.MarkupLine("  [green]rm <provider> <local>[/]        Remove um modelo");
                AnsiConsole.MarkupLine("  [green]import <provider>[/]            Importa os modelos expostos pelo provider");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Opções de add/edit:[/]");
                AnsiConsole.MarkupLine("  [grey]--remote <nome>[/]        Nome do modelo no provider (padrão: nome local)");
                AnsiConsole.MarkupLine("  [grey]--context-length <n>[/]   Tamanho de contexto");
                AnsiConsole.MarkupLine("  [grey]--max-output <n>[/]       Máximo de tokens de saída");
                AnsiConsole.MarkupLine("  [grey]--temperature <t>[/]      Temperatura (0 a 2)");
                AnsiConsole.MarkupLine("  [grey]--vision | --no-vision[/] Suporte a visão");
                AnsiConsole.MarkupLine("  [grey]--active | --inactive[/]  Ativa ou desativa");
                break;

            case "config":
                AnsiConsole.MarkupLine("[bold]Comandos:[/]");
                AnsiConsole.MarkupLine("  [green]show[/]    Mostra a configuração atual");
                AnsiConsole.MarkupLine("  [green]set[/]     Altera a configuração");
                AnsiConsole.MarkupLine("  [green]reset[/]   Restaura os padrões");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Opções de set:[/]");
                AnsiConsole.MarkupLine("  [grey]--port <n>[/]       Porta padrão do proxy");
                AnsiConsole.MarkupLine("  [grey]--proxy <path>[/]   Caminho do .csproj, .exe ou diretório do SZA.AI.Proxy");
                break;
        }
    }
}
