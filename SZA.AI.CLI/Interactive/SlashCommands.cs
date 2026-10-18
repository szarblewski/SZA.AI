namespace SZA.AI.CLI.Interactive;

internal sealed class SlashCommand(string name, string description, Action<string> execute)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public void Execute(string args) => execute(args);
}

internal sealed class SlashCommandRegistry
{
    private readonly Dictionary<string, SlashCommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<SlashCommand> All => _commands.Values;

    public void Register(SlashCommand command) => _commands[command.Name] = command;

    public bool TryExecute(string line, Action<string> write)
    {
        if (!line.StartsWith('/'))
            return false;

        var parts = line[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0] : string.Empty;
        var args = parts.Length > 1 ? parts[1] : string.Empty;

        if (_commands.TryGetValue(name, out var command))
            command.Execute(args);
        else
            write($"Comando desconhecido: /{name}. Use /help.");

        return true;
    }
}
