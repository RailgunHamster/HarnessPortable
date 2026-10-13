using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public sealed record ConnectionEntry(string Key, string Name, string Address, TunnelProfile? Tunnel, string? Url,
    bool Favorite, string Group, DateTimeOffset? LastOpened);

public enum ConnectionLibraryFilter { All, Favorites, Recent }

public sealed class ConnectionLibrary(ProfileStore profiles, AppSettingsStore settings)
{
    public static string TunnelKey(string id) => "tunnel:" + id;
    public static string DirectKey(string url) => "direct:" + url;

    public IReadOnlyList<ConnectionEntry> Load(string? query = null, ConnectionLibraryFilter filter = ConnectionLibraryFilter.All)
    {
        var config = profiles.Load();
        var preferences = settings.Load().Connections;
        var entries = config.Tunnels.Select(p => Entry(TunnelKey(p.Id), p.DisplayName, p.Summary, p, null))
            .Concat(config.Directs.Select(url => Entry(DirectKey(url), ProfileStore.HostOf(url), url, null, url)));
        var search = query?.Trim() ?? "";
        var matching = entries.Where(e => search.Length == 0 ||
                (e.Name + " " + e.Address + " " + e.Group).Contains(search, StringComparison.OrdinalIgnoreCase));
        return filter switch
        {
            ConnectionLibraryFilter.Favorites => matching.Where(e => e.Favorite).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            ConnectionLibraryFilter.Recent => matching.Where(e => e.LastOpened is not null).OrderByDescending(e => e.LastOpened)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).Take(12).ToList(),
            _ => matching.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };

        ConnectionEntry Entry(string key, string name, string address, TunnelProfile? tunnel, string? url)
        {
            var pref = preferences.GetValueOrDefault(key) ?? new ConnectionPreference();
            return new(key, string.IsNullOrWhiteSpace(pref.Name) ? name : pref.Name, address, tunnel, url,
                pref.Favorite, string.IsNullOrWhiteSpace(pref.Group) ? "未分组" : pref.Group, pref.LastOpened);
        }
    }

    public void Update(string key, Action<ConnectionPreference> update)
    {
        var value = settings.Load();
        if (!value.Connections.TryGetValue(key, out var preference))
            value.Connections[key] = preference = new ConnectionPreference();
        update(preference);
        settings.Save(value);
    }

    public void MarkOpened(string key) => Update(key, p => p.LastOpened = DateTimeOffset.UtcNow);
}
