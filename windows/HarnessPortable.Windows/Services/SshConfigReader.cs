using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HarnessPortable.Windows.Services;

public sealed record SshConfigHost(string Alias, string HostName, string? User, int Port)
{
    public string ConnectionLabel =>
        $"{(string.IsNullOrWhiteSpace(User) ? "未设置用户" : User)}@{HostName}:{Port}";
}

/// <summary>
/// Reads the small, display-safe subset of OpenSSH config needed by the
/// tunnel editor. Authentication options such as IdentityFile are ignored.
/// </summary>
public static class SshConfigReader
{
    private static readonly string[] SupportedOptions = ["hostname", "user", "port"];

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ssh",
        "config");

    public static IReadOnlyList<SshConfigHost> LoadDefault()
    {
        try
        {
            return File.Exists(DefaultPath)
                ? Parse(File.ReadAllText(DefaultPath))
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<SshConfigHost> Parse(string text)
    {
        var sections = new List<Section>();
        Section? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var (key, value) = SplitDirective(line);
            if (key.Length == 0)
            {
                continue;
            }

            if (key.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                var patterns = Tokenize(value).ToArray();
                current = patterns.Length == 0 ? null : new Section(patterns);
                if (current is not null)
                {
                    sections.Add(current);
                }

                continue;
            }

            if (current is null || !SupportedOptions.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // OpenSSH uses the first value it encounters for each option.
            current.Options.TryAdd(key, value.Trim().Trim('"', '\''));
        }

        var aliases = sections
            .SelectMany(section => section.Patterns)
            .Where(IsLiteralAlias)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<SshConfigHost>(aliases.Length);

        foreach (var alias in aliases)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections)
            {
                if (!Matches(section.Patterns, alias))
                {
                    continue;
                }

                foreach (var option in SupportedOptions)
                {
                    if (!options.ContainsKey(option) && section.Options.TryGetValue(option, out var value))
                    {
                        options[option] = value;
                    }
                }
            }

            var hostName = GetOption(options, "hostname");
            if (string.IsNullOrWhiteSpace(hostName))
            {
                hostName = alias;
            }

            var userValue = GetOption(options, "user");
            var user = string.IsNullOrWhiteSpace(userValue) ? null : userValue;
            var portValue = GetOption(options, "port");
            if (!string.IsNullOrWhiteSpace(portValue) &&
                (!int.TryParse(portValue, out var parsedPort) || parsedPort is < 1 or > 65535))
            {
                // Do not turn a malformed config into an accidental port 22 connection.
                continue;
            }

            var port = string.IsNullOrWhiteSpace(portValue) ? 22 : int.Parse(portValue);
            result.Add(new SshConfigHost(alias, hostName, user, port));
        }

        return result;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) ? value : "";

    private static bool IsLiteralAlias(string pattern) =>
        !string.IsNullOrWhiteSpace(pattern) &&
        !pattern.StartsWith('!') &&
        !pattern.Contains('*') &&
        !pattern.Contains('?');

    private static bool Matches(IReadOnlyList<string> patterns, string alias)
    {
        var hasPositive = false;
        var positiveMatch = false;
        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern.Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            if (pattern.StartsWith('!'))
            {
                if (GlobMatches(pattern[1..], alias))
                {
                    return false;
                }

                continue;
            }

            hasPositive = true;
            positiveMatch |= GlobMatches(pattern, alias);
        }

        return !hasPositive || positiveMatch;
    }

    private static bool GlobMatches(string pattern, string value)
    {
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static (string Key, string Value) SplitDirective(string line)
    {
        var separator = line.IndexOfAny([' ', '\t', '=']);
        if (separator < 0)
        {
            return (line, "");
        }

        var key = line[..separator].Trim();
        var value = line[separator..].TrimStart(' ', '\t', '=');
        return (key, value);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var token = new StringBuilder();
        var quote = '\0';
        foreach (var character in value)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    token.Append(character);
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
            else
            {
                token.Append(character);
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '#' && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }

    private sealed class Section(IReadOnlyList<string> patterns)
    {
        public IReadOnlyList<string> Patterns { get; } = patterns;
        public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
