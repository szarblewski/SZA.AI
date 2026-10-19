using System.Diagnostics;
using Microsoft.Win32;

namespace SZA.AI.CLI;

internal static class AutostartCommands
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SZA.AI.Proxy";

    public static int Run(string? action)
    {
        if (!OperatingSystem.IsWindows())
            throw new CliException("A inicialização automática só é suportada no Windows.");

        switch ((action ?? string.Empty).ToLowerInvariant())
        {
            case "on":
                return Enable();
            case "off":
                return Disable();
            case "status":
                return Status();
            default:
                throw new CliException("Use: sza proxy autostart on|off|status");
        }
    }

    private static int Enable()
    {
        var command = BuildCommand();
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, command);
        ConsoleUi.Success("O proxy será iniciado junto com o Windows.");
        return 0;
    }

    private static int Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key.GetValue(ValueName) is null)
        {
            ConsoleUi.Info("A inicialização automática já está desativada.");
            return 0;
        }

        key.DeleteValue(ValueName);
        ConsoleUi.Success("Inicialização automática do proxy desativada.");
        return 0;
    }

    private static int Status()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(ValueName) is string value)
            ConsoleUi.Info($"Ativada: {value}");
        else
            ConsoleUi.Info("Desativada.");
        return 0;
    }

    private static string BuildCommand()
    {
        var processPath = Environment.ProcessPath
            ?? throw new CliException("Não foi possível determinar o caminho do executável.");

        return $"cmd.exe /c start \"\" /min \"{processPath}\" proxy start --detach";
    }
}
