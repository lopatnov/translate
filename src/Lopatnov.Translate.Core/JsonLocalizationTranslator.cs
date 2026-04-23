using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core;

public static class JsonLocalizationTranslator
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<(string Json, int StringsTranslated)> TranslateAsync(
        string json,
        ITextTranslator translator,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default,
        string? existingTranslation = null,
        string? context = null)
    {
        using var doc = JsonDocument.Parse(json);
        using var existingDoc = existingTranslation is null ? null : JsonDocument.Parse(existingTranslation);
        using var contextDoc = context is null ? null : JsonDocument.Parse(context);

        JsonElement? existingRoot = existingDoc is null ? null : existingDoc.RootElement;
        JsonElement? contextRoot = contextDoc is null ? null : contextDoc.RootElement;

        var (node, count) = await TranslateNodeAsync(
            doc.RootElement, translator, sourceLanguage, targetLanguage, cancellationToken,
            existingRoot, contextRoot);
        var result = node is null ? "null" : node.ToJsonString(_serializerOptions);
        return (result, count);
    }

    private static async Task<(JsonNode? Node, int Count)> TranslateNodeAsync(
        JsonElement element,
        ITextTranslator translator,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken,
        JsonElement? existingElement,
        JsonElement? contextElement)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                var objCount = 0;
                foreach (var prop in element.EnumerateObject())
                {
                    var (child, c) = await TranslateNodeAsync(
                        prop.Value, translator, sourceLanguage, targetLanguage, cancellationToken,
                        TryGetProperty(existingElement, prop.Name),
                        TryGetProperty(contextElement, prop.Name));
                    obj[prop.Name] = child;
                    objCount += c;
                }
                return (obj, objCount);

            case JsonValueKind.Array:
                var arr = new JsonArray();
                var arrCount = 0;
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var (child, c) = await TranslateNodeAsync(
                        item, translator, sourceLanguage, targetLanguage, cancellationToken,
                        TryGetIndex(existingElement, index),
                        TryGetIndex(contextElement, index));
                    arr.Add(child);
                    arrCount += c;
                    index++;
                }
                return (arr, arrCount);

            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return (JsonValue.Create(text), 0);

                if (existingElement is { ValueKind: JsonValueKind.String })
                {
                    var existing = existingElement.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(existing))
                        return (JsonValue.Create(existing), 0);
                }

                // contextElement is reserved for LLM-based providers; NLLB receives text only
                var translated = await translator.TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken);
                return (JsonValue.Create(translated), 1);

            default:
                return (JsonNode.Parse(element.GetRawText()), 0);
        }
    }

    private static JsonElement? TryGetProperty(JsonElement? element, string name)
    {
        if (element is { ValueKind: JsonValueKind.Object } el && el.TryGetProperty(name, out var prop))
            return prop;
        return null;
    }

    private static JsonElement? TryGetIndex(JsonElement? element, int index)
    {
        if (element is { ValueKind: JsonValueKind.Array } el && index < el.GetArrayLength())
            return el[index];
        return null;
    }
}
