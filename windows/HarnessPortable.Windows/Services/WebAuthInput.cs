namespace HarnessPortable.Windows.Services;

/// <summary>
/// Turns what the user typed for the manual web login into the absolute URL
/// the embedded browser should open. Accepted forms, in order:
///
///   http://host:port/?token=…   a full URL (must carry a query)
///   ?token=…                    a bare query
///   token=…                     a key=value pair
///   b64urlToken                 the dsh web launch token itself
///
/// The authority is the profile's remote host/port — downstream code
/// (<see cref="NssmAuthUrlFetcher.RewriteToLocal"/>) rewrites it onto the
/// local forwarded port, so the stored value never pins a local port.
/// </summary>
public static class WebAuthInput
{
    /// <summary>Query parameter dsh web prints its launch token under.</summary>
    public const string TokenQueryName = "token";

    /// <summary>Normalizes the input, or null when nothing usable was typed.</summary>
    public static string? Normalize(string? input, string? host, int port)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var authority = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

        // A full URL keeps whatever the server printed; only a query makes it
        // useful for the handshake.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return trimmed.Contains('?') ? trimmed : null;
        }

        if (trimmed.StartsWith('?'))
        {
            return $"http://{authority}:{port}{trimmed}";
        }

        if (trimmed.Contains('=') && !trimmed.Contains('/') && !HasWhitespace(trimmed))
        {
            return $"http://{authority}:{port}/?{trimmed}";
        }

        // dsh's launch token is base64url ([A-Za-z0-9_-]); the prompt asks the
        // user for "the URL or the key", so a bare value is the token.
        if (!trimmed.Contains('?') && !trimmed.Contains('/') && !HasWhitespace(trimmed))
        {
            return $"http://{authority}:{port}/?{TokenQueryName}={Uri.EscapeDataString(trimmed)}";
        }

        return null;
    }

    private static bool HasWhitespace(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }
}
