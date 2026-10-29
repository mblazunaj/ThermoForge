using System.Diagnostics;
using System.Text.Json;

namespace ThermoForge;

public record Reading(string Id, string Name, string Kind, float? Value, float? Max);
public record DeviceReading(string Id, string Name, bool IsCpu, Reading[] Sensors)
{
    public Reading[] Temperatures => Sensors.Where(s => s.Kind == "Temperature").ToArray();
    public float? Hottest => Temperatures.Where(s => s.Value.HasValue).Select(s => s.Value).DefaultIfEmpty().Max();
    public float? Temperature => Temperatures.FirstOrDefault(s => s.Name.Contains(IsCpu ? "Package" : "GPU Core", StringComparison.OrdinalIgnoreCase))?.Value
        ?? Temperatures.FirstOrDefault(s => s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase))?.Value ?? Hottest;
    public float? Load => Sensors.FirstOrDefault(s => s.Kind == "Load" && (s.Name.Contains("Total") || s.Name is "GPU Core" or "D3D 3D"))?.Value;
}
public record Snapshot(long Timestamp, DeviceReading[] Devices, string? Error)
{
    public static Snapshot Empty => new(0, [], null);
    public bool Fresh(long now) => Timestamp != 0 && Stopwatch.GetElapsedTime(Timestamp, now) < TimeSpan.FromSeconds(5);
}
public static class Protection
{
    public static string? Check(Snapshot s, long now, bool cpu, string? gpuId, int cpuLimit, int gpuLimit)
    {
        if (!s.Fresh(now)) return "Zaustavljeno: očitanja senzora kasne više od 5 sekundi.";
        if (s.Error != null) return "Zaustavljeno: greška pri čitanju senzora.";
        var cpus = s.Devices.Where(d => d.IsCpu).ToArray();
        var gpu = s.Devices.FirstOrDefault(d => d.Id == gpuId);
        if (cpu && (cpus.Length == 0 || cpus.Any(d => !d.Hottest.HasValue))) return "Zaustavljeno: temperatura CPU-a nije dostupna.";
        if (gpuId != null && gpu?.Hottest == null) return "Zaustavljeno: temperatura GPU-a nije dostupna.";
        // Monitor all present sensors, even when only the other device is stressed.
        foreach (var d in s.Devices)
            if (d.Hottest is float t && t >= (d.IsCpu ? cpuLimit : gpuLimit))
                return $"Zaustavljeno: {d.Name} dosegao je {t:F0} °C (prag {(d.IsCpu ? cpuLimit : gpuLimit)} °C).";
        return null;
    }
    public static bool Expired(long start, long now, int minutes) => minutes > 0 && start != 0 && Stopwatch.GetElapsedTime(start, now) >= TimeSpan.FromMinutes(minutes);
}
public sealed class Settings
{
    public int Minutes { get; set; } = 5;
    public int CpuLimit { get; set; } = 95;
    public int GpuLimit { get; set; } = 85;
    public string? GpuName { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThermoForge");
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new();
            if (!new[] { 0, 1, 5, 10, 15, 30 }.Contains(s.Minutes)) s.Minutes = 5;
            s.CpuLimit = Math.Clamp(s.CpuLimit, 40, 110); s.GpuLimit = Math.Clamp(s.GpuLimit, 40, 105);
            return s;
        }
        catch { return new(); }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "settings.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { Diagnostics.Log("Spremanje postavki: " + e.Message); }
    }
}
public static class Diagnostics
{
    private static readonly object Gate = new();
    public static void Log(string message)
    {
        try { lock (Gate) { Directory.CreateDirectory(Settings.Folder); var p = Path.Combine(Settings.Folder, "events.log"); if (File.Exists(p) && new FileInfo(p).Length > 1_000_000) File.Move(p, p + ".old", true); File.AppendAllText(p, $"{DateTimeOffset.Now:O} {message}\n"); } } catch { }
    }
}
