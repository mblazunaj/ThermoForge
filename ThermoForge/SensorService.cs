using LibreHardwareMonitor.Hardware;
using System.Diagnostics;

namespace ThermoForge;

public sealed class SensorService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private Snapshot current = Snapshot.Empty;
    public Snapshot Current => Volatile.Read(ref current);
    public SensorService() => new Thread(Run) { IsBackground = true, Name = "Temperature sensors" }.Start();
    private void Run()
    {
        Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
        try
        {
            computer.Open();
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var devices = new List<DeviceReading>();
                    foreach (var h in computer.Hardware)
                    {
                        h.Update();
                        var sensors = new List<Reading>();
                        Collect(h, sensors);
                        devices.Add(new(h.Identifier.ToString(), h.Name, h.HardwareType == HardwareType.Cpu, sensors.ToArray()));
                    }
                    Volatile.Write(ref current, new(Stopwatch.GetTimestamp(), devices.ToArray(), null));
                }
                catch (Exception e) { Volatile.Write(ref current, new(Stopwatch.GetTimestamp(), [], e.Message)); Diagnostics.Log("Senzori: " + e.Message); }
                stop.Token.WaitHandle.WaitOne(1000);
            }
        }
        catch (Exception e) { Volatile.Write(ref current, new(Stopwatch.GetTimestamp(), [], e.Message)); Diagnostics.Log("Otvaranje senzora: " + e); }
        finally { try { computer.Close(); } catch { } }
    }
    private static void Collect(IHardware h, List<Reading> sensors)
    {
        foreach (var s in h.Sensors)
        {
            // A distance to TjMax is a margin, not an absolute temperature.
            if (s.SensorType == SensorType.Temperature && s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)) continue;
            float? value = s.Value;
            if (value is float v && (!float.IsFinite(v) || (s.SensorType == SensorType.Temperature && (v <= 0 || v > 150)))) value = null;
            sensors.Add(new(s.Identifier.ToString(), s.Name, s.SensorType.ToString(), value, s.Max));
        }
        foreach (var sub in h.SubHardware) { sub.Update(); Collect(sub, sensors); }
    }
    public void Dispose() => stop.Cancel();
}
