using System.Diagnostics;
using System.Text.Json;

namespace ThermoForge;

internal sealed record ThermalPeak(string Sensor, float Celsius);
internal sealed record TestResult(DateTimeOffset FinishedAt, TimeSpan Duration, bool CpuTested, string? GpuName,
    bool PackageMonitored, ThermalPeak? CpuPeak, ThermalPeak? CorePeak, ThermalPeak? GpuPeak, ThermalPeak? PackagePeak, string Reason);
internal readonly record struct TestTimeline(long ActiveStart, long LastStart, long LastStop, TestResult? LastResult);

internal sealed class TestRun(long started)
{
    internal long Started { get; } = started;
    internal bool CpuTested { get; set; }
    internal string? GpuName { get; set; }
    internal bool PackageMonitored { get; set; }
    private long lastSample, lastGpuSample;
    private ThermalPeak? cpuPeak, corePeak, gpuPeak, packagePeak;

    internal void Observe(Snapshot snapshot, long now, string? gpuId, long gpuStarted, bool packageMode)
    {
        if (snapshot.Timestamp < Started || snapshot.Timestamp > now || !snapshot.Fresh(now) || snapshot.Error != null) return;
        if (snapshot.Timestamp > lastSample)
        {
            lastSample = snapshot.Timestamp;
            foreach (var cpu in snapshot.Devices.Where(device => device.IsCpu))
                foreach (var sensor in cpu.Temperatures)
                {
                    string name = cpu.Name + " · " + sensor.Name;
                    KeepPeak(ref cpuPeak, name, sensor.Value);
                    if (DeviceMetrics.IsCore(sensor.Name)) KeepPeak(ref corePeak, name, sensor.Value);
                }
        }
        if (gpuId == null || snapshot.Timestamp < gpuStarted || snapshot.Timestamp <= lastGpuSample) return;
        lastGpuSample = snapshot.Timestamp;
        var gpu = snapshot.Devices.FirstOrDefault(device => device.Id == gpuId && !device.IsCpu);
        if (gpu != null)
            foreach (var sensor in gpu.Temperatures)
                KeepPeak(ref gpuPeak, sensor.Name, sensor.Value);
        if (packageMode)
            KeepPeak(ref packagePeak, "CPU paket · nadzor iGPU-a", GpuThermals.PackageFor(snapshot, gpuId));
    }

    internal TestResult Finish(long now, string reason) => new(DateTimeOffset.Now, Stopwatch.GetElapsedTime(Started, now),
        CpuTested, GpuName, PackageMonitored, cpuPeak, corePeak, gpuPeak, packagePeak, reason);

    private static void KeepPeak(ref ThermalPeak? peak, string name, float? reading)
    {
        if (reading is float value && float.IsFinite(value) && value is > 0 and <= 150 && (peak == null || value > peak.Celsius))
            peak = new(name, value);
    }
}

internal static class TestHistory
{
    private static string PathName => Path.Combine(Settings.Folder, "test-results.json");

    internal static TestResult[] Load()
    {
        try { return JsonSerializer.Deserialize<TestResult[]>(File.ReadAllText(PathName))?.OfType<TestResult>().Take(2).ToArray() ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    internal static void Save(TestResult[] results)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            string path = PathName;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(results));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Diagnostics.Log("Spremanje rezultata testova: " + e.Message); }
    }
}
