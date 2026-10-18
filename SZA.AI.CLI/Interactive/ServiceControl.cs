using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SZA.AI.CLI.Interactive;

/// <summary>
/// Executa o próprio CLI em um processo filho para controlar o serviço sem
/// misturar a saída do Spectre.Console com a tela do Terminal.Gui.
/// </summary>
internal static class ServiceControl
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static async Task<(int ExitCode, string Output)> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var processPath = Environment.ProcessPath
            ?? throw new CliException("Não foi possível localizar o executável do CLI.");

        var startInfo = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entry = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(entry))
                throw new CliException("Não foi possível localizar o assembly do CLI.");

            startInfo.ArgumentList.Add(entry);
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new CliException("Falha ao iniciar o processo do CLI.");

        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        var output = Clean(await stdout + Environment.NewLine + await stderr);
        return (process.ExitCode, output);
    }

    private static string Clean(string text)
    {
        var plain = AnsiEscape.Replace(text, string.Empty);
        var lines = plain
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Any(char.IsLetterOrDigit));

        return string.Join(Environment.NewLine, lines);
    }
}
