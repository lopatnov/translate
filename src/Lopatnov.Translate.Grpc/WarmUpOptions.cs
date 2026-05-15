namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Configuration for model pre-loading at startup.
/// Bind from the <c>WarmUp</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class WarmUpOptions
{
    /// <summary>
    /// Names of model entries (keys in the <c>Models</c> section) to warm up when the
    /// service starts. Unknown keys are logged and skipped. Empty array disables warm-up.
    /// </summary>
    public string[] Models { get; set; } = [];
}
