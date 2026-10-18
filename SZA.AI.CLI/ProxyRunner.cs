using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class ProxyRunner
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static async Task<int> StartAttachedAsync(
        ProxyLocation location,
        int port,
        bool noBuild,
        CancellationToken cancellationToken)
    {
        EnsureBuilt(location, noBuild);
        await EnsurePortAvailableAsync(port, cancellationToken);

        var logs = new ConcurrentQueue<string>();
        var logFile = CliConfig.CurrentLogFile;
        Directory.CreateDirectory(CliConfig.LogsDirectory);
        var logLock = new object();

        void Append(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var clean = AnsiEscape.Replace(line, string.Empty).TrimEnd();
            logs.Enqueue($"{DateTime.Now:HH:mm:ss} {clean}");
            while (logs.Count > 400)
                logs.TryDequeue(out _);

            try
            {
                lock (logLock)
                    File.AppendAllText(logFile, clean + Environment.NewLine);
            }
            catch (IOException)
            {
                // Falha ao gravar o log não deve derrubar o dashboard.
            }
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(location, port),
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        var startedAt = DateTimeOffset.Now;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var layout = new Layout("root").SplitRows(
            new Layout("status") { Size = 9 },
            new Layout("logs"));

        var ready = false;
        var timedOut = false;
        var canceled = false;
        string? url = null;
        ProxyState? state = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            linkedCts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            AnsiConsole.Live(layout)
                .AutoClear(false)
                .Start(context =>
                {
                    using var probe = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
                    var deadline = DateTime.UtcNow.AddSeconds(45);

                    while (true)
                    {
                        if (process.HasExited)
                            break;

                        if (linkedCts.IsCancellationRequested)
                        {
                            canceled = true;
                            break;
                        }

                        if (!ready)
                        {
                            var candidate = port == 0
                                ? ProxyLocator.TryReadRuntimeUrl(location, startedAt)
                                : $"http://localhost:{port}";

                            if (candidate is not null && Ping(probe, candidate))
                            {
                                ready = true;
                                url = ProxyLocator.TryReadRuntimeUrl(location, startedAt) ?? candidate;

                                state = new ProxyState
                                {
                                    Pid = process.Id,
                                    Port = port == 0 ? ProxyLocator.ReadPortFromUrl(url) : port,
                                    Url = url,
                                    ExePath = location.ExePath,
                                    Detached = false,
                                    StartedAt = startedAt
                                };

                                try
                                {
                                    state.Save();
                                }
                                catch (IOException)
                                {
                                    // Estado é apenas um atalho; o proxy continua válido.
                                }
                            }

                            if (!ready && DateTime.UtcNow >= deadline)
                            {
                                timedOut = true;
                                break;
                            }
                        }

                        RenderDashboard(layout, process, ready, timedOut, url, startedAt, logs);
                        context.Refresh();
                        Thread.Sleep(200);
                    }
                });
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        if (canceled)
        {
            StopProcess(process);
            await WaitForExitAsync(process);
            DeleteState(process.Id);
            AnsiConsole.WriteLine();
            ConsoleUi.Warning("Proxy encerrado.");
            return 0;
        }

        if (timedOut)
        {
            StopProcess(process);
            await WaitForExitAsync(process);
            DeleteState(process.Id);
            AnsiConsole.WriteLine();
            ConsoleUi.Error($"O proxy não respondeu em 45 segundos. Log: {CliConfig.CurrentLogFile}");
            var tail = logs.ToArray().TakeLast(15).ToArray();
            if (tail.Length > 0)
                AnsiConsole.Write(ConsoleUi.LogPanel(tail));
            return 1;
        }

        process.WaitForExit();
        DeleteState(process.Id);
        AnsiConsole.WriteLine();

        if (process.ExitCode == 0)
        {
            ConsoleUi.Warning("O proxy encerrou.");
            return 0;
        }

        ConsoleUi.Error($"O proxy encerrou com código {process.ExitCode}. Log: {CliConfig.CurrentLogFile}");
        var lastLines = logs.ToArray().TakeLast(15).ToArray();
        if (lastLines.Length > 0)
            AnsiConsole.Write(ConsoleUi.LogPanel(lastLines));
        return 1;
    }

    public static async Task<int> StartDetachedAsync(
        ProxyLocation location,
        int port,
        bool noBuild,
        CancellationToken cancellationToken)
    {
        EnsureBuilt(location, noBuild);
        await EnsurePortAvailableAsync(port, cancellationToken);

        Directory.CreateDirectory(CliConfig.LogsDirectory);
        var logFile = CliConfig.CurrentLogFile;
        var launchedAt = DateTimeOffset.Now;

        var arguments =
            $"/c start \"\" /b \"{location.ExePath}\" --port {port.ToString(CultureInfo.InvariantCulture)} " +
            $">> \"{logFile}\" 2>&1";

        var startInfo = new ProcessStartInfo("cmd.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = location.WorkingDirectory
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

        using (var launcher = Process.Start(startInfo))
            launcher?.WaitForExit(5000);

        var (ok, url) = await WaitForReadyAsync(location, port, launchedAt, TimeSpan.FromSeconds(45), cancellationToken);

        if (!ok)
        {
            ConsoleUi.Error("O proxy não respondeu ao iniciar em segundo plano.");
            var tail = ReadTail(logFile, 20);
            if (tail.Count > 0)
                AnsiConsole.Write(ConsoleUi.LogPanel(tail));
            else
                ConsoleUi.Info($"Nenhum log encontrado em {logFile}.");
            return 1;
        }

        var pid = FindDetachedPid(location.ExePath, launchedAt);
        var state = new ProxyState
        {
            Pid = pid,
            Port = port == 0 ? ProxyLocator.ReadPortFromUrl(url) : port,
            Url = url,
            ExePath = location.ExePath,
            Detached = true,
            StartedAt = launchedAt
        };
        state.Save();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(ConsoleUi.StatusPanel(
            "Em execução (segundo plano)",
            "green",
            url,
            pid?.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            null,
            null,
            "Use `sza proxy stop` para encerrar."));
        AnsiConsole.WriteLine();
        ConsoleUi.Info($"Log: {logFile}");
        return 0;
    }

    public static int Stop(CliConfig config)
    {
        var location = ProxyLocator.Locate(config);
        var state = ProxyState.Load();
        var expectedExe = state?.ExePath ?? location?.ExePath;

        var candidates = new List<Process>();

        Process? stateProcess = null;
        if (state is not null && state.TryGetProcess(out var found))
            stateProcess = found;

        if (stateProcess is not null)
            candidates.Add(stateProcess);

        foreach (var process in Process.GetProcessesByName("SZA.AI.Proxy"))
        {
            if (candidates.Any(candidate => candidate.Id == process.Id))
            {
                process.Dispose();
                continue;
            }

            if (expectedExe is not null && !MatchesExe(process, expectedExe))
            {
                process.Dispose();
                continue;
            }

            candidates.Add(process);
        }

        var stopped = new List<int>();
        foreach (var process in candidates)
        {
            try
            {
                if (process.HasExited)
                    continue;

                var pid = process.Id;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
                stopped.Add(pid);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
            {
                // Processo pode ter encerrado entre a listagem e o kill.
            }
            finally
            {
                process.Dispose();
            }
        }

        ProxyState.Delete();

        if (stopped.Count == 0)
        {
            ConsoleUi.Warning("O proxy não está em execução.");
            return 0;
        }

        ConsoleUi.Success($"Proxy encerrado (PID {string.Join(", ", stopped)}).");
        return 0;
    }

    public static IReadOnlyList<string> ReadTail(string path, int count)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            var lines = new Queue<string>(count);
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > count)
                    lines.Dequeue();
            }

            return lines.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static bool IsPortFree(int port)
    {
        if (port == 0)
            return true;

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProxyLocation location, int port)
    {
        var startInfo = new ProcessStartInfo(location.ExePath)
        {
            WorkingDirectory = location.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        return startInfo;
    }

    private static void EnsureBuilt(ProxyLocation location, bool noBuild)
    {
        var needsBuild = !File.Exists(location.ExePath) || (!noBuild && ProxyLocator.NeedsBuild(location));
        if (!needsBuild)
            return;

        if (noBuild)
            throw new CliException($"Executável do proxy não encontrado: {location.ExePath}. Remova --no-build ou compile o projeto.");

        if (string.IsNullOrWhiteSpace(location.ProjectPath))
            throw new CliException("Não encontrei o projeto do proxy para compilar. Informe o caminho com --proxy.");

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Compilando o SZA.AI.Proxy...", _ => ProxyLocator.Build(location));
    }

    private static async Task EnsurePortAvailableAsync(int port, CancellationToken cancellationToken)
    {
        if (port == 0 || IsPortFree(port))
            return;

        if (await ProxyApi.PingAsync($"http://localhost:{port}", TimeSpan.FromSeconds(1), cancellationToken))
        {
            ConsoleUi.Warning($"Já existe um proxy respondendo em http://localhost:{port}.");
            throw new CliException("Nada a fazer: o proxy já está ativo.");
        }

        throw new CliException($"A porta {port} já está em uso. Use --port para escolher outra.");
    }

    private static void RenderDashboard(
        Layout layout,
        Process process,
        bool ready,
        bool timedOut,
        string? url,
        DateTimeOffset startedAt,
        ConcurrentQueue<string> logs)
    {
        var state = process.HasExited
            ? "Encerrado"
            : ready
                ? "Em execução"
                : timedOut
                    ? "Sem resposta"
                    : "Iniciando...";

        var color = process.HasExited || timedOut
            ? "red"
            : ready
                ? "green"
                : "yellow";

        var uptime = ConsoleUi.FormatUptime(DateTimeOffset.Now - startedAt);

        layout["status"].Update(ConsoleUi.StatusPanel(
            state,
            color,
            url,
            process.Id.ToString(CultureInfo.InvariantCulture),
            uptime,
            null,
            null,
            null,
            "Ctrl+C encerra o proxy."));

        var tail = logs.ToArray().TakeLast(14).ToArray();
        layout["logs"].Update(ConsoleUi.LogPanel(tail));
    }

    private static bool Ping(HttpClient http, string url)
    {
        try
        {
            using var response = http.GetAsync(url.TrimEnd('/') + "/api/version").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Ok, string? Url)> WaitForReadyAsync(
        ProxyLocation location,
        int port,
        DateTimeOffset startedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = port == 0
                ? ProxyLocator.TryReadRuntimeUrl(location, startedAt)
                : $"http://localhost:{port}";

            if (candidate is not null)
            {
                try
                {
                    using var response = await http.GetAsync(candidate.TrimEnd('/') + "/api/version", cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return (true, ProxyLocator.TryReadRuntimeUrl(location, startedAt) ?? candidate);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Ainda inicializando.
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        return (false, null);
    }

    private static int? FindDetachedPid(string exePath, DateTimeOffset launchedAt)
    {
        using var process = FindProxyProcess(exePath, launchedAt);
        return process?.Id;
    }

    public static Process? FindProxyProcess(string? expectedExe, DateTimeOffset? startedAfter = null)
    {
        foreach (var process in Process.GetProcessesByName("SZA.AI.Proxy"))
        {
            var keep = false;
            try
            {
                if (process.HasExited)
                    continue;

                if (startedAfter is { } threshold &&
                    process.StartTime.ToUniversalTime() < threshold.UtcDateTime.AddSeconds(-5))
                {
                    continue;
                }

                var modulePath = TryGetMainModulePath(process);
                if (expectedExe is not null && modulePath is not null && !SamePath(modulePath, expectedExe))
                    continue;

                keep = true;
                return process;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Processo de outro usuário ou já encerrado.
            }
            finally
            {
                if (!keep)
                    process.Dispose();
            }
        }

        return null;
    }

    private static bool MatchesExe(Process process, string expectedExe)
    {
        var modulePath = TryGetMainModulePath(process);
        return modulePath is null || SamePath(modulePath, expectedExe);
    }

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // Processo já encerrado.
        }
    }

    private static async Task WaitForExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            // Não há mais nada a fazer.
        }
    }

    private static void DeleteState(int pid)
    {
        var state = ProxyState.Load();
        if (state is null || state.Pid is null || state.Pid == pid)
            ProxyState.Delete();
    }
}
