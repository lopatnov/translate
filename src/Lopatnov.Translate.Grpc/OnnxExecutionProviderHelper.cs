using System.Globalization;
using Lopatnov.Translate.Grpc.Memory;
using OnnxRuntimeException = Microsoft.ML.OnnxRuntime.OnnxRuntimeException;
using OrtCUDAProviderOptions = Microsoft.ML.OnnxRuntime.OrtCUDAProviderOptions;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Builds <see cref="SessionOptions"/> for an ONNX execution provider specified by name.
/// <para>
/// When the caller supplies an <see cref="OnnxMemoryPolicy"/> with a known model footprint,
/// selection becomes memory-aware:
/// <list type="bullet">
///   <item><c>auto</c> — a GPU provider whose free device memory is below the footprint is
///         skipped with a warning and selection falls through to CPU, instead of CUDA
///         OOM-ing at first inference or WDDM silently demoting DirectML allocations to
///         slow shared system memory;</item>
///   <item>explicit <c>cuda</c>/<c>directml</c> — fails fast with
///         <see cref="ModelMemoryBudgetException"/> when the footprint cannot fit;</item>
///   <item>free memory unknown (no driver API, exotic hardware) — checks are skipped and
///         behaviour matches the legacy overload.</item>
/// </list>
/// </para>
/// </summary>
public static class OnnxExecutionProviderHelper
{
    private const string DirectMlProvider = "directml";
    private const string CudaProvider     = "cuda";

    private static readonly IGpuMemoryProbe DefaultCudaProbe     = new CudaMemoryProbe();
    private static readonly IGpuMemoryProbe DefaultDirectMlProbe = new DirectMlMemoryProbe();

    /// <summary>
    /// Creates a <see cref="SessionOptions"/> for the requested execution provider with no
    /// memory checks (footprint unknown).
    /// <list type="bullet">
    ///   <item><c>""</c> / <c>"auto"</c> (default) — auto-detects the best available provider.</item>
    ///   <item><c>"cpu"</c> — forces CPU, even when a GPU is available.</item>
    ///   <item><c>"directml"</c> — DirectML (Windows, any DX12 GPU). Warns and falls back to CPU if unavailable.</item>
    ///   <item><c>"cuda"</c> — CUDA (NVIDIA). Warns and falls back to CPU if unavailable.</item>
    ///   <item>Any other value — throws <see cref="ArgumentException"/>.</item>
    /// </list>
    /// </summary>
    public static SessionOptions BuildSessionOptions(string? provider, ILogger? logger = null)
        => BuildSessionOptions(provider, OnnxMemoryPolicy.None, logger);

    /// <summary>
    /// Memory-aware variant: skips (auto) or rejects (explicit GPU) providers whose free
    /// device memory is known to be below <see cref="OnnxMemoryPolicy.RequiredBytes"/>,
    /// and caps the CUDA arena when <see cref="OnnxMemoryPolicy.CudaGpuMemLimitBytes"/> is set.
    /// </summary>
    /// <exception cref="ModelMemoryBudgetException">
    /// An explicitly requested GPU provider does not have enough free device memory.
    /// </exception>
    public static SessionOptions BuildSessionOptions(
        string? provider, OnnxMemoryPolicy memory, ILogger? logger = null)
        => BuildSessionOptions(provider, memory, DefaultCudaProbe, DefaultDirectMlProbe, logger);

    // Probe-injecting overload for tests (InternalsVisibleTo: Lopatnov.Translate.Grpc.Tests).
    internal static SessionOptions BuildSessionOptions(
        string? provider,
        OnnxMemoryPolicy memory,
        IGpuMemoryProbe cudaProbe,
        IGpuMemoryProbe directMlProbe,
        ILogger? logger)
    {
        return (provider ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "auto"    => BuildAuto(memory, cudaProbe, directMlProbe, logger),
            "cpu"           => BuildCpu(logger),
            DirectMlProvider => BuildExplicit(DirectMlProvider, "DirectML",
                                    o => o.AppendExecutionProvider_DML(0),
                                    memory, directMlProbe, logger),
            CudaProvider    => BuildExplicit(CudaProvider, "CUDA",
                                    o => AppendCuda(o, memory.CudaGpuMemLimitBytes, logger),
                                    memory, cudaProbe, logger),
            var unknown   => throw new ArgumentException(
                                $"Unknown ONNX execution provider '{unknown}'. " +
                                "Valid values: auto, cpu, directml, cuda.",
                                nameof(provider)),
        };
    }

    // ── Auto ────────────────────────────────────────────────────────────────

    private static SessionOptions BuildAuto(
        OnnxMemoryPolicy memory,
        IGpuMemoryProbe cudaProbe,
        IGpuMemoryProbe directMlProbe,
        ILogger? logger)
    {
        // Auto-detection priority per platform. On Windows, DirectML is tried first
        // (works on any DX12 GPU without a CUDA toolkit install); CUDA is a fallback
        // for NVIDIA machines where DirectML is unavailable or short on memory.
        (string Display, Action<SessionOptions> Append, IGpuMemoryProbe Probe)[] candidates =
            OperatingSystem.IsWindows()
                ? [("DirectML", o => o.AppendExecutionProvider_DML(0), directMlProbe),
                   ("CUDA",     o => AppendCuda(o, memory.CudaGpuMemLimitBytes, logger), cudaProbe)]
                : [("CUDA",     o => AppendCuda(o, memory.CudaGpuMemLimitBytes, logger), cudaProbe)];

        foreach (var (display, append, probe) in candidates)
        {
            if (!HasEnoughDeviceMemory(display, memory, probe, failFast: false, logger))
                continue;

            var opts = new SessionOptions();
            try
            {
                append(opts);
#pragma warning disable CA1873 // display is a cheap local string — no guard needed
                logger?.LogInformation(
                    "ONNX execution provider auto-selected: {Provider} — inference will run on GPU/NPU",
                    display);
#pragma warning restore CA1873
                return opts;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
            {
                opts.Dispose();
#pragma warning disable CA1873
                logger?.LogDebug(ex,
                    "Auto-detection: {Provider} not available — trying next",
                    display);
#pragma warning restore CA1873
            }
        }

        logger?.LogInformation(
            "ONNX execution provider auto-selected: CPU (no usable GPU/NPU provider on this machine)");
        return new SessionOptions();
    }

    // ── Explicit providers ───────────────────────────────────────────────────

    private static SessionOptions BuildCpu(ILogger? logger)
    {
        logger?.LogDebug("ONNX execution provider: CPU (forced)");
        return new SessionOptions();
    }

    private static SessionOptions BuildExplicit(
        string key, string display,
        Action<SessionOptions> append,
        OnnxMemoryPolicy memory,
        IGpuMemoryProbe probe,
        ILogger? logger)
    {
        // An explicitly requested provider fails fast on a known memory shortfall —
        // the operator opted out of silent fallbacks (throws ModelMemoryBudgetException).
        HasEnoughDeviceMemory(display, memory, probe, failFast: true, logger);

        var opts = new SessionOptions();
        try
        {
            append(opts);
#pragma warning disable CA1873 // display is a cheap local string — no guard needed
            logger?.LogInformation("ONNX execution provider: {Provider}", display);
#pragma warning restore CA1873
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
        {
            logger?.LogWarning(ex,
                "Execution provider '{Provider}' is not available — falling back to CPU. " +
                "Ensure Microsoft.ML.OnnxRuntime.{Suffix} is installed.",
                key,
                key == DirectMlProvider ? "DirectML" : "Gpu");
        }
        return opts;
    }

    // ── Memory policy ─────────────────────────────────────────────────────────

    /// <summary>
    /// True when the model is known (or assumed) to fit into the provider's free device
    /// memory. Unknown footprint or unknown free memory ⇒ optimistic true.
    /// With <paramref name="failFast"/> a shortfall throws instead of returning false.
    /// </summary>
    private static bool HasEnoughDeviceMemory(
        string display,
        OnnxMemoryPolicy memory,
        IGpuMemoryProbe probe,
        bool failFast,
        ILogger? logger)
    {
        if (memory.RequiredBytes <= 0)
            return true;

        long? free = SafeFreeBytes(probe);
        if (free is null)
        {
#pragma warning disable CA1873 // display is a cheap local string — no guard needed
            logger?.LogDebug(
                "{Provider}: free device memory unknown — memory check skipped",
                display);
#pragma warning restore CA1873
            return true;
        }

        if (free.Value >= memory.RequiredBytes)
            return true;

        long requiredMb = memory.RequiredBytes >> 20;
        long freeMb = free.Value >> 20;

        if (failFast)
            throw new ModelMemoryBudgetException(
                $"Execution provider '{display}' was explicitly requested, but the model needs an " +
                $"estimated {requiredMb} MB of device memory while only {freeMb} MB is free. " +
                "Use ExecutionProvider=auto for CPU fallback, pick a smaller model, or free GPU memory.");

        logger?.LogWarning(
            "{Provider}: model needs an estimated {RequiredMb} MB of device memory but only {FreeMb} MB " +
            "is free — skipping it to avoid OOM/shared-memory demotion, falling back to CPU",
            display, requiredMb, freeMb);
        return false;
    }

    private static long? SafeFreeBytes(IGpuMemoryProbe probe)
    {
        try
        {
            return probe.GetFreeBytes();
        }
        catch
        {
            return null; // a misbehaving probe must never block provider selection
        }
    }

    // ── CUDA arena cap ────────────────────────────────────────────────────────

    private static void AppendCuda(SessionOptions opts, long gpuMemLimitBytes, ILogger? logger)
    {
        if (gpuMemLimitBytes <= 0)
        {
            opts.AppendExecutionProvider_CUDA();
            return;
        }

        // gpu_mem_limit caps the arena allocator so a single session cannot grab the
        // whole device. The options object is copied natively on append — safe to dispose.
        using var cudaOptions = new OrtCUDAProviderOptions();
        cudaOptions.UpdateOptions(new Dictionary<string, string>
        {
            ["device_id"]     = "0",
            ["gpu_mem_limit"] = gpuMemLimitBytes.ToString(CultureInfo.InvariantCulture),
        });
        opts.AppendExecutionProvider_CUDA(cudaOptions);

        long limitMb = gpuMemLimitBytes >> 20;
#pragma warning disable CA1873 // limitMb is a cheap precomputed local
        logger?.LogInformation("CUDA arena capped at {LimitMb} MB (gpu_mem_limit)", limitMb);
#pragma warning restore CA1873
    }
}
