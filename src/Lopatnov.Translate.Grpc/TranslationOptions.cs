namespace Lopatnov.Translate.Grpc;

public sealed class TranslationOptions
{
    /// <summary>Restricts which models can be used by name. Empty = all configured translators allowed.</summary>
    public string[] AllowedModels { get; set; } = [];

    public int ModelTtlMinutes { get; set; } = 30;

    /// <summary>Name of the language-detection model entry in Models. Empty = heuristic fallback.</summary>
    public string AutoDetect { get; set; } = string.Empty;

    /// <summary>Default model name when request.Provider is empty.</summary>
    public string DefaultModel { get; set; } = string.Empty;
}
