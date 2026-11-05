using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using SharpGen.Runtime;

namespace ThermoForge;

public record GpuAdapter(uint Index, string Name, ulong Memory)
{
    public ulong SharedMemory { get; init; }
    public override string ToString() => Name;
}
public sealed class GpuWorkload : IDisposable
{
    private const int MaxPendingBatches = 3;
    private const int MaxDispatchesPerBatch = 32;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11Buffer buffer;
    private readonly ID3D11UnorderedAccessView view;
    private readonly ID3D11ComputeShader shader;
    private readonly ID3D11Query[] completions;
    public static GpuAdapter[] Adapters()
    {
        var list = new List<GpuAdapter>();
        using var f = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; f.EnumAdapters1(i, out var a).Success; i++)
        {
            using (a)
            {
                var d = a.Description1;
                if ((d.Flags & AdapterFlags.Software) == 0)
                    list.Add(new(i, d.Description.TrimEnd('\0'), (ulong)d.DedicatedVideoMemory) { SharedMemory = (ulong)d.SharedSystemMemory });
            }
        }
        return list.OrderByDescending(a => a.Memory).ToArray();
    }
    public unsafe GpuWorkload(uint adapterIndex)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(adapterIndex, out var adapter).CheckError();
        using (adapter)
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, [FeatureLevel.Level_11_0], out device, out _, out context).CheckError();
        buffer = device.CreateBuffer(65536 * 16, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16);
        view = device.CreateUnorderedAccessView(buffer);
        var source = Encoding.UTF8.GetBytes("""
            RWStructuredBuffer<float4> outputData : register(u0);
            [numthreads(128,1,1)]
            void main(uint3 id : SV_DispatchThreadID) {
                float4 a = frac(float4(id.x + 1, id.x + 3, id.x + 7, id.x + 11) * 0.000123f) + 0.1f;
                float4 b = a.yzwx + 0.07f;
                [loop] for (uint i=0; i<128; i++) {
                    a = frac(mad(a, b.yzwx + 1.0003f, 0.031f));
                    b = frac(mad(b, a.wxyz + 1.0007f, 0.071f));
                    a = sqrt(abs(a * b + 0.0001f));
                }
                outputData[id.x] = a + b;
            }
            """);
        nint code = 0, errors = 0;
        try
        {
            int hr = D3DCompile(source, (nuint)source.Length, null, 0, 0, "main", "cs_5_0", 1 << 15, 0, out code, out errors);
            if (hr < 0) throw new InvalidOperationException("GPU shader: " + (errors == 0 ? $"0x{hr:X}" : Marshal.PtrToStringAnsi(BlobPointer(errors))));
            shader = device.CreateComputeShader((void*)BlobPointer(code), new PointerUSize(BlobSize(code)));
        }
        finally { if (code != 0) Marshal.Release(code); if (errors != 0) Marshal.Release(errors); }
        completions = new ID3D11Query[MaxPendingBatches];
        for (int i = 0; i < completions.Length; i++)
            completions[i] = device.CreateQuery(new QueryDescription(QueryType.Event));
        context.CSSetShader(shader);
        context.CSSetUnorderedAccessView(0, view);
    }
    public void Run(CancellationToken token, int? maxBatches = null)
    {
        long remaining = maxBatches.HasValue ? maxBatches.Value : long.MaxValue;
        int first = 0, pending = 0, dispatches = 4;
        var submitted = new (long Timestamp, int Dispatches)[MaxPendingBatches];
        // Keep a few batches ready while waiting for the oldest one. The dispatch dimensions stay fixed.
        while (!token.IsCancellationRequested && (remaining > 0 || pending > 0))
        {
            while (pending < MaxPendingBatches && remaining > 0)
            {
                int slot = (first + pending) % MaxPendingBatches;
                submitted[slot] = (Stopwatch.GetTimestamp(), dispatches);
                for (int i = 0; i < dispatches; i++)
                {
                    if (token.IsCancellationRequested) return;
                    context.Dispatch(512, 1, 1);
                }
                if (token.IsCancellationRequested) return;
                context.End(completions[slot]);
                context.Flush();
                pending++;
                remaining--;
            }
            var batch = submitted[first];
            Result status;
            while ((status = context.GetData(completions[first], nint.Zero, 0, AsyncGetDataFlags.DoNotFlush)).Code == 1)
            {
                if (token.IsCancellationRequested) return;
                if (Stopwatch.GetElapsedTime(batch.Timestamp) > TimeSpan.FromSeconds(3))
                    throw new TimeoutException("GPU ne odgovara. Test je zaustavljen.");
                // A timed wait can outlast a short GPU batch. Yield without waiting for the timer tick.
                Thread.SpinWait(64);
                Thread.Yield();
            }
            status.CheckError();
            device.DeviceRemovedReason.CheckError();
            // Older batches can have a previous size. Only samples at the current size tune the queue.
            if (batch.Dispatches == dispatches)
                dispatches = NextDispatchCount(dispatches, Stopwatch.GetElapsedTime(batch.Timestamp));
            first = (first + 1) % MaxPendingBatches;
            pending--;
        }
    }

    internal static int NextDispatchCount(int current, TimeSpan latency)
    {
        if (latency > TimeSpan.FromMilliseconds(85)) return Math.Max(1, current / 2);
        if (latency < TimeSpan.FromMilliseconds(35)) return Math.Min(MaxDispatchesPerBatch, current * 2);
        return current;
    }

    public void Dispose()
    {
        context.ClearState();
        foreach (var completion in completions) completion.Dispose();
        shader.Dispose(); view.Dispose(); buffer.Dispose(); context.Dispose(); device.Dispose();
    }
    private static unsafe nint BlobPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nint>)(*(nint**)blob)[3])(blob);
    private static unsafe nuint BlobSize(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nuint>)(*(nint**)blob)[4])(blob);
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] data, nuint size, string? sourceName, nint defines, nint include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target, uint flags, uint effectFlags, out nint code, out nint errors);
}
