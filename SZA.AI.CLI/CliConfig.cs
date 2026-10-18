using System.Text.Json;
using System.Text.Json.Serialization;

namespace SZA.AI.CLI;

internal sealed class CliConfig
{
    public int? Port { get; set; }
    public string? ProxyPath { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SZA.AI");

    public static string FilePath => Path.Combine(RootDirectory, "cli.json");
    public static string StatePath => Path.Combine(RootDirectory, "proxy-state.json");
    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public static string CurrentLogFile => Path.Combine(LogsDirectory, $"proxy-{DateTime.Now:yyyyMMdd}.log");

    public static CliConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new CliConfig();

            return JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new CliConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new CliConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(RootDirectory);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}

internal sealed class CliException(string message) : Exception(message);
