using System.Diagnostics;

namespace ThermoForge;

public sealed class StressController : IDisposable
{
    private readonly object gate = new();
    private readonly ISensorSource sensors;
    private readonly Settings settings;
    private readonly CancellationTokenSource stop = new();
    private Process? cpu, gpu;
    private string? gpuId;
    private bool gpuUsesCpuPackage;
    private long started, gpuStarted, lastStarted, lastStopped;
    private TestRun? run;
    private TestResult? lastResult;
    private long lastUiPulse = Stopwatch.GetTimestamp();
    public string Message { get; private set; } = "Spremno. Odaberi trajanje i pokreni CPU ili GPU test.";
    public bool CpuRunning { get { lock (gate) return cpu != null; } }
    public bool GpuRunning { get { lock (gate) return gpu != null; } }
    public bool GpuUsesCpuPackage { get { lock (gate) return gpu != null && gpuUsesCpuPackage; } }
    public bool Running => CpuRunning || GpuRunning;
    public TimeSpan Elapsed { get { lock (gate) return started == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(started); } }
    public void UiPulse() => Interlocked.Exchange(ref lastUiPulse, Stopwatch.GetTimestamp());
    internal TestTimeline Timeline { get { lock (gate) return new(started, lastStarted, lastStopped, lastResult); } }
    public StressController(ISensorSource sensors, Settings settings)
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
            var snapshot = sensors.Current;
            var usePackage = isCpu ? gpuUsesCpuPackage : GpuThermals.Resolve(snapshot, sensorId)?.UsesCpuPackage == true;
            var error = Protection.Check(snapshot, Stopwatch.GetTimestamp(), isCpu || cpu != null, sensorId ?? gpuId, settings.CpuLimit, settings.GpuLimit, usePackage);
            if (error != null) return error;
            if (Protection.Expired(started, Stopwatch.GetTimestamp(), settings.Minutes)) { StopAllLocked("Timer je istekao."); return "Timer je istekao. Pokreni novi test."; }
            var info = Program.WorkerStart(isCpu ? "--worker-cpu" : "--worker-gpu", isCpu ? null : adapter!.Index);
            var p = new Process { StartInfo = info };
            try
            {
                p.Start();
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Diagnostics.Log("Worker: " + e.Data); };
                p.BeginErrorReadLine();
                // Drain stdout continuously; no blocked worker on full pipe.
                p.OutputDataReceived += (_, e) => { if (e.Data != null) Diagnostics.Log("Worker: " + e.Data); };
                p.BeginOutputReadLine();
                long now = Stopwatch.GetTimestamp();
                if (started == 0) { started = now; run = new(now); }
                if (isCpu) { cpu = p; run!.CpuTested = true; }
                else
                {
                    gpu = p; gpuId = sensorId; gpuUsesCpuPackage = usePackage; gpuStarted = now;
                    run!.GpuName = adapter!.Name;
                    run.PackageMonitored |= usePackage;
                }
                Message = gpuUsesCpuPackage ? "GPU test je aktivan uz nadzor CPU paketa; GPU temperatura nije dostupna." : "Test je aktivan. Temperaturna zaštita je uključena.";
                Diagnostics.Log($"START {(isCpu ? "CPU" : adapter!.Name)}; timer={settings.Minutes}; CPU={settings.CpuLimit}; GPU={settings.GpuLimit}; GPU source={(gpuUsesCpuPackage ? "CPU package" : "GPU sensor")}");
                return null;
            }
            catch (Exception e) { p.Dispose(); return "Test se ne može pokrenuti: " + e.Message; }
        }
    }
    public void StopCpu() { lock (gate) { Kill(ref cpu); FinishIfIdle("CPU test je ručno zaustavljen."); Message = "CPU test je zaustavljen."; } }
    public void StopGpu()
    {
        lock (gate)
        {
            var snapshot = sensors.Current;
            long now = Stopwatch.GetTimestamp();
            Kill(ref gpu);
            Observe(snapshot, now);
            gpuId = null; gpuUsesCpuPackage = false;
            FinishIfIdle("GPU test je ručno zaustavljen.");
            Message = "GPU test je zaustavljen.";
        }
    }
    public void StopAll(string reason = "Test je ručno zaustavljen.") { lock (gate) StopAllLocked(reason); }
    private void StopAllLocked(string reason, Snapshot? snapshot = null, long? timestamp = null)
    {
        if (run != null) snapshot ??= sensors.Current;
        long now = timestamp ?? Stopwatch.GetTimestamp();
        Kill(ref cpu); Kill(ref gpu);
        if (run != null)
        {
            Observe(snapshot!, now);
            Finish(now, reason);
        }
        gpuId = null; gpuUsesCpuPackage = false;
        Message = reason; Diagnostics.Log(reason);
    }
    private void FinishIfIdle(string reason)
    {
        if (run == null || cpu != null || gpu != null) return;
        var snapshot = sensors.Current;
        long now = Stopwatch.GetTimestamp();
        Observe(snapshot, now);
        Finish(now, reason);
    }
    private void Finish(long now, string reason)
    {
        lastStarted = started;
        lastStopped = now;
        lastResult = run!.Finish(now, reason);
        run = null;
        started = 0;
    }
    private void Observe(Snapshot snapshot, long now) => run?.Observe(snapshot, now, gpuId, gpuStarted, gpuUsesCpuPackage);
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
                var snapshot = sensors.Current;
                var now = Stopwatch.GetTimestamp();
                var error = Protection.Check(snapshot, now, cpu != null, gpuId, settings.CpuLimit, settings.GpuLimit, gpuUsesCpuPackage);
                if (error != null) { StopAllLocked(error, snapshot, now); continue; }
                if (Protection.Expired(started, now, settings.Minutes)) { StopAllLocked("Test je završen: isteklo je odabrano vrijeme.", snapshot, now); continue; }
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastUiPulse), now) > TimeSpan.FromSeconds(5)) { StopAllLocked("Zaustavljeno: sučelje ne odgovara dulje od 5 sekundi.", snapshot, now); continue; }
                Observe(snapshot, now);
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
