namespace Lopatnov.Translate.Core.Abstractions;

/// <summary>
/// Text translation provider. One interface, many model adapters (NLLB, M2M-100,
/// LibreTranslate, gRPC redirect, …) registered by name.
/// </summary>
public interface ITextTranslator
{
    /// <summary>
    /// Translates <paramref name="text"/> from <paramref name="sourceLanguage"/> to
    /// <paramref name="targetLanguage"/>.
    /// <para>
    /// Language codes are <b>BCP-47</b> tags (e.g. "en", "uk", "zh-Hans") — the system-wide
    /// interchange format. Each adapter converts BCP-47 to its model's native codes
    /// internally; codes the adapter does not recognise as BCP-47 are passed through
    /// unchanged, so callers may also supply the model's native codes directly.
    /// </para>
    /// </summary>
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
}
