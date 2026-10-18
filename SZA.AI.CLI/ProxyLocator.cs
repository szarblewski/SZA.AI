using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SZA.AI.CLI;

internal sealed record ProxyLocation(string ProjectPath, string ExePath, string WorkingDirectory);

internal static class ProxyLocator
{
    private const string ProxyProjectName = "SZA.AI.Proxy";
    private const string ProxyAssemblyName = "SZA.AI.Proxy.exe";

    public static ProxyLocation? Locate(CliConfig config)
    {
        var explicitPath = config.ProxyPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return FromPath(explicitPath);

        foreach (var root in CandidateRoots())
        {
            var project = Path.Combine(root, ProxyProjectName, ProxyProjectName + ".csproj");
            if (File.Exists(project))
                return FromProject(project);
        }

        var bundledDirectory = Path.Combine(AppContext.BaseDirectory, "proxy");
        var bundledExe = Path.Combine(bundledDirectory, ProxyAssemblyName);
        if (File.Exists(bundledExe))
            return new ProxyLocation(string.Empty, bundledExe, bundledDirectory);

        return null;
    }

    public static bool NeedsBuild(ProxyLocation location)
    {
        if (!File.Exists(location.ExePath))
            return true;

        if (string.IsNullOrWhiteSpace(location.ProjectPath) || !File.Exists(location.ProjectPath))
            return false;

        var projectDirectory = Path.GetDirectoryName(location.ProjectPath)!;
        var exeTimestamp = File.GetLastWriteTimeUtc(location.ExePath);

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(projectDirectory, file);
            if (IsBuildArtifact(relative))
                continue;

            var extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".csproj" or ".json"))
                continue;

            if (File.GetLastWriteTimeUtc(file) > exeTimestamp)
                return true;
        }

        return false;
    }

    public static void Build(ProxyLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.ProjectPath) || !File.Exists(location.ProjectPath))
            throw new CliException("NÃ£o encontrei o projeto SZA.AI.Proxy para compilar. Informe o caminho com --proxy.");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(location.ProjectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Debug");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("minimal");

        using var process = Process.Start(startInfo)
            ?? throw new CliException("NÃ£o foi possÃ­vel iniciar o dotnet build.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = string.Join(Environment.NewLine, new[] { stdout.Result, stderr.Result }
            .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (process.ExitCode != 0)
            throw new CliException($"Falha ao compilar o SZA.AI.Proxy.{Environment.NewLine}{output.Trim()}");

        if (!File.Exists(location.ExePath))
            throw new CliException($"A compilaÃ§Ã£o terminou, mas o executÃ¡vel nÃ£o foi encontrado em {location.ExePath}.");
    }

    public static int ReadPortFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return 0;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : 0;
    }

    public static string? TryReadRuntimeUrl(ProxyLocation? location, DateTimeOffset? startedAfter = null)
    {
        if (location is null)
            return null;

        var runtimePath = Path.Combine(location.WorkingDirectory, "Data", "runtime.json");
        if (!File.Exists(runtimePath))
            return null;

        if (startedAfter is { } threshold &&
            File.GetLastWriteTimeUtc(runtimePath) < threshold.UtcDateTime.AddSeconds(-3))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runtimePath));
            if (!document.RootElement.TryGetProperty("addresses", out var addresses) ||
                addresses.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? firstAddress = null;
            foreach (var address in addresses.EnumerateArray())
            {
                var value = address.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                firstAddress ??= value.TrimEnd('/');
                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    return value.TrimEnd('/');
            }

            return firstAddress;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ProxyLocation? FromPath(string path)
    {
        try
        {
            path = Path.GetFullPath(path.Trim('"'));

            if (File.Exists(path))
            {
                if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    return FromProject(path);

                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return new ProxyLocation(FindProjectNear(path) ?? string.Empty, path, Path.GetDirectoryName(path)!);
            }

            if (Directory.Exists(path))
            {
                var project = Directory.EnumerateFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (project is not null)
                    return FromProject(project);

                var nestedProject = Path.Combine(path, ProxyProjectName, ProxyProjectName + ".csproj");
                if (File.Exists(nestedProject))
                    return FromProject(nestedProject);

                var directExe = Path.Combine(path, ProxyAssemblyName);
                if (File.Exists(directExe))
                    return new ProxyLocation(FindProjectNear(directExe) ?? string.Empty, directExe, path);

                var binExe = FindBuiltExe(path);
                if (binExe is not null)
                    return new ProxyLocation(FindProjectNear(binExe) ?? string.Empty, binExe, Path.GetDirectoryName(binExe)!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        return null;
    }

    private static ProxyLocation FromProject(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var exePath = FindBuiltExe(projectDirectory)
            ?? Path.Combine(projectDirectory, "bin", "Debug", "net10.0", ProxyAssemblyName);

        return new ProxyLocation(projectPath, exePath, Path.GetDirectoryName(exePath)!);
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var roots = new List<string>();
        AddAncestors(roots, Directory.GetCurrentDirectory());
        AddAncestors(roots, AppContext.BaseDirectory);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddAncestors(List<string> roots, string start)
    {
        try
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                roots.Add(directory.FullName);
                directory = directory.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Ignora caminhos invÃ¡lidos.
        }
    }

    private static string? FindBuiltExe(string projectDirectory)
    {
        var binDirectory = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binDirectory))
            return null;

        try
        {
            return Directory
                .EnumerateFiles(binDirectory, ProxyAssemblyName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindProjectNear(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        while (directory is not null)
        {
            var project = Path.Combine(directory.FullName, ProxyProjectName + ".csproj");
            if (File.Exists(project))
                return project;

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsBuildArtifact(string relativePath)
    {
        var separator = Path.DirectorySeparatorChar;
        return relativePath.StartsWith("bin" + separator, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("obj" + separator, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("logs" + separator, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(".vs" + separator, StringComparison.OrdinalIgnoreCase);
    }
}
