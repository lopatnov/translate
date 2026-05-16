using System.Runtime.InteropServices;
using OnnxRuntimeException = Microsoft.ML.OnnxRuntime.OnnxRuntimeException;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Builds <see cref="SessionOptions"/> for an ONNX execution provider specified by name.
/// </summary>
public static class OnnxExecutionProviderHelper
{
    // Auto-detection priority per platform.
    // Each entry: (provider key, display name, append action).
    private static readonly (string Key, string Display, Action<SessionOptions>)[] AutoCandidates =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? [("directml", "DirectML", o => o.AppendExecutionProvider_DML(0))]
            : [("cuda",     "CUDA",     o => o.AppendExecutionProvider_CUDA())];

    /// <summary>
    /// Creates a <see cref="SessionOptions"/> for the requested execution provider.
    /// <list type="bullet">
    ///   <item><c>""</c> / <c>"auto"</c> (default) — auto-detects the best available provider.</item>
    ///   <item><c>"cpu"</c> — forces CPU, even when a GPU is available.</item>
    ///   <item><c>"directml"</c> — DirectML (Windows, any DX12 GPU). Warns and falls back to CPU if unavailable.</item>
    ///   <item><c>"cuda"</c> — CUDA (NVIDIA). Warns and falls back to CPU if unavailable.</item>
    ///   <item>Any other value — throws <see cref="ArgumentException"/>.</item>
    /// </list>
    /// </summary>
    public static SessionOptions BuildSessionOptions(string provider, ILogger? logger = null)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "" or "auto"  => BuildAuto(logger),
            "cpu"         => BuildCpu(logger),
            "directml"    => BuildExplicit("directml", "DirectML",
                                o => o.AppendExecutionProvider_DML(0), logger),
            "cuda"        => BuildExplicit("cuda", "CUDA",
                                o => o.AppendExecutionProvider_CUDA(), logger),
            var unknown   => throw new ArgumentException(
                                $"Unknown ONNX execution provider '{unknown}'. " +
                                "Valid values: auto, cpu, directml, cuda.",
                                nameof(provider)),
        };
    }

    // ── Auto ────────────────────────────────────────────────────────────────

    private static SessionOptions BuildAuto(ILogger? logger)
    {
        foreach (var (key, display, append) in AutoCandidates)
        {
            var opts = new SessionOptions();
            try
            {
                append(opts);
                logger?.LogInformation(
                    "ONNX execution provider auto-selected: {Provider} — inference will run on GPU/NPU",
                    display);
                return opts;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
            {
                opts.Dispose();
                logger?.LogDebug(
                    "Auto-detection: {Provider} not available ({Message}), trying next",
                    display, ex.Message);
            }
        }

        logger?.LogInformation(
            "ONNX execution provider auto-selected: CPU (no GPU/NPU provider available on this machine)");
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
        ILogger? logger)
    {
        var opts = new SessionOptions();
        try
        {
            append(opts);
            logger?.LogInformation("ONNX execution provider: {Provider}", display);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
        {
            logger?.LogWarning(
                "Execution provider '{Provider}' is not available — falling back to CPU. " +
                "Ensure Microsoft.ML.OnnxRuntime.{Suffix} is installed. Details: {Message}",
                key,
                key == "directml" ? "DirectML" : "Gpu",
                ex.Message);
        }
        return opts;
    }
}
