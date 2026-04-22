namespace Lopatnov.Translate.Core.Models;

public sealed record TranslationRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string Provider = "");
