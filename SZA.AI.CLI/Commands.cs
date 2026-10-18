using System.Diagnostics;
using System.Globalization;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class Commands
{
    public const int DefaultPort = 11434;

    public static int ResolvePort(CliConfig config) => config.Port ?? DefaultPort;

    public static async Task<int> StartAsync(
        CliConfig config,
        int port,
        bool detach,
        bool noBuild,
        CancellationToken cancellationToken)
    {
        var location = RequireLocation(config);

        var runningUrl = await GetRunningUrlAsync(config, cancellationToken);
        if (runningUrl is not null)
        {
            ConsoleUi.Warning($"O proxy já está em execução em {runningUrl}.");
            ConsoleUi.Info("Use `sza status`, `sza logs --follow` ou `sza stop`.");
            return 0;
        }

        ConsoleUi.Info($"Executável: {location.ExePath}");

        return detach
            ? await ProxyRunner.StartDetachedAsync(location, port, noBuild, cancellationToken)
            : await ProxyRunner.StartAttachedAsync(location, port, noBuild, cancellationToken);
    }

    public static int Stop(CliConfig config) => ProxyRunner.Stop(config);

    public static async Task<int> StatusAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var state = ProxyState.Load();
        Process? process = null;
        var managed = false;
        var detached = false;
        string? url = null;
        DateTimeOffset? startedAt = null;

        if (state is not null && state.TryGetProcess(out var found))
        {
            process = found;
            managed = true;
            detached = state.Detached;
            url = state.Url ?? (state.Port > 0 ? $"http://localhost:{state.Port}" : null);
            startedAt = state.StartedAt == default ? null : state.StartedAt;
        }
        else
        {
            if (state is not null)
                ProxyState.Delete();

            url = await GetExternalUrlAsync(config, cancellationToken);
            if (url is not null)
                process = ProxyRunner.FindProxyProcess(ProxyLocator.Locate(config)?.ExePath);
        }

        using (process)
        {
            if (process is null)
            {
                AnsiConsole.Write(ConsoleUi.StatusPanel(
                    "Parado",
                    "red",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Inicie com `sza start`."));
                return 0;
            }

            var pid = process.Id.ToString(CultureInfo.InvariantCulture);
            var uptime = startedAt is not null
                ? ConsoleUi.FormatUptime(DateTimeOffset.Now - startedAt.Value)
                : SafeUptime(process);

            if (url is null || !await ProxyApi.PingAsync(url, TimeSpan.FromSeconds(3), cancellationToken))
            {
                AnsiConsole.Write(ConsoleUi.StatusPanel(
                    "Sem resposta",
                    "yellow",
                    url,
                    pid,
                    uptime,
                    null,
                    null,
                    null,
                    "O processo existe, mas a API do proxy não respondeu."));
                return 1;
            }

            using var api = new ProxyApi(url);
            var version = await TryRequestAsync(() => api.GetVersionAsync(cancellationToken));
            var providers = await TryRequestAsync(() => api.GetProvidersAsync(cancellationToken));
            var models = await TryRequestAsync(() => api.GetModelsAsync(cancellationToken));

            var label = !managed
                ? "Em execução (fora do CLI)"
                : detached
                    ? "Em execução (segundo plano)"
                    : "Em execução";

            AnsiConsole.Write(ConsoleUi.StatusPanel(
                label,
                "green",
                url,
                pid,
                uptime,
                version,
                providers?.Count.ToString(CultureInfo.InvariantCulture),
                models?.Count.ToString(CultureInfo.InvariantCulture)));
            return 0;
        }
    }

    public static async Task<int> ProvidersAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var url = await RequireRunningUrlAsync(config, cancellationToken);
        using var api = new ProxyApi(url);

        var providers = await FetchAsync(
            () => api.GetProvidersAsync(cancellationToken),
            "os providers");

        if (providers.Count == 0)
        {
            ConsoleUi.Warning("Nenhum provider configurado no proxy.");
            return 0;
        }

        AnsiConsole.Write(ConsoleUi.ProvidersTable(providers));
        ConsoleUi.Info($"{providers.Count} provider(s) em {url}");
        return 0;
    }

    public static async Task<int> ModelsAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var url = await RequireRunningUrlAsync(config, cancellationToken);
        using var api = new ProxyApi(url);

        var models = await FetchAsync(
            () => api.GetModelsAsync(cancellationToken),
            "os modelos");

        if (models.Count == 0)
        {
            ConsoleUi.Warning("Nenhum modelo configurado no proxy.");
            return 0;
        }

        AnsiConsole.Write(ConsoleUi.ModelsTable(models));
        ConsoleUi.Info($"{models.Count} modelo(s) em {url}");
        return 0;
    }

    public static async Task<int> LogsAsync(
        CliConfig config,
        int lines,
        bool follow,
        CancellationToken cancellationToken)
    {
        var logFile = ResolveLogFile();
        if (logFile is null)
        {
            ConsoleUi.Warning($"Nenhum log encontrado em {CliConfig.LogsDirectory}.");
            ConsoleUi.Info("Os logs são criados quando o proxy é iniciado pelo CLI.");
            return 0;
        }

        AnsiConsole.Write(ConsoleUi.LogPanel(ProxyRunner.ReadTail(logFile, lines)));
        ConsoleUi.Info($"Arquivo: {logFile}");

        if (!follow)
            return 0;

        ConsoleUi.Info("Acompanhando novos logs... (Ctrl+C encerra)");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.CancelKeyPress += handler;
        try
        {
            using var stream = new FileStream(
                logFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(0, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            var buffer = new char[8192];

            while (!cts.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cts.Token);
                if (read > 0)
                {
                    Console.Out.Write(buffer, 0, read);
                    Console.Out.Flush();
                }
                else
                {
                    await Task.Delay(300, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C encerra o acompanhamento.
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        return 0;
    }

    public static async Task<int> OpenAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var url = await RequireRunningUrlAsync(config, cancellationToken);
        var target = url.TrimEnd('/') + "/scalar/v1";

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new CliException($"Não foi possível abrir o navegador: {ex.Message}");
        }

        ConsoleUi.Success($"Abrindo {target}");
        return 0;
    }

    public static int Config(CliConfig config, int? port, string? proxyPath, bool reset, bool show)
    {
        if (reset)
        {
            config.Port = null;
            config.ProxyPath = null;
            config.Save();
            ConsoleUi.Success("Configuração restaurada para os padrões.");
            AnsiConsole.Write(ConsoleUi.ConfigTable(config, ProxyLocator.Locate(config)));
            return 0;
        }

        var changed = false;

        if (port is not null && config.Port != port)
        {
            config.Port = port;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(proxyPath))
        {
            var candidate = new CliConfig { Port = config.Port, ProxyPath = proxyPath };
            if (ProxyLocator.Locate(candidate) is null)
                throw new CliException($"Não encontrei o proxy em {proxyPath}. Informe o .csproj, o .exe ou a pasta do projeto.");

            var fullPath = Path.GetFullPath(proxyPath);
            if (!string.Equals(config.ProxyPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                config.ProxyPath = fullPath;
                changed = true;
            }
        }

        if (changed)
        {
            config.Save();
            ConsoleUi.Success("Configuração salva.");
        }

        AnsiConsole.Write(ConsoleUi.ConfigTable(config, ProxyLocator.Locate(config)));

        if (!changed && !show)
            ConsoleUi.Info("Use --port <n> ou --proxy <caminho> para alterar, ou --reset para restaurar.");

        return 0;
    }

    public static async Task<int> InteractiveAsync(
        CliConfig config,
        CancellationToken cancellationToken = default)
    {
        ConsoleUi.Banner();

        while (!cancellationToken.IsCancellationRequested)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[grey]O que você quer fazer?[/]")
                    .PageSize(12)
                    .AddChoices(
                        "Iniciar com dashboard (foreground)",
                        "Iniciar em segundo plano",
                        "Status",
                        "Providers",
                        "Modelos",
                        "Logs (últimas linhas)",
                        "Abrir documentação",
                        "Configuração",
                        "Sair"));

            try
            {
                switch (choice)
                {
                    case "Iniciar com dashboard (foreground)":
                        await StartAsync(config, ResolvePort(config), detach: false, noBuild: false, cancellationToken);
                        break;

                    case "Iniciar em segundo plano":
                        await StartAsync(config, ResolvePort(config), detach: true, noBuild: false, cancellationToken);
                        break;

                    case "Status":
                        await StatusAsync(config, cancellationToken);
                        break;

                    case "Providers":
                        await ProvidersAsync(config, cancellationToken);
                        break;

                    case "Modelos":
                        await ModelsAsync(config, cancellationToken);
                        break;

                    case "Logs (últimas linhas)":
                        await LogsAsync(config, 40, follow: false, cancellationToken);
                        break;

                    case "Abrir documentação":
                        await OpenAsync(config, cancellationToken);
                        break;

                    case "Configuração":
                        Config(config, null, null, reset: false, show: true);
                        break;

                    case "Sair":
                        return 0;
                }
            }
            catch (CliException ex)
            {
                ConsoleUi.Error(ex.Message);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static ProxyLocation RequireLocation(CliConfig config)
    {
        var location = ProxyLocator.Locate(config);
        if (location is null)
        {
            throw new CliException(
                "Não encontrei o projeto SZA.AI.Proxy. Rode o CLI de dentro do repositório ou use `sza config --proxy <caminho>`.");
        }

        if (!Directory.Exists(location.WorkingDirectory))
            throw new CliException($"O diretório de trabalho do proxy não existe: {location.WorkingDirectory}");

        return location;
    }

    private static async Task<string> RequireRunningUrlAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var url = await GetRunningUrlAsync(config, cancellationToken);
        return url ?? throw new CliException("O proxy não está em execução. Inicie com `sza start --detach`.");
    }

    private static async Task<string?> GetRunningUrlAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var state = ProxyState.Load();
        if (state is not null)
        {
            if (!state.TryGetProcess(out var process))
            {
                ProxyState.Delete();
            }
            else
            {
                process.Dispose();

                var stateUrl = state.Url ?? (state.Port > 0 ? $"http://localhost:{state.Port}" : null);
                if (stateUrl is not null &&
                    await ProxyApi.PingAsync(stateUrl, TimeSpan.FromSeconds(3), cancellationToken))
                {
                    return stateUrl;
                }
            }
        }

        return await GetExternalUrlAsync(config, cancellationToken);
    }

    private static async Task<string?> GetExternalUrlAsync(CliConfig config, CancellationToken cancellationToken)
    {
        var location = ProxyLocator.Locate(config);

        var runtimeUrl = ProxyLocator.TryReadRuntimeUrl(location);
        if (runtimeUrl is not null &&
            await ProxyApi.PingAsync(runtimeUrl, TimeSpan.FromSeconds(2), cancellationToken))
        {
            return runtimeUrl;
        }

        var fallbackUrl = $"http://localhost:{ResolvePort(config)}";
        return await ProxyApi.PingAsync(fallbackUrl, TimeSpan.FromSeconds(2), cancellationToken)
            ? fallbackUrl
            : null;
    }

    private static string? SafeUptime(Process process)
    {
        try
        {
            return ConsoleUi.FormatUptime(DateTimeOffset.Now - process.StartTime);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ResolveLogFile()
    {
        if (File.Exists(CliConfig.CurrentLogFile))
            return CliConfig.CurrentLogFile;

        if (!Directory.Exists(CliConfig.LogsDirectory))
            return null;

        try
        {
            return Directory
                .EnumerateFiles(CliConfig.LogsDirectory, "proxy-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<T?> TryRequestAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return default;
        }
    }

    private static async Task<T> FetchAsync<T>(Func<Task<T>> action, string description)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex)
        {
            throw new CliException($"Falha ao consultar {description}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new CliException($"Tempo esgotado ao consultar {description}.");
        }
    }
}
