using System.Diagnostics;
using System.Text.Json;

namespace ThermoForge;

internal static class Program
{
    internal static ProcessStartInfo WorkerStart(string mode, uint? adapter = null)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        info.ArgumentList.Add(mode);
        if (adapter.HasValue) info.ArgumentList.Add(adapter.Value.ToString());
        return info;
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.FirstOrDefault()?.StartsWith("--worker-") == true) return Worker(args);
        if (args.FirstOrDefault() == "--self-test") return SelfTest.Run(args.ElementAtOrDefault(1));
        if (args.FirstOrDefault() == "--policy-test") return SelfTest.Run(args.ElementAtOrDefault(1), false);
        if (args.FirstOrDefault() == "--wait-for" && int.TryParse(args.ElementAtOrDefault(1), out int oldPid))
        { try { using var old = Process.GetProcessById(oldPid); old.WaitForExit(10000); } catch (ArgumentException) { } }
        ApplicationConfiguration.Initialize();
        #pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
        #pragma warning restore WFO5001
        if (args.FirstOrDefault() == "--render-ui")
        {
            using var preview = new MainForm(); preview.RenderForVerification(args[1], int.Parse(args[2]), int.Parse(args[3])); return 0;
        }
        using var mutex = new Mutex(true, @"Local\ThermoForge.UI", out bool created);
        if (!created) { MessageBox.Show("ThermoForge je već pokrenut.", "ThermoForge"); return 0; }
        Application.ThreadException += (_, e) => { Diagnostics.Log(e.Exception.ToString()); MessageBox.Show("Greška sučelja. Testovi su zaustavljeni zbog sigurnosti.\n" + e.Exception.Message, "ThermoForge"); Application.Exit(); };
        try { using var form = new MainForm(); Application.Run(form); }
        catch (Exception e) { Diagnostics.Log(e.ToString()); MessageBox.Show("Program se nije mogao pokrenuti:\n" + e.Message, "ThermoForge"); return 1; }
        return 0;
    }
    private static int Worker(string[] args)
    {
        using var stop = new CancellationTokenSource();
        long pulse = Stopwatch.GetTimestamp();
        new Thread(() =>
        {
            try { while (Console.ReadLine() is string line) { if (line == "stop") break; Interlocked.Exchange(ref pulse, Stopwatch.GetTimestamp()); } } catch { }
            stop.Cancel();
        }) { IsBackground = true }.Start();
        new Thread(() =>
        {
            while (!stop.Token.WaitHandle.WaitOne(250))
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref pulse)) > TimeSpan.FromSeconds(4)) { stop.Cancel(); break; }
            // Hard exit also releases a driver call that stopped responding.
            Thread.Sleep(500); Environment.Exit(0);
        }) { IsBackground = true, Priority = ThreadPriority.AboveNormal }.Start();
        try
        {
            if (args[0].StartsWith("--worker-cpu", StringComparison.Ordinal))
            {
                var threads = CpuWorkload.Start(stop.Token, args[0] == "--worker-cpu-probe" ? 2 : null);
                Console.WriteLine("READY CPU " + threads.Length);
                stop.Token.WaitHandle.WaitOne();
                foreach (var t in threads) t.Join(1000);
            }
            else
            {
                using var gpu = new GpuWorkload(uint.Parse(args[1]));
                Console.WriteLine("READY GPU"); gpu.Run(stop.Token);
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 2; }
    }
}

public static class SelfTest
{
    private sealed class FixedSensors(Snapshot snapshot) : ISensorSource
    {
        public Snapshot Current => snapshot with { Timestamp = Stopwatch.GetTimestamp() };
        public void Dispose() { }
    }
    public static int Run(string? path, bool hardware = true)
    {
        var log = new List<string>();
        try
        {
            ApplicationConfiguration.Initialize();
            #pragma warning disable WFO5001
            Application.SetColorMode(SystemColorMode.Dark);
            #pragma warning restore WFO5001
            void Check(bool condition, string name) { if (!condition) throw new Exception(name); log.Add("PASS " + name); }
            var now = Stopwatch.GetTimestamp();
            Snapshot Sample(float? cpu = 50, float? gpu = 40) => new(now, [new("cpu", "Test CPU", true, [new("c0", "CPU Core #1", "Temperature", cpu, cpu)]), new("gpu", "Test GPU", false, [new("g0", "GPU Core", "Temperature", gpu, gpu)])], null);
            Check(Protection.Check(Sample(), now, true, "gpu", 95, 85) == null, "normal temperatures");
            Check(Protection.Check(Sample(95), now, true, null, 95, 85) != null, "CPU threshold inclusive");
            Check(Protection.Check(Sample(50, 85), now, false, "gpu", 95, 85) != null, "GPU threshold inclusive");
            var hotCore = new Snapshot(now, [new("cpu", "CPU", true, [new("pkg", "CPU Package", "Temperature", 80, 80), new("core", "P-Core #1", "Temperature", 96, 96)])], null);
            Check(Protection.Check(hotCore, now, true, null, 95, 85) != null, "hot core overrides cooler CPU package");
            Check(Protection.Check(Sample(null), now, true, null, 95, 85) != null, "missing CPU blocks start");
            Check(Protection.Check(Sample(50, null), now, false, "gpu", 95, 85) != null, "missing GPU blocks start");
            Check(Protection.Check(Sample(), now + Stopwatch.Frequency * 6, true, null, 95, 85) != null, "stale sensors fail closed");
            Check(Protection.Check(Sample(96), now, false, "gpu", 95, 85) != null, "other hot device stops both");
            foreach (int m in new[] { 1, 5, 10, 15, 30 })
            {
                Check(!Protection.Expired(now, now + Stopwatch.Frequency * (m * 60L - 1), m), $"timer {m} before deadline");
                Check(Protection.Expired(now, now + Stopwatch.Frequency * m * 60L, m), $"timer {m} at deadline");
            }
            Check(!Protection.Expired(now, now + Stopwatch.Frequency * 86400L, 0), "unlimited timer");
            Check(Protection.Check(Sample() with { Error = "failure" }, now, true, null, 95, 85) != null, "sensor read failure stops test");
            Check(Protection.Check(Sample(), now, false, "missing-gpu", 95, 85) != null, "removed GPU stops test");
            Check(UpdateService.TrustedAssetUrl("https://github.com/mblazunaj/ThermoForge/releases/download/v1.0.1/ThermoForge-Setup-1.0.1.exe"), "official update URL");
            Check(!UpdateService.TrustedAssetUrl("https://github.com/other/ThermoForge/releases/download/v1/x.exe"), "other repository rejected");
            Check(!UpdateService.TrustedAssetUrl("http://github.com/mblazunaj/ThermoForge/releases/download/v1/x.exe"), "non TLS update rejected");
            Check(!UpdateService.TrustedAssetUrl("https://github.com.evil.example/mblazunaj/ThermoForge/releases/download/v1/x.exe"), "spoofed update host rejected");
            Check(!UpdateService.TrustedAssetUrl("https://github.com/mblazunaj/ThermoForge/other/x.exe"), "non release path rejected");
            var ambiguous = new Snapshot(now, [new("gpu1", "Same GPU", false, []), new("gpu2", "Same GPU", false, [])], null);
            Check(MainForm.MatchGpu(ambiguous, new(0, "Same GPU", 0)) == null, "ambiguous GPU sensor rejected");
            const string intelId = "/gpu-intel-integrated/test";
            Snapshot Intel(float? package = 50, float? ownGpu = null) => new(now, [new("cpu", "Intel CPU", true, [new("pkg", "CPU Package", "Temperature", package, package), new("core", "Core Max", "Temperature", 60, 60)]), new(intelId, "Intel integrated GPU", false, [new("gpu", "GPU Core", "Temperature", ownGpu, ownGpu)])], null);
            Check(GpuThermals.Resolve(Intel(), intelId) is { UsesCpuPackage: true, Temperature: 50 }, "integrated Intel uses actual package value");
            Check(GpuThermals.Resolve(Intel(50, 70), intelId) is { UsesCpuPackage: false, Temperature: 70 }, "own GPU sensor preferred over package");
            Check(Protection.Check(Intel(), now, false, intelId, 95, 85) != null, "package fallback must be explicitly selected");
            Check(Protection.Check(Intel(), now, false, intelId, 95, 85, true) == null, "selected package monitoring permits integrated GPU test");
            Check(Protection.Check(Intel(85), now, false, intelId, 95, 85, true) != null, "GPU threshold applies to monitored package");
            Check(Protection.Check(Intel(null), now, false, intelId, 95, 85, true) != null, "missing package stops GPU despite available core readings");
            Check(Protection.Check(Intel(), now + Stopwatch.Frequency * 6, false, intelId, 95, 85, true) != null, "stale package readings stop GPU");
            Check(Protection.Check(Intel(50, 90), now, false, intelId, 95, 85, true) != null, "hot own GPU sensor still stops package-monitored test");
            var discrete = Intel() with { Devices = [Intel().Devices[0], Intel().Devices[1] with { Id = "/gpu-nvidia/0" }] };
            Check(GpuThermals.Resolve(discrete, "/gpu-nvidia/0") == null, "discrete GPU cannot use package fallback");
            Check(Protection.Check(discrete, now, false, "/gpu-nvidia/0", 95, 85, true) != null, "forced package mode cannot bypass discrete GPU protection");
            var multiCpu = Intel() with { Devices = [.. Intel().Devices, Intel().Devices[0] with { Id = "cpu2" }] };
            Check(GpuThermals.Resolve(multiCpu, intelId) == null, "ambiguous CPU package blocks fallback");
            Check(GpuThermals.Resolve(Intel(float.NaN), intelId) == null, "invalid package value blocks fallback");
            var compute = new DeviceReading("gpu", "GPU", false, [new("3d", "D3D 3D", "Load", 2, 2), new("compute", "D3D Compute", "Load", 99, 99)]);
            Check(compute.Load == 99, "compute workload utilization is displayed");
            using (var ui = new MainForm(new FixedSensors(Intel()), [new(0, "Intel integrated GPU", 0)]))
            {
                var state = ui.GpuUiState();
                Check(state.Enabled, "actual GPU button enabled with package monitoring");
                Check(state.Action.Contains("CPU paketa") && state.Monitor.Contains("CPU paket"), "actual UI explicitly names package monitoring");
                Check(state.Reading == "—", "CPU package is never presented as a GPU temperature");
                Check(ui.Icon != null, "application icon resource loads");
                if (hardware && path != null) ui.RenderForVerification(Path.ChangeExtension(path, ".ui.png"), 840, 720, "TESTNI SENZORI — provjera sučelja");
            }
            using (var ui = new MainForm(new FixedSensors(Intel(null)), [new(0, "Intel integrated GPU", 0)]))
                Check(!ui.GpuUiState().Enabled, "actual GPU button disabled if package source is lost");
            using (var ui = new MainForm(new FixedSensors(discrete), [new(0, "Intel integrated GPU", 0)]))
                Check(!ui.GpuUiState().Enabled, "actual GPU button blocks unmonitored discrete GPU");
            if (!hardware) { log.Add("RESULT PASS"); if (path != null) File.WriteAllLines(path, log); return 0; }
            using (var cancel = new CancellationTokenSource())
            {
                var threads = CpuWorkload.Start(cancel.Token, 2); Thread.Sleep(300); cancel.Cancel();
                Check(threads.All(t => t.Join(2000)), "CPU SIMD workload cancellation (2 threads, 300 ms)");
            }
            foreach (bool closePipe in new[] { true, false })
            {
                using var worker = new Process { StartInfo = Program.WorkerStart("--worker-cpu-probe") };
                worker.Start();
                try
                {
                    var ready = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    Check(ready == "READY CPU 2", "worker started with 2 threads");
                    if (closePipe) worker.StandardInput.Close();
                    Check(worker.WaitForExit(6500), closePipe ? "worker exits on parent pipe closure" : "worker exits on missing heartbeat");
                }
                finally { if (!worker.HasExited) worker.Kill(true); }
            }
            var adapters = GpuWorkload.Adapters();
            log.Add("ADAPTERS " + JsonSerializer.Serialize(adapters));
            foreach (var adapter in adapters)
            {
                using var gpu = new GpuWorkload(adapter.Index);
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3)); gpu.Run(cancel.Token, 2);
                Check(!cancel.IsCancellationRequested, "GPU bounded dispatch " + adapter.Name);
            }
            using (var sensors = new SensorService())
            {
                var wait = Stopwatch.StartNew();
                while (sensors.Current.Timestamp == 0 && wait.Elapsed < TimeSpan.FromSeconds(20)) Thread.Sleep(100);
                log.Add("SENSORS " + JsonSerializer.Serialize(sensors.Current));
            }
            log.Add("RESULT PASS");
            if (path != null) File.WriteAllLines(path, log);
            return 0;
        }
        catch (Exception e) { log.Add("RESULT FAIL " + e); if (path != null) File.WriteAllLines(path, log); return 1; }
    }
}
