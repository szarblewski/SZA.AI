using System.Diagnostics;
using Spectre.Console;

namespace SZA.AI.CLI;

internal static class InstallCommands
{
    private static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "sza",
        "bin");

    public static int Install(string? path)
    {
        var target = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultInstallDirectory : path);
        var source = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        if (!string.Equals(source, Path.TrimEndingDirectorySeparator(target), PathComparison))
        {
            try
            {
                CopyDirectory(source, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CliException($"Falha ao copiar os arquivos para {target}: {ex.Message}");
            }

            ConsoleUi.Success($"Arquivos copiados para {target}");
        }

        if (OperatingSystem.IsWindows())
        {
            if (AddToUserPath(target))
                ConsoleUi.Success("Diretório adicionado ao PATH do usuário.");
            else
                ConsoleUi.Info("O diretório já está no PATH do usuário.");

            ConsoleUi.Info("Abra um novo terminal e execute `sza --help`.");
        }
        else
        {
            ConsoleUi.Warning($"Adicione {target} ao PATH manualmente (ex.: export PATH=\"$PATH:{target}\").");
        }

        return 0;
    }

    public static int Uninstall(string? path)
    {
        var target = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? DefaultInstallDirectory : path);

        if (OperatingSystem.IsWindows() && RemoveFromUserPath(target))
            ConsoleUi.Success("Diretório removido do PATH do usuário.");

        var removedTool = UninstallDotnetTool();

        if (!Directory.Exists(target))
        {
            if (!removedTool)
                ConsoleUi.Info($"Nada para remover em {target}.");
            return 0;
        }

        var running = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var runningFromTarget = string.Equals(running, Path.TrimEndingDirectorySeparator(target), PathComparison);

        if (runningFromTarget && OperatingSystem.IsWindows())
        {
            var command = $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{target}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            ConsoleUi.Success($"Remoção de {target} agendada.");
            return 0;
        }

        try
        {
            Directory.Delete(target, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliException($"Falha ao remover {target}: {ex.Message}");
        }

        ConsoleUi.Success($"Removido {target}");
        return 0;
    }

    private static bool UninstallDotnetTool()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var running = AppContext.BaseDirectory;
        var storeMarker = Path.Combine(".dotnet", "tools", ".store") + Path.DirectorySeparatorChar;
        if (!running.Contains(storeMarker, StringComparison.OrdinalIgnoreCase))
            return false;

        // O processo atual mantém os arquivos da ferramenta em uso; a remoção roda depois que ele encerrar.
        var command = "/c ping -n 3 127.0.0.1 >nul & dotnet tool uninstall --global SZA.AI.CLI";
        Process.Start(new ProcessStartInfo("cmd.exe", command)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        ConsoleUi.Success("Desinstalação da ferramenta global do dotnet (SZA.AI.CLI) agendada.");
        return true;
    }
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool AddToUserPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        if (entries.Any(e => IsSameDirectory(e, directory)))
            return false;

        var updated = string.Join(Path.PathSeparator, entries.Append(directory));
        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.User);
        return true;
    }

    private static bool RemoveFromUserPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var entries = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var remaining = entries.Where(e => !IsSameDirectory(e, directory)).ToArray();

        if (remaining.Length == entries.Length)
            return false;

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, remaining), EnvironmentVariableTarget.User);
        return true;
    }

    private static bool IsSameDirectory(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a.Trim()),
            Path.TrimEndingDirectorySeparator(b),
            PathComparison);
}
