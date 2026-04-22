using System.ComponentModel.DataAnnotations;

namespace Lopatnov.Translate.LibreTranslate;

public sealed class LibreTranslateOptions
{
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = string.Empty;
}
