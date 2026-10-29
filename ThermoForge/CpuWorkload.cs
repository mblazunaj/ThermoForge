using System.Numerics;
using System.Runtime.InteropServices;

namespace ThermoForge;

public static class CpuWorkload
{
    private static float sink;
    public static Thread[] Start(CancellationToken token, int? maxThreads = null)
    {
        int count = checked((int)GetActiveProcessorCount(0xffff));
        if (maxThreads.HasValue) count = Math.Min(count, maxThreads.Value);
        var threads = new Thread[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            threads[i] = new Thread(() => Burn(index, token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"CPU stress {i}" };
            threads[i].Start();
        }
        return threads;
    }
    private static void Burn(int index, CancellationToken token)
    {
        ushort group = 0; int local = index;
        while (local >= GetActiveProcessorCount(group)) { local -= (int)GetActiveProcessorCount(group); group++; }
        var affinity = new GroupAffinity { Group = group, Mask = (nuint)1 << local };
        SetThreadGroupAffinity(GetCurrentThread(), ref affinity, out _);
        var a = new Vector<float>(0.1234f + index * .00001f);
        var b = new Vector<float>(0.8765f);
        var c = new Vector<float>(.99991f);
        var d = new Vector<float>(.00003f);
        while (!token.IsCancellationRequested)
        {
            for (int j = 0; j < 8192; j++) { a = a * c + d; b = b * c + a * d; a = Vector.SquareRoot(Vector.Abs(a * b) + d); }
            Volatile.Write(ref sink, a[0] + b[0]);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct GroupAffinity { public nuint Mask; public ushort Group, Reserved0, Reserved1, Reserved2; }
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetThreadGroupAffinity(nint thread, ref GroupAffinity affinity, out GroupAffinity previous);
}
