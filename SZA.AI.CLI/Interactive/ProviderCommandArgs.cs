using System.Globalization;
using System.Text.Json.Nodes;

namespace SZA.AI.CLI.Interactive;

internal sealed class ProviderCommandArgs
{
    public List<string> Positional { get; } = [];
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProviderCommandArgs Parse(string line)
    {
        var parsed = new ProviderCommandArgs();

        foreach (var token in Tokenize(line))
        {
            var separator = token.IndexOf('=');
            if (separator > 0)
                parsed.Options[token[..separator]] = token[(separator + 1)..];
            else
                parsed.Positional.Add(token);
        }

        return parsed;
    }

    public void Apply(JsonObject provider)
    {
        if (Options.TryGetValue("name", out var name))
            provider["name"] = name;

        if (Options.TryGetValue("url", out var url))
            provider["baseUrl"] = url;

        if (Options.TryGetValue("temp", out var temp))
            provider["temperature"] = ParseNullable<double>(temp, "temp", value =>
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null);

        if (Options.TryGetValue("in", out var tokensIn))
            provider["contextLength"] = ParseNullable<int>(tokensIn, "in", ParseInt);

        if (Options.TryGetValue("out", out var tokensOut))
            provider["maxOutputTokens"] = ParseNullable<int>(tokensOut, "out", ParseInt);

        if (Options.TryGetValue("active", out var active))
        {
            if (!bool.TryParse(active, out var flag))
                throw new CliException("Valor inválido para active. Use true ou false.");

            provider["active"] = flag;
        }
    }

    public void EnsureKnownOptions()
    {
        string[] known = ["name", "url", "temp", "in", "out", "active"];
        var unknown = Options.Keys.FirstOrDefault(key => !known.Contains(key, StringComparer.OrdinalIgnoreCase));
        if (unknown is not null)
            throw new CliException($"Opção desconhecida: {unknown}. Use name, url, temp, in, out ou active.");
    }

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static JsonNode? ParseNullable<T>(string value, string option, Func<string, T?> parse)
        where T : struct
    {
        if (value.Length == 0 || value == "-")
            return null;

        var parsed = parse(value);
        if (parsed is null)
            throw new CliException($"Valor inválido para {option}: {value}");

        return JsonValue.Create(parsed.Value);
    }

    private static IEnumerable<string> Tokenize(string line)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var hasToken = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                quoted = !quoted;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (hasToken)
                    yield return current.ToString();

                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(character);
                hasToken = true;
            }
        }

        if (hasToken)
            yield return current.ToString();
    }
}
