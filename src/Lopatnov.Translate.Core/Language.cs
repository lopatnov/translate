namespace Lopatnov.Translate.Core;

/// <summary>
/// BCP-47 language tags for the languages the built-in heuristic detector can identify.
/// BCP-47 is the system-wide interchange format: detectors emit it, the gRPC API accepts it,
/// and each model adapter converts it to the model's native codes internally.
/// </summary>
public static class Language
{
    public const string English = "en";
    public const string Ukrainian = "uk";
    public const string Russian = "ru";
    public const string German = "de";
    public const string French = "fr";
    public const string Spanish = "es";
    public const string Portuguese = "pt";
    public const string Italian = "it";
    public const string Polish = "pl";
    public const string Romanian = "ro";
    public const string Swedish = "sv";
    public const string Czech = "cs";
    public const string Turkish = "tr";
    public const string Hungarian = "hu";
    public const string ChineseSimplified = "zh-Hans";
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string Arabic = "ar";
    public const string Hindi = "hi";
    public const string Thai = "th";
    public const string Greek = "el";
    public const string Hebrew = "he";
}
