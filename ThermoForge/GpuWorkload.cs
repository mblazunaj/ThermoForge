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
    public override string ToString() => Name;
}
public sealed class GpuWorkload : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly ID3D11Buffer buffer;
    private readonly ID3D11UnorderedAccessView view;
    private readonly ID3D11ComputeShader shader;
    private readonly ID3D11Query query;
    public static GpuAdapter[] Adapters()
    {
        var list = new List<GpuAdapter>();
        using var f = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; f.EnumAdapters1(i, out var a).Success; i++)
        {
            using (a) { var d = a.Description1; if ((d.Flags & AdapterFlags.Software) == 0) list.Add(new(i, d.Description.TrimEnd('\0'), (ulong)d.DedicatedVideoMemory)); }
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
        query = device.CreateQuery(new QueryDescription(QueryType.Event));
        context.CSSetShader(shader);
        context.CSSetUnorderedAccessView(0, view);
    }
    public void Run(CancellationToken token, int? maxBatches = null)
    {
        int batches = 0;
        // A bounded dispatch and at most four queued batches keep stop latency short and avoid TDR-sized kernels.
        while (!token.IsCancellationRequested && (!maxBatches.HasValue || batches < maxBatches))
        {
            for (int i = 0; i < 4 && !token.IsCancellationRequested; i++) context.Dispatch(512, 1, 1);
            context.End(query); context.Flush();
            var watch = Stopwatch.StartNew();
            while (context.GetData(query, nint.Zero, 0, AsyncGetDataFlags.None).Code == 1)
            {
                if (token.IsCancellationRequested) return;
                if (watch.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("GPU ne odgovara. Test je zaustavljen.");
                Thread.Sleep(1);
            }
            device.DeviceRemovedReason.CheckError(); batches++;
        }
    }
    public void Dispose() { context.ClearState(); query.Dispose(); shader.Dispose(); view.Dispose(); buffer.Dispose(); context.Dispose(); device.Dispose(); }
    private static unsafe nint BlobPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nint>)(*(nint**)blob)[3])(blob);
    private static unsafe nuint BlobSize(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nuint>)(*(nint**)blob)[4])(blob);
    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3DCompile(byte[] data, nuint size, string? sourceName, nint defines, nint include, [MarshalAs(UnmanagedType.LPStr)] string entry, [MarshalAs(UnmanagedType.LPStr)] string target, uint flags, uint effectFlags, out nint code, out nint errors);
}
