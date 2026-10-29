using System.Diagnostics;

namespace ThermoForge;

public sealed class StressController : IDisposable
{
    private readonly object gate = new();
    private readonly SensorService sensors;
    private readonly Settings settings;
    private readonly CancellationTokenSource stop = new();
    private Process? cpu, gpu;
    private string? gpuId;
    private long started;
    private long lastUiPulse = Stopwatch.GetTimestamp();
    public string Message { get; private set; } = "Spremno. Odaberi trajanje i pokreni CPU ili GPU test.";
    public bool CpuRunning { get { lock (gate) return cpu != null; } }
    public bool GpuRunning { get { lock (gate) return gpu != null; } }
    public bool Running => CpuRunning || GpuRunning;
    public TimeSpan Elapsed { get { lock (gate) return started == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(started); } }
    public void UiPulse() => Interlocked.Exchange(ref lastUiPulse, Stopwatch.GetTimestamp());
    public StressController(SensorService sensors, Settings settings)
    {
        this.sensors = sensors; this.settings = settings;
        new Thread(Watch) { IsBackground = true, Name = "Independent safety watchdog", Priority = ThreadPriority.AboveNormal }.Start();
    }
    public string? StartCpu() => Start(true, null, null);
    public string? StartGpu(GpuAdapter adapter, string sensorId) => Start(false, adapter, sensorId);
    private string? Start(bool isCpu, GpuAdapter? adapter, string? sensorId)
    {
        lock (gate)
        {
            if ((isCpu ? cpu : gpu) != null) return null;
            var error = Protection.Check(sensors.Current, Stopwatch.GetTimestamp(), isCpu || cpu != null, sensorId ?? gpuId, settings.CpuLimit, settings.GpuLimit);
            if (error != null) return error;
            if (Protection.Expired(started, Stopwatch.GetTimestamp(), settings.Minutes)) { StopAllLocked("Timer je istekao."); return "Timer je istekao. Pokreni novi test."; }
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(isCpu ? "--worker-cpu" : "--worker-gpu");
            if (!isCpu) info.ArgumentList.Add(adapter!.Index.ToString());
            var p = new Process { StartInfo = info };
            try
            {
                p.Start();
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Diagnostics.Log("Worker: " + e.Data); };
                p.BeginErrorReadLine();
                // Drain stdout continuously; no blocked worker on full pipe.
                p.OutputDataReceived += (_, e) => { if (e.Data != null) Diagnostics.Log("Worker: " + e.Data); };
                p.BeginOutputReadLine();
                if (isCpu) cpu = p; else { gpu = p; gpuId = sensorId; }
                if (started == 0) started = Stopwatch.GetTimestamp();
                Message = "Test je aktivan. Temperaturna zaštita je uključena.";
                Diagnostics.Log($"START {(isCpu ? "CPU" : adapter!.Name)}; timer={settings.Minutes}; CPU={settings.CpuLimit}; GPU={settings.GpuLimit}");
                return null;
            }
            catch (Exception e) { p.Dispose(); return "Test se ne može pokrenuti: " + e.Message; }
        }
    }
    public void StopCpu() { lock (gate) { Kill(ref cpu); ResetIfIdle(); Message = "CPU test je zaustavljen."; } }
    public void StopGpu() { lock (gate) { Kill(ref gpu); gpuId = null; ResetIfIdle(); Message = "GPU test je zaustavljen."; } }
    public void StopAll(string reason = "Oba testa su zaustavljena.") { lock (gate) StopAllLocked(reason); }
    private void StopAllLocked(string reason) { Kill(ref cpu); Kill(ref gpu); gpuId = null; started = 0; Message = reason; Diagnostics.Log(reason); }
    private void ResetIfIdle() { if (cpu == null && gpu == null) started = 0; }
    private static void Kill(ref Process? p)
    {
        if (p == null) return;
        try { if (!p.HasExited) p.Kill(true); } catch { }
        p.Dispose(); p = null;
    }
    private void Watch()
    {
        while (!stop.Token.WaitHandle.WaitOne(250))
        {
            lock (gate)
            {
                if (cpu == null && gpu == null) continue;
                var now = Stopwatch.GetTimestamp();
                var error = Protection.Check(sensors.Current, now, cpu != null, gpuId, settings.CpuLimit, settings.GpuLimit);
                if (error != null) { StopAllLocked(error); continue; }
                if (Protection.Expired(started, now, settings.Minutes)) { StopAllLocked("Test je završen: isteklo je odabrano vrijeme."); continue; }
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastUiPulse), now) > TimeSpan.FromSeconds(5)) { StopAllLocked("Zaustavljeno: sučelje ne odgovara dulje od 5 sekundi."); continue; }
                foreach (var p in new[] { cpu, gpu }.Where(p => p != null))
                {
                    try
                    {
                        if (p!.HasExited) { StopAllLocked($"Test je prekinut: radni proces završio je kodom {p.ExitCode}. Detalji su u events.log."); break; }
                        p.StandardInput.WriteLine("pulse"); p.StandardInput.Flush();
                    }
                    catch { StopAllLocked("Test je prekinut: izgubljena veza s radnim procesom."); break; }
                }
            }
        }
    }
    public void Dispose() { StopAll("Program je zatvoren; svi testovi su zaustavljeni."); stop.Cancel(); }
}
