using System.Diagnostics;

namespace SZA.AI.CLI;

/// <summary>Mantém os dados do proxy empacotado fora da pasta da ferramenta, para sobreviverem a reinstalações.</summary>
internal static class ProxyData
{
    public static string PersistentDirectory => Path.Combine(CliConfig.RootDirectory, "data");

    public static void Ensure(ProxyLocation location, bool reset)
    {
        // Proxy do repositório (com projeto) usa o próprio Data.
        if (!string.IsNullOrWhiteSpace(location.ProjectPath))
            return;

        var bundled = Path.Combine(location.WorkingDirectory, "Data");
        var persistent = PersistentDirectory;

        Directory.CreateDirectory(persistent);

        if (reset)
        {
            // Preserva os templates; apaga só o que foi cadastrado pelo usuário.
            foreach (var name in new[] { "providers.json", "models.json", "secrets.json", "runtime.json" })
                File.Delete(Path.Combine(persistent, name));

            var keys = Path.Combine(persistent, "keys");
            if (Directory.Exists(keys))
                Directory.Delete(keys, recursive: true);
        }

        if (new DirectoryInfo(bundled).LinkTarget is not null)
        {
            // Já é uma junção; o pacote novo só complementa arquivos que faltam.
            return;
        }

        if (Directory.Exists(bundled))
        {
            foreach (var file in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(persistent, Path.GetRelativePath(bundled, file));
                if (File.Exists(target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            Directory.Delete(bundled, recursive: true);
        }

        CreateJunction(bundled, persistent);
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new CliException($"Não foi possível ligar os dados do proxy a {target}: {error.Trim()}");
    }
}
