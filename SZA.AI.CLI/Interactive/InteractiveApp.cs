using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SZA.AI.CLI.Interactive;

internal static class InteractiveApp
{
    private sealed record Snapshot(string? Url, int ConfiguredPort, List<ProviderInfo> Providers, string? Error);

    public static Task<int> RunAsync(CliConfig config, CancellationToken cancellationToken = default)
    {
        using var app = Application.Create().Init();
        using var window = new Window { Title = "SZA.AI" };

        var dashboard = CreateTextView("Dashboard");
        var providersView = CreateTextView("Providers");
        var settings = CreateTextView("Configurações");
        View[] pages = [dashboard, providersView, settings];

        var tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(8)
        };
        tabs.Add(pages);
        tabs.Value = dashboard;

        var log = new TextView
        {
            X = 0,
            Y = Pos.AnchorEnd(8),
            Width = Dim.Fill(),
            Height = 6,
            ReadOnly = true,
            CanFocus = false,
            Text = "Digite /help para ver os comandos."
        };

        var input = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill()
        };

        var shortcuts = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Text = "F1 Dashboard · F2 Providers · F3 Configurações · F4/Esc linha de comando · F5 Atualizar · Tab alterna abas · /help"
        };

        var logBuilder = new StringBuilder("Digite /help para ver os comandos.");

        void Write(string message)
        {
            foreach (var line in message.Split(["\r\n", "\n"], StringSplitOptions.None))
                logBuilder.AppendLine().Append(line);

            log.Text = logBuilder.ToString();
            log.MoveEnd();
        }

        void WriteSafe(string message) => app.Invoke(() => Write(message));

        void Show(View page)
        {
            tabs.Value = page;
            input.SetFocus();
        }

        async Task RefreshAsync()
        {
            var snapshot = await LoadAsync(config, cancellationToken);
            app.Invoke(() =>
            {
                dashboard.Text = BuildDashboardText(snapshot);
                providersView.Text = BuildProvidersText(snapshot);
                settings.Text = BuildSettingsText(snapshot);
            });
        }

        void Refresh()
        {
            dashboard.Text = "Carregando...";
            providersView.Text = "Carregando...";
            settings.Text = "Carregando...";
            _ = RefreshAsync();
        }

        void Run(Func<Task<string?>> action, bool refreshAfter = true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var message = await action();
                    if (!string.IsNullOrWhiteSpace(message))
                        WriteSafe(message);
                }
                catch (CliException ex)
                {
                    WriteSafe(ex.Message);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    WriteSafe($"Erro: {ex.Message}");
                }

                if (refreshAfter)
                    await RefreshAsync();
            }, cancellationToken);
        }

        var registry = new SlashCommandRegistry();
        registry.Register(new SlashCommand("help", "Lista os comandos", _ =>
        {
            Write("Comandos disponíveis:");
            foreach (var command in registry.All)
                Write($"  /{command.Name} - {command.Description}");
        }));
        registry.Register(new SlashCommand("dashboard", "Abre a aba Dashboard", _ => Show(dashboard)));
        registry.Register(new SlashCommand("providers", "Abre a aba Providers", _ => Show(providersView)));
        registry.Register(new SlashCommand("config", "Abre a aba Configurações", _ => Show(settings)));
        registry.Register(new SlashCommand("refresh", "Atualiza os dados do proxy", _ => Refresh()));
        registry.Register(new SlashCommand("start", "Inicia o proxy: /start [porta]", args =>
            Run(() => StartServiceAsync(config, args, cancellationToken, WriteSafe))));
        registry.Register(new SlashCommand("stop", "Para o proxy", _ =>
            Run(() => StopServiceAsync(cancellationToken, WriteSafe))));
        registry.Register(new SlashCommand("restart", "Reinicia o proxy: /restart [porta]", args =>
            Run(() => RestartServiceAsync(config, args, cancellationToken, WriteSafe))));
        registry.Register(new SlashCommand("provider-add", "Cadastra: /provider-add <id> <url> [name=] [temp=] [in=] [out=]", args =>
            Run(() => AddProviderAsync(config, args, cancellationToken))));
        registry.Register(new SlashCommand("provider-update", "Atualiza: /provider-update <id> [url=] [name=] [temp=] [in=] [out=] [active=]", args =>
            Run(() => UpdateProviderAsync(config, args, cancellationToken))));
        registry.Register(new SlashCommand("provider-delete", "Remove: /provider-delete <id>", args =>
            Run(() => DeleteProviderAsync(config, args, cancellationToken))));
        registry.Register(new SlashCommand("provider-key", "Define a API key: /provider-key <id> <chave>", args =>
            Run(() => SetProviderKeyAsync(config, args, cancellationToken))));
        registry.Register(new SlashCommand("exit", "Sai do modo interativo", _ => app.RequestStop()));

        input.Accepted += (_, _) =>
        {
            var line = input.Text?.Trim() ?? string.Empty;
            input.Text = string.Empty;

            if (line.Length == 0)
                return;

            if (!registry.TryExecute(line, Write))
                Write("Chat com agente ainda não disponível. Use /help.");
        };

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key == Key.F1)
                Show(dashboard);
            else if (key == Key.F2)
                Show(providersView);
            else if (key == Key.F3)
                Show(settings);
            else if (key == Key.F4 || key == Key.Esc)
                input.SetFocus();
            else if (key == Key.F5)
                Refresh();
            else if (key == Key.Tab)
                Show(pages[(Array.IndexOf(pages, tabs.Value) + 1) % pages.Length]);
            else
                return;

            key.Handled = true;
        };

        window.Add(tabs, log, input, shortcuts);
        input.SetFocus();
        Refresh();

        using var registration = cancellationToken.Register(() => app.Invoke(() => app.RequestStop()));
        app.Run(window);

        return Task.FromResult(0);
    }

    private static TextView CreateTextView(string title) => new()
    {
        Title = title,
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        ReadOnly = true,
        CanFocus = false
    };

    private static async Task<Snapshot> LoadAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var port = Commands.ResolvePort(config);

        try
        {
            var url = await Commands.GetRunningUrlAsync(config, cancellationToken);
            if (url is null)
                return new Snapshot(null, port, [], null);

            using var api = new ProxyApi(url);
            var providers = await api.GetProvidersAsync(cancellationToken);
            return new Snapshot(url, port, providers, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new Snapshot(null, port, [], ex.Message);
        }
    }

    private static async Task<string?> StartServiceAsync(
        CliConfig config,
        string args,
        CancellationToken cancellationToken,
        Action<string> write)
    {
        var port = ParsePort(args);

        var running = await Commands.GetRunningUrlAsync(config, cancellationToken);
        if (running is not null)
            return $"O proxy já está em execução em {running}. Use /restart para reiniciar.";

        write(port is null
            ? "Iniciando o proxy..."
            : $"Iniciando o proxy na porta {port.Value.ToString(CultureInfo.InvariantCulture)}...");

        var arguments = new List<string> { "start", "--detach" };
        if (port is not null)
        {
            arguments.Add("--port");
            arguments.Add(port.Value.ToString(CultureInfo.InvariantCulture));
        }

        var (exitCode, output) = await ServiceControl.RunAsync(arguments, cancellationToken);
        return exitCode == 0 ? output : $"Falha ao iniciar o proxy (código {exitCode}).{Environment.NewLine}{output}";
    }

    private static async Task<string?> StopServiceAsync(CancellationToken cancellationToken, Action<string> write)
    {
        write("Parando o proxy...");
        var (exitCode, output) = await ServiceControl.RunAsync(["stop"], cancellationToken);
        return exitCode == 0 ? output : $"Falha ao parar o proxy (código {exitCode}).{Environment.NewLine}{output}";
    }

    private static async Task<string?> RestartServiceAsync(
        CliConfig config,
        string args,
        CancellationToken cancellationToken,
        Action<string> write)
    {
        ParsePort(args);

        var (exitCode, output) = await ServiceControl.RunAsync(["stop"], cancellationToken);
        if (exitCode != 0)
            return $"Falha ao parar o proxy (código {exitCode}).{Environment.NewLine}{output}";

        write(output);
        return await StartServiceAsync(config, args, cancellationToken, write);
    }

    private static int? ParsePort(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return null;

        if (!int.TryParse(args.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new CliException($"Porta inválida: {args.Trim()}. Use um número entre 1 e 65535.");
        }

        return port;
    }

    private static async Task<ProxyApi> ConnectAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var url = await Commands.GetRunningUrlAsync(config, cancellationToken)
            ?? throw new CliException("O proxy está offline. Inicie com /start.");

        return new ProxyApi(url);
    }

    private static async Task<string?> AddProviderAsync(CliConfig config, string args, CancellationToken cancellationToken)
    {
        var parsed = ProviderCommandArgs.Parse(args);
        parsed.EnsureKnownOptions();

        if (parsed.Positional.Count == 0)
            throw new CliException("Uso: /provider-add <id> <url> [name=] [temp=] [in=] [out=]");

        var id = parsed.Positional[0];
        if (!parsed.Options.ContainsKey("url") && parsed.Positional.Count > 1)
            parsed.Options["url"] = parsed.Positional[1];

        if (!parsed.Options.ContainsKey("url"))
            throw new CliException("Informe a URL do provider. Uso: /provider-add <id> <url>");

        if (!parsed.Options.ContainsKey("name"))
            parsed.Options["name"] = id;

        var provider = new JsonObject { ["id"] = id, ["active"] = true };
        parsed.Apply(provider);

        using var api = await ConnectAsync(config, cancellationToken);
        if (await api.GetProviderAsync(id, cancellationToken) is not null)
            throw new CliException($"O provider '{id}' já existe. Use /provider-update.");

        await api.CreateProviderAsync(provider, cancellationToken);
        return $"Provider '{id}' cadastrado. Defina a chave com /provider-key {id} <chave>.";
    }

    private static async Task<string?> UpdateProviderAsync(CliConfig config, string args, CancellationToken cancellationToken)
    {
        var parsed = ProviderCommandArgs.Parse(args);
        parsed.EnsureKnownOptions();

        if (parsed.Positional.Count == 0 || parsed.Options.Count == 0)
            throw new CliException("Uso: /provider-update <id> [url=] [name=] [temp=] [in=] [out=] [active=]");

        var id = parsed.Positional[0];

        using var api = await ConnectAsync(config, cancellationToken);
        var provider = await api.GetProviderAsync(id, cancellationToken)
            ?? throw new CliException($"Provider '{id}' não encontrado.");

        provider.Remove("hasApiKey");
        parsed.Apply(provider);

        await api.UpdateProviderAsync(provider, cancellationToken);
        return $"Provider '{id}' atualizado.";
    }

    private static async Task<string?> DeleteProviderAsync(CliConfig config, string args, CancellationToken cancellationToken)
    {
        var id = args.Trim();
        if (id.Length == 0)
            throw new CliException("Uso: /provider-delete <id>");

        using var api = await ConnectAsync(config, cancellationToken);
        return await api.DeleteProviderAsync(id, cancellationToken)
            ? $"Provider '{id}' removido."
            : $"Provider '{id}' não encontrado.";
    }

    private static async Task<string?> SetProviderKeyAsync(CliConfig config, string args, CancellationToken cancellationToken)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            throw new CliException("Uso: /provider-key <id> <chave>");

        using var api = await ConnectAsync(config, cancellationToken);
        return await api.SetApiKeyAsync(parts[0], parts[1], cancellationToken)
            ? $"API key do provider '{parts[0]}' atualizada."
            : $"Provider '{parts[0]}' não encontrado.";
    }

    private static string BuildDashboardText(Snapshot snapshot)
    {
        var text = new StringBuilder();

        if (snapshot.Url is null)
        {
            text.AppendLine("Status: OFFLINE");
            text.AppendLine($"Porta configurada: {snapshot.ConfiguredPort.ToString(CultureInfo.InvariantCulture)}");
            if (snapshot.Error is not null)
                text.AppendLine($"Erro: {snapshot.Error}");
            text.AppendLine("Inicie o proxy com /start [porta].");
            return text.ToString();
        }

        var port = Uri.TryCreate(snapshot.Url, UriKind.Absolute, out var uri) ? uri.Port : snapshot.ConfiguredPort;
        text.AppendLine("Status: ONLINE");
        text.AppendLine($"URL:    {snapshot.Url}");
        text.AppendLine($"Porta:  {port.ToString(CultureInfo.InvariantCulture)}");
        text.AppendLine();
        text.AppendLine($"Providers cadastrados ({snapshot.Providers.Count.ToString(CultureInfo.InvariantCulture)}):");

        if (snapshot.Providers.Count == 0)
            text.AppendLine("  (nenhum)");

        foreach (var provider in snapshot.Providers)
        {
            text.AppendLine(
                $"  - {provider.Name} [{provider.Id}] {(provider.Active ? "ativo" : "inativo")}, chave: {(provider.HasApiKey ? "sim" : "não")}");
        }

        return text.ToString();
    }

    private static string BuildProvidersText(Snapshot snapshot)
    {
        if (snapshot.Url is null)
            return "Proxy offline. Inicie com /start [porta]." + Environment.NewLine + "Use F5 ou /refresh para atualizar.";

        var text = new StringBuilder();
        text.AppendLine($"{"ID",-18} {"Nome",-22} {"Ativo",-6} {"Chave",-6} URL");

        if (snapshot.Providers.Count == 0)
            text.AppendLine("(nenhum provider cadastrado)");

        foreach (var provider in snapshot.Providers)
        {
            text.AppendLine(
                $"{Truncate(provider.Id, 18),-18} {Truncate(provider.Name, 22),-22} {(provider.Active ? "sim" : "não"),-6} {(provider.HasApiKey ? "sim" : "não"),-6} {provider.BaseUrl}");
        }

        text.AppendLine();
        text.AppendLine("/provider-add <id> <url> [name=] [temp=] [in=] [out=]");
        text.AppendLine("/provider-update <id> [url=] [name=] [temp=] [in=] [out=] [active=]");
        text.AppendLine("/provider-delete <id>");
        text.AppendLine("/provider-key <id> <chave>");
        return text.ToString();
    }

    private static string BuildSettingsText(Snapshot snapshot)
    {
        if (snapshot.Url is null)
            return "Proxy offline. Não foi possível carregar os providers." + Environment.NewLine + "Use F5 ou /refresh para tentar novamente.";

        if (snapshot.Providers.Count == 0)
            return "Nenhum provider cadastrado no proxy.";

        var text = new StringBuilder();
        foreach (var provider in snapshot.Providers)
        {
            text.AppendLine($"{provider.Name}");
            text.AppendLine($"  ID:           {provider.Id}");
            text.AppendLine($"  URL:          {provider.BaseUrl}");
            text.AppendLine($"  Temperatura:  {Format(provider.Temperature)}");
            text.AppendLine($"  Tokens in:    {Format(provider.ContextLength)}");
            text.AppendLine($"  Tokens out:   {Format(provider.MaxOutputTokens)}");
            text.AppendLine($"  Ativo:        {(provider.Active ? "sim" : "não")}");
            text.AppendLine();
        }

        return text.ToString();
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string Format(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Format(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
