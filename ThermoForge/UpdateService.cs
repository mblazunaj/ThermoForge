using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThermoForge;

public record ReleaseInfo(Version Version, string InstallerUrl, string HashUrl, string FileName);
public static class UpdateService
{
    public const string Repository = "mblazunaj/ThermoForge";
    public static Version Version { get; } = GetVersion();
    private static Version GetVersion() { var v = typeof(UpdateService).Assembly.GetName().Version!; return new(v.Major, v.Minor, v.Build); }
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThermoForge/" + Version);
        return client;
    }
    public static bool TrustedAssetUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "github.com" && u.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal) && u.UserInfo.Length == 0;
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (!System.Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var version) || version <= Version) return null;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var filename = $"ThermoForge-Setup-{version}.exe";
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        string? Url(string name) => assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name) is var a && a.ValueKind == JsonValueKind.Object ? a.GetProperty("browser_download_url").GetString() : null;
        var installer = Url(filename); var hash = Url(filename + ".sha256");
        if (installer == null || hash == null || !TrustedAssetUrl(installer) || !TrustedAssetUrl(hash)) throw new InvalidDataException("Objava nema očekivanu instalaciju i SHA-256 kontrolnu datoteku.");
        return new(version, installer, hash, filename);
    }
    public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct)
    {
        if (!TrustedAssetUrl(release.InstallerUrl) || !TrustedAssetUrl(release.HashUrl) || Path.GetFileName(release.FileName) != release.FileName) throw new InvalidDataException("Neispravna adresa nadogradnje.");
        using var client = CreateClient();
        var hashText = await client.GetStringAsync(release.HashUrl, ct);
        var hash = hashText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash?.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Neispravan SHA-256.");
        var folder = Path.Combine(Settings.Folder, "Updates"); Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, release.FileName); var partial = target + ".partial";
        try
        {
            using var response = await client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 250_000_000) throw new InvalidDataException("Instalacija je prevelika.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(bytes, ct)) > 0) { total += read; if (total > 250_000_000) throw new InvalidDataException("Instalacija je prevelika."); await output.WriteAsync(bytes.AsMemory(0, read), ct); }
            }
            await using (var stream = File.OpenRead(partial))
            { var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)); if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Kontrolni zbroj se ne podudara. Instalacija nije pokrenuta."); }
            File.Move(partial, target, true); return target;
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
    }
}
