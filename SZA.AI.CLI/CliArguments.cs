using System.Globalization;

namespace SZA.AI.CLI;

/// <summary>Argumentos já interpretados da linha de comando: posicionais, opções com valor e flags.</summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Argumentos posicionais após o grupo e o subcomando.</summary>
    public IReadOnlyList<string> Args { get; set; } = [];

    public void AddValue(string name, string value)
    {
        if (!_values.TryGetValue(name, out var list))
            _values[name] = list = [];
        list.Add(value);
    }

    public void AddFlag(string name) => _flags.Add(name);

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) =>
        _values.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> GetAll(string name) =>
        _values.TryGetValue(name, out var list) ? list : [];

    public int? GetInt(string name, int min, int max)
    {
        var value = Get(name);
        if (value is null)
            return null;

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            number < min || number > max)
        {
            throw new CliException($"Valor inválido para --{name}: {value} (esperado entre {min} e {max}).");
        }

        return number;
    }

    public double? GetDouble(string name, double min, double max)
    {
        var value = Get(name);
        if (value is null)
            return null;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            number < min || number > max)
        {
            throw new CliException($"Valor inválido para --{name}: {value} (esperado entre {min.ToString(CultureInfo.InvariantCulture)} e {max.ToString(CultureInfo.InvariantCulture)}).");
        }

        return number;
    }
}
