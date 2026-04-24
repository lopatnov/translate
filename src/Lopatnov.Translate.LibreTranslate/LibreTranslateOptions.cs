using System.ComponentModel.DataAnnotations;

namespace Lopatnov.Translate.LibreTranslate;

public sealed class LibreTranslateOptions
{
    [Url]
    public string? BaseUrl { get; set; }
    public string ApiKey { get; set; } = string.Empty;
}
