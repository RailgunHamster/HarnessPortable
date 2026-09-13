using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HarnessPortable.Windows.Services;

public static class Changelog
{
    public static string ReadEmbedded()
    {
        var assembly = typeof(Changelog).Assembly;
        using var stream = assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
        {
            return "";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Returns the markdown section for <paramref name="version"/> (from its
    /// <c>##</c> heading up to the next heading), or null if missing.
    /// </summary>
    public static string? SectionFor(string changelog, string version)
    {
        if (string.IsNullOrWhiteSpace(changelog) || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        var header = new Regex(@"^##\s+\[?" + Regex.Escape(version.Trim()) + @"\]?\s*$");
        var anyHeader = new Regex(@"^##\s+");
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (header.IsMatch(lines[i].TrimEnd()))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (anyHeader.IsMatch(lines[i].TrimEnd()))
            {
                end = i;
                break;
            }
        }

        return string.Join("\n", lines[start..end]).Trim();
    }
}
