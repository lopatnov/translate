using OnnxRuntimeException = Microsoft.ML.OnnxRuntime.OnnxRuntimeException;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Builds <see cref="SessionOptions"/> for an ONNX execution provider specified by name.
/// If the requested provider is unavailable (wrong NuGet package or missing native library),
/// a warning is logged and the session falls back to the default CPU provider.
/// </summary>
public static class OnnxExecutionProviderHelper
{
    /// <summary>
    /// Creates a <see cref="SessionOptions"/> configured for the specified execution provider.
    /// </summary>
    /// <param name="provider">Provider name — "cpu" (default), "directml", or "cuda".</param>
    /// <param name="logger">Optional logger for info / warning messages.</param>
    public static SessionOptions BuildSessionOptions(string provider, ILogger? logger = null)
    {
        var opts = new SessionOptions();

        switch (provider.Trim().ToLowerInvariant())
        {
            case "directml":
                TryAppend(() =>
                {
                    opts.AppendExecutionProvider_DML(0);
                    logger?.LogInformation("ONNX execution provider: DirectML (device 0)");
                }, provider, logger);
                break;

            case "cuda":
                TryAppend(() =>
                {
                    opts.AppendExecutionProvider_CUDA();
                    logger?.LogInformation("ONNX execution provider: CUDA");
                }, provider, logger);
                break;

            case "cpu":
            default:
                logger?.LogDebug("ONNX execution provider: CPU");
                break;
        }

        return opts;
    }

    private static void TryAppend(Action appendAction, string provider, ILogger? logger)
    {
        try
        {
            appendAction();
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
        {
            logger?.LogWarning(
                "Execution provider '{Provider}' is not available in the current " +
                "Microsoft.ML.OnnxRuntime package — falling back to CPU. " +
                "Install Microsoft.ML.OnnxRuntime.{PackageSuffix} to enable it. Details: {Message}",
                provider,
                provider.Equals("directml", StringComparison.OrdinalIgnoreCase) ? "DirectML" : "Gpu",
                ex.Message);
        }
    }
}
