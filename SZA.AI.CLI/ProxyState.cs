using System.Diagnostics;
using System.Text.Json;

namespace SZA.AI.CLI;

internal sealed class ProxyState
{
    public int? Pid { get; set; }
    public int Port { get; set; }
    public string? Url { get; set; }
    public string? ExePath { get; set; }
    public bool Detached { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ProxyState? Load()
    {
        try
        {
            if (!File.Exists(CliConfig.StatePath))
                return null;

            return JsonSerializer.Deserialize<ProxyState>(File.ReadAllText(CliConfig.StatePath), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(CliConfig.RootDirectory);
        var tempPath = CliConfig.StatePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tempPath, CliConfig.StatePath, overwrite: true);
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(CliConfig.StatePath))
                File.Delete(CliConfig.StatePath);
        }
        catch
        {
            // Estado obsoleto não deve impedir o encerramento do CLI.
        }
    }

    public bool TryGetProcess(out Process process)
    {
        process = null!;

        if (Pid is null or <= 0)
            return false;

        try
        {
            var candidate = Process.GetProcessById(Pid.Value);
            if (candidate.HasExited ||
                !string.Equals(candidate.ProcessName, "SZA.AI.Proxy", StringComparison.OrdinalIgnoreCase))
            {
                candidate.Dispose();
                return false;
            }

            process = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
