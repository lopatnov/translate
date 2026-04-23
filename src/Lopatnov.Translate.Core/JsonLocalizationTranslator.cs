using System.Text.Json;
using System.Text.Json.Nodes;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core;

public static class JsonLocalizationTranslator
{
    public static async Task<(string Json, int StringsTranslated)> TranslateAsync(
        string json,
        ITextTranslator translator,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(json);
        var (node, count) = await TranslateNodeAsync(doc.RootElement, translator, sourceLanguage, targetLanguage, cancellationToken);
        var result = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
        return (result, count);
    }

    private static async Task<(JsonNode? Node, int Count)> TranslateNodeAsync(
        JsonElement element,
        ITextTranslator translator,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                var objCount = 0;
                foreach (var prop in element.EnumerateObject())
                {
                    var (child, c) = await TranslateNodeAsync(prop.Value, translator, sourceLanguage, targetLanguage, cancellationToken);
                    obj[prop.Name] = child;
                    objCount += c;
                }
                return (obj, objCount);

            case JsonValueKind.Array:
                var arr = new JsonArray();
                var arrCount = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var (child, c) = await TranslateNodeAsync(item, translator, sourceLanguage, targetLanguage, cancellationToken);
                    arr.Add(child);
                    arrCount += c;
                }
                return (arr, arrCount);

            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return (JsonValue.Create(text), 0);
                var translated = await translator.TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken);
                return (JsonValue.Create(translated), 1);

            default:
                return (JsonNode.Parse(element.GetRawText()), 0);
        }
    }
}
