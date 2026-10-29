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
    public float? Load => IsCpu ? Sensors.FirstOrDefault(s => s.Kind == "Load" && s.Name.Contains("Total"))?.Value
        : Sensors.FirstOrDefault(s => s.Kind == "Load" && s.Name == "GPU Core")?.Value
          ?? Sensors.Where(s => s.Kind == "Load" && (s.Name == "D3D 3D" || s.Name.Contains("Compute", StringComparison.OrdinalIgnoreCase))).Select(s => s.Value).DefaultIfEmpty().Max();
}
public record Snapshot(long Timestamp, DeviceReading[] Devices, string? Error)
{
    public static Snapshot Empty => new(0, [], null);
    public bool Fresh(long now) => Timestamp != 0 && Stopwatch.GetElapsedTime(Timestamp, now) < TimeSpan.FromSeconds(5);
}
public interface ISensorSource : IDisposable
{
    Snapshot Current { get; }
}
public record GpuThermalSource(float Temperature, bool UsesCpuPackage);
public static class GpuThermals
{
    public static float? PackageFor(Snapshot snapshot, string? gpuId)
    {
        var gpu = snapshot.Devices.SingleOrDefault(d => d.Id == gpuId);
        // LibreHardwareMonitor binds this iGPU family to its Intel CPU package.
        // Never infer integration from VRAM size or use this fallback for a discrete GPU.
        if (gpu == null || gpu.IsCpu || !gpu.Id.StartsWith("/gpu-intel-integrated/", StringComparison.Ordinal)) return null;
        var cpus = snapshot.Devices.Where(d => d.IsCpu).ToArray();
        if (cpus.Length != 1) return null;
        var package = cpus[0].Temperatures.FirstOrDefault(s => s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase))?.Value;
        return package is float t && float.IsFinite(t) && t > 0 && t <= 150 ? t : null;
    }
    public static GpuThermalSource? Resolve(Snapshot snapshot, string? gpuId)
    {
        var gpu = snapshot.Devices.FirstOrDefault(d => d.Id == gpuId && !d.IsCpu);
        if (gpu?.Hottest is float direct) return new(direct, false);
        return PackageFor(snapshot, gpuId) is float package ? new(package, true) : null;
    }
}
public static class Protection
{
    public static string? Check(Snapshot s, long now, bool cpu, string? gpuId, int cpuLimit, int gpuLimit, bool useCpuPackageForGpu = false)
    {
        if (!s.Fresh(now)) return "Zaustavljeno: očitanja senzora kasne više od 5 sekundi.";
        if (s.Error != null) return "Zaustavljeno: greška pri čitanju senzora.";
        var cpus = s.Devices.Where(d => d.IsCpu).ToArray();
        var gpu = s.Devices.FirstOrDefault(d => d.Id == gpuId);
        if (cpu && (cpus.Length == 0 || cpus.Any(d => !d.Hottest.HasValue))) return "Zaustavljeno: temperatura CPU-a nije dostupna.";
        if (gpuId != null)
        {
            if (useCpuPackageForGpu)
            {
                var package = GpuThermals.PackageFor(s, gpuId);
                if (package == null) return "Zaustavljeno: temperatura CPU paketa za nadzor integriranog GPU-a nije dostupna.";
                if (package >= gpuLimit) return $"Zaustavljeno: CPU paket u nadzoru iGPU-a dosegao je {package:F0} °C (GPU prag {gpuLimit} °C).";
            }
            else if (gpu?.Hottest == null) return "Zaustavljeno: temperatura GPU-a nije dostupna.";
        }
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
