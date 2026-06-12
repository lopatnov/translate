using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Reads the local video-memory budget of the first hardware DXGI adapter via
/// <c>IDXGIAdapter3::QueryVideoMemoryInfo</c> — the same arbitration data WDDM uses
/// when deciding whether to demote DirectML allocations into slow shared system memory.
/// Implemented with raw COM vtable calls (no COM-wrapper NuGet package required).
/// <para>
/// The first hardware adapter from <c>EnumAdapters1</c> is a heuristic match for the
/// adapter the DirectML execution provider picks for device 0; on multi-GPU machines
/// the estimate may be taken from a different adapter than ORT ends up using.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage(Justification =
    "Requires Windows with DXGI and a hardware GPU — only the degraded path is reachable on CI runners.")]
public sealed class DirectMlMemoryProbe : IGpuMemoryProbe
{
    // vtable slots: IUnknown(0-2) + IDXGIObject(3-6) + IDXGIFactory(7-11) + IDXGIFactory1::EnumAdapters1(12)
    private const int SlotEnumAdapters1 = 12;

    // IUnknown(0-2) + IDXGIObject(3-6) + IDXGIAdapter(7-9) + IDXGIAdapter1::GetDesc1(10)
    private const int SlotGetDesc1 = 10;

    // ... + IDXGIAdapter2::GetDesc2(11) + IDXGIAdapter3::RegisterHardwareContentProtectionTeardownStatusEvent(12),
    // UnregisterHardwareContentProtectionTeardownStatus(13), QueryVideoMemoryInfo(14)
    private const int SlotQueryVideoMemoryInfo = 14;

    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 0x2;
    private const int DxgiMemorySegmentGroupLocal = 0;

    private static readonly Guid IidIDxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IidIDxgiAdapter3 = new("645967a4-1392-4310-a798-8053ce3e93fd");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiQueryVideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DxgiAdapterDesc1 desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryVideoMemoryInfoDelegate(
        IntPtr adapter, uint nodeIndex, int memorySegmentGroup, out DxgiQueryVideoMemoryInfo info);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    public long? GetFreeBytes()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            return QueryFirstHardwareAdapterFreeBudget();
        }
        catch
        {
            return null; // missing dxgi.dll, remote session without DXGI, etc. — unknown
        }
    }

    private static long? QueryFirstHardwareAdapterFreeBudget()
    {
        var iidFactory = IidIDxgiFactory1;
        if (CreateDXGIFactory1(ref iidFactory, out var factory) < 0 || factory == IntPtr.Zero)
            return null;

        try
        {
            var enumAdapters1 = GetVtableMethod<EnumAdapters1Delegate>(factory, SlotEnumAdapters1);

            for (uint index = 0; ; index++)
            {
                int hr = enumAdapters1(factory, index, out var adapter);
                if (hr == DxgiErrorNotFound)
                    return null; // ran out of adapters without finding a hardware one
                if (hr < 0 || adapter == IntPtr.Zero)
                    return null;

                try
                {
                    var getDesc1 = GetVtableMethod<GetDesc1Delegate>(adapter, SlotGetDesc1);
                    if (getDesc1(adapter, out var desc) < 0 ||
                        (desc.Flags & DxgiAdapterFlagSoftware) != 0)
                        continue; // skip "Microsoft Basic Render Driver" and friends

                    if (Marshal.QueryInterface(adapter, in IidIDxgiAdapter3, out var adapter3) < 0 ||
                        adapter3 == IntPtr.Zero)
                        return null; // pre-Windows-10 stack — budget API unavailable

                    try
                    {
                        var queryVideoMemoryInfo =
                            GetVtableMethod<QueryVideoMemoryInfoDelegate>(adapter3, SlotQueryVideoMemoryInfo);
                        if (queryVideoMemoryInfo(
                                adapter3, 0, DxgiMemorySegmentGroupLocal, out var info) < 0)
                            return null;

                        // Budget is how much this process may use before WDDM starts demoting;
                        // what is left of it is the headroom a new model can claim.
                        ulong free = info.Budget <= info.CurrentUsage ? 0 : info.Budget - info.CurrentUsage;
                        return free > long.MaxValue ? long.MaxValue : (long)free;
                    }
                    finally
                    {
                        Marshal.Release(adapter3);
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    private static TDelegate GetVtableMethod<TDelegate>(IntPtr comObject, int slot)
        where TDelegate : Delegate
    {
        var vtable = Marshal.ReadIntPtr(comObject);
        var method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }
}
