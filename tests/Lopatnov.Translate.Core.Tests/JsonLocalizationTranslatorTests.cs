using System.Text.Json;
using Lopatnov.Translate.Core.Abstractions;
using Moq;

namespace Lopatnov.Translate.Core.Tests;

public sealed class JsonLocalizationTranslatorTests
{
    private static Mock<ITextTranslator> Translator(Func<string, string>? map = null)
    {
        var mock = new Mock<ITextTranslator>();
        mock.Setup(t => t.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, string __, CancellationToken ___) =>
                map?.Invoke(text) ?? $"[{text}]");
        return mock;
    }

    [Fact]
    public async Task TranslateAsync_TranslatesLeafStrings()
    {
        var input = """{"greeting":"Hello","farewell":"Goodbye"}""";
        var translator = Translator();

        var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        var doc = JsonDocument.Parse(json);
        Assert.Equal("[Hello]", doc.RootElement.GetProperty("greeting").GetString());
        Assert.Equal("[Goodbye]", doc.RootElement.GetProperty("farewell").GetString());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TranslateAsync_PreservesNestedObjects()
    {
        var input = """{"auth":{"email":"Email","password":"Password"}}""";
        var translator = Translator();

        var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        var doc = JsonDocument.Parse(json);
        var auth = doc.RootElement.GetProperty("auth");
        Assert.Equal("[Email]", auth.GetProperty("email").GetString());
        Assert.Equal("[Password]", auth.GetProperty("password").GetString());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TranslateAsync_TranslatesStringsInArrays()
    {
        var input = """{"items":["apple","banana"]}""";
        var translator = Translator();

        var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal("[apple]", items[0].GetString());
        Assert.Equal("[banana]", items[1].GetString());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TranslateAsync_PassesThroughNonStringValues()
    {
        var input = """{"count":42,"active":true,"ratio":1.5,"nothing":null}""";
        var translator = Translator();

        var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        var doc = JsonDocument.Parse(json);
        Assert.Equal(42, doc.RootElement.GetProperty("count").GetInt32());
        Assert.True(doc.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(1.5, doc.RootElement.GetProperty("ratio").GetDouble(), precision: 10);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("nothing").ValueKind);
        Assert.Equal(0, count);

        translator.Verify(
            t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task TranslateAsync_SkipsBlankStrings(string blank)
    {
        var input = $$$"""{"key":{{{JsonSerializer.Serialize(blank)}}}}""";
        var translator = Translator();

        var (_, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        Assert.Equal(0, count);
        translator.Verify(
            t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TranslateAsync_PassesCorrectLanguageCodes()
    {
        var input = """{"msg":"Hello"}""";
        var translator = Translator();

        await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        translator.Verify(
            t => t.TranslateAsync("Hello", "eng_Latn", "ukr_Cyrl", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_EmptyObject_ReturnsZeroCount()
    {
        var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
            "{}", Translator().Object, "eng_Latn", "ukr_Cyrl");

        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task TranslateAsync_PreservesKeyStructure()
    {
        var input = """{"a":{"b":{"c":"deep"}}}""";
        var translator = Translator(t => t.ToUpper());

        var (json, _) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "deu_Latn");

        var doc = JsonDocument.Parse(json);
        Assert.Equal("DEEP", doc.RootElement
            .GetProperty("a").GetProperty("b").GetProperty("c").GetString());
    }

    [Fact]
    public async Task TranslateAsync_MixedObject_CountsOnlyStrings()
    {
        var input = """{"label":"Click","count":5,"enabled":true,"name":"Submit"}""";
        var translator = Translator();

        var (_, count) = await JsonLocalizationTranslator.TranslateAsync(
            input, translator.Object, "eng_Latn", "ukr_Cyrl");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TranslateAsync_CancellationToken_ForwardedToTranslator()
    {
        var input = """{"key":"value"}""";
        var cts = new CancellationTokenSource();
        var capturedToken = CancellationToken.None;

        var mock = new Mock<ITextTranslator>();
        mock.Setup(t => t.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, _, ct) => capturedToken = ct)
            .ReturnsAsync("translated");

        await JsonLocalizationTranslator.TranslateAsync(
            input, mock.Object, "eng_Latn", "ukr_Cyrl", cts.Token);

        Assert.Equal(cts.Token, capturedToken);
    }
}
