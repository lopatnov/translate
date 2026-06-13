using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Reads free VRAM on an NVIDIA GPU via NVML — the driver library behind nvidia-smi
/// (nvml.dll on Windows, libnvidia-ml.so.1 on Linux). Exports are bound manually with
/// <see cref="NativeLibrary"/> so no NuGet wrapper package is needed, and the probe
/// degrades to "unknown" (<c>null</c>) on machines without an NVIDIA driver.
/// </summary>
[ExcludeFromCodeCoverage(Justification =
    "Requires an NVIDIA driver (NVML) — only the degraded no-driver path is reachable on CI runners.")]
public sealed class CudaMemoryProbe : IGpuMemoryProbe
{
    private const int NvmlSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, ref NvmlMemory memory);

    private sealed record NvmlBinding(
        NvmlDeviceGetHandleByIndexDelegate GetHandle,
        NvmlDeviceGetMemoryInfoDelegate GetMemoryInfo);

    // Bound once per process. nvmlInit_v2 is reference-counted inside the driver and NVML
    // stays loaded for the process lifetime, which is what a long-running service wants.
    private static readonly Lazy<NvmlBinding?> Binding =
        new(Bind, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly uint _deviceIndex;

    /// <param name="deviceIndex">
    /// NVML device index. The CUDA execution provider is appended with device 0, so the
    /// default matches what ONNX Runtime will actually use.
    /// </param>
    public CudaMemoryProbe(uint deviceIndex = 0) => _deviceIndex = deviceIndex;

    public long? GetFreeBytes()
    {
        try
        {
            var binding = Binding.Value;
            if (binding is null)
                return null;

            if (binding.GetHandle(_deviceIndex, out var device) != NvmlSuccess)
                return null;

            var memory = default(NvmlMemory);
            if (binding.GetMemoryInfo(device, ref memory) != NvmlSuccess)
                return null;

            return memory.Free > long.MaxValue ? long.MaxValue : (long)memory.Free;
        }
        catch
        {
            return null; // driver/marshalling failure — treat as unknown
        }
    }

    private static NvmlBinding? Bind()
    {
        if (!TryLoadNvml(out var lib))
            return null;

        try
        {
            if (!TryGetExport<NvmlInitDelegate>(lib, "nvmlInit_v2", out var init) &&
                !TryGetExport(lib, "nvmlInit", out init) || init is null)
                return null;

            if (!TryGetExport<NvmlDeviceGetHandleByIndexDelegate>(lib, "nvmlDeviceGetHandleByIndex_v2", out var getHandle) &&
                !TryGetExport(lib, "nvmlDeviceGetHandleByIndex", out getHandle) || getHandle is null)
                return null;

            if (!TryGetExport<NvmlDeviceGetMemoryInfoDelegate>(lib, "nvmlDeviceGetMemoryInfo", out var getMemoryInfo) ||
                getMemoryInfo is null)
                return null;

            if (init() != NvmlSuccess)
                return null;

            return new NvmlBinding(getHandle, getMemoryInfo);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryLoadNvml(out IntPtr lib)
    {
        // Absolute paths only — a bare "nvml.dll" would search the application
        // directory first, opening a DLL search-order hijacking vector.
        // System32 since driver R396; the NVSMI folder covers older drivers.
        string[] candidates = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                             "NVIDIA Corporation", "NVSMI", "nvml.dll"),
            ]
            : ["libnvidia-ml.so.1", "libnvidia-ml.so"];

        foreach (var name in candidates)
        {
            if (NativeLibrary.TryLoad(name, out lib))
                return true;
        }

        lib = IntPtr.Zero;
        return false;
    }

    private static bool TryGetExport<TDelegate>(IntPtr lib, string name, out TDelegate? export)
        where TDelegate : Delegate
    {
        if (NativeLibrary.TryGetExport(lib, name, out var address))
        {
            export = Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
            return true;
        }

        export = null;
        return false;
    }
}
