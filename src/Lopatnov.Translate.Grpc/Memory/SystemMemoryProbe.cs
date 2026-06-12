using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Estimates how much system RAM the process can still claim before the host (or its
/// cgroup) starts swapping or OOM-killing. Combines OS-level available physical memory
/// (MEMORYSTATUSEX on Windows, /proc/meminfo on Linux) with the GC's container-aware
/// total limit, and reports the most pessimistic of the two.
/// </summary>
public static class SystemMemoryProbe
{
    /// <summary>
    /// Available bytes, or <c>null</c> when no source could be queried.
    /// </summary>
    public static long? GetAvailableBytes()
    {
        long? osAvailable = GetOsAvailableBytes();
        long? gcRemaining = GetGcBudgetRemainingBytes();

        if (osAvailable is null) return gcRemaining;
        if (gcRemaining is null) return osAvailable;
        return Math.Min(osAvailable.Value, gcRemaining.Value);
    }

    /// <summary>
    /// What remains of the GC's container-aware memory limit after this process's
    /// current working set. On bare metal the limit equals physical RAM and the OS
    /// value below is tighter; inside a cgroup-limited container this is the value
    /// that actually matters.
    /// </summary>
    private static long? GetGcBudgetRemainingBytes()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes <= 0)
                return null;
            return Math.Max(0, info.TotalAvailableMemoryBytes - Environment.WorkingSet);
        }
        catch
        {
            return null;
        }
    }

    private static long? GetOsAvailableBytes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return GetWindowsAvailableBytes();
            if (OperatingSystem.IsLinux())
                return GetLinuxAvailableBytes();
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [ExcludeFromCodeCoverage(Justification = "Windows-only P/Invoke path; CI coverage runs on Linux.")]
    private static long? GetWindowsAvailableBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            return null;
        return status.ullAvailPhys > long.MaxValue ? long.MaxValue : (long)status.ullAvailPhys;
    }

    // ── Linux ────────────────────────────────────────────────────────────────

    private static long? GetLinuxAvailableBytes()
    {
        // MemAvailable (kernel 3.14+) accounts for reclaimable page cache;
        // MemFree is the conservative fallback on older kernels.
        long? memFree = null;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                return ParseMemInfoKb(line);
            if (line.StartsWith("MemFree:", StringComparison.Ordinal))
                memFree = ParseMemInfoKb(line);
        }

        return memFree;
    }

    private static long? ParseMemInfoKb(string line)
    {
        // Format: "MemAvailable:   16319168 kB"
        var span = line.AsSpan(line.IndexOf(':') + 1).Trim();
        var end = span.IndexOf(' ');
        if (end > 0)
            span = span[..end];
        return long.TryParse(span, out var kb) ? kb * 1024 : null;
    }
}
