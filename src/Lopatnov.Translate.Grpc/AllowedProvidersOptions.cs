namespace Lopatnov.Translate.Grpc;

public sealed class AllowedProvidersOptions
{
    /// <summary>
    /// Restricts which providers can be used. Empty list = all configured providers allowed.
    /// </summary>
    public string[] AllowedProviders { get; set; } = [];

    /// <summary>
    /// Minutes of inactivity after which a loaded model is unloaded to free memory.
    /// </summary>
    public int ModelTtlMinutes { get; set; } = 30;
}
