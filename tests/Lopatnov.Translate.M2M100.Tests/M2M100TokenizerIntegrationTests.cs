using Xunit.Abstractions;

namespace Lopatnov.Translate.M2M100.Tests;

[Trait("Category", "Integration")]
public sealed class M2M100TokenizerIntegrationTests(ITestOutputHelper output)
{
    private static readonly string ModelPath = ResolveModelPath();

    private static string ResolveModelPath()
    {
        var envPath = Environment.GetEnvironmentVariable("Models__M2M100__Path");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "translate.slnx")))
                return Path.Combine(dir.FullName, "models", "translate", "m2m100_418M");
            dir = dir.Parent;
        }
        return Path.Combine("models", "translate", "m2m100_418M");
    }

    [SkippableTheory]
    [InlineData("eng_Latn", 128022L)]
    [InlineData("en",       128022L)]
    [InlineData("ukr_Cyrl", 128094L)]
    [InlineData("uk",       128094L)]
    [InlineData("rus_Cyrl", 128077L)]
    [InlineData("ru",       128077L)]
    public void GetLanguageTokenId_MatchesAddedTokensJson(string code, long expectedId)
    {
        Skip.If(!Directory.Exists(ModelPath), $"M2M-100 model not found at '{ModelPath}'.");

        using var tokenizer = new M2M100Tokenizer(ModelPath, configFile: "added_tokens.json");

        Assert.Equal(expectedId, tokenizer.GetLanguageTokenId(code));
    }

    [SkippableFact]
    public void Encode_StartsWithSourceLangTokenAndEndsWithEos()
    {
        Skip.If(!Directory.Exists(ModelPath), $"M2M-100 model not found at '{ModelPath}'.");

        using var tokenizer = new M2M100Tokenizer(ModelPath, configFile: "added_tokens.json");

        var ids = tokenizer.Encode("Hello world", "eng_Latn");

        output.WriteLine($"Encoded IDs: [{string.Join(", ", ids)}]");
        output.WriteLine($"Content range: [{ids[1..^1].Min()}, {ids[1..^1].Max()}]");

        Assert.Equal(128022L, ids[0]);
        Assert.Equal(M2M100Tokenizer.EosTokenId, ids[^1]);
        Assert.True(ids.Length >= 3, $"Expected at least 3 tokens, got {ids.Length}");
    }

    [SkippableFact]
    public void ContentTokenIds_AreInBpeRange()
    {
        Skip.If(!Directory.Exists(ModelPath), $"M2M-100 model not found at '{ModelPath}'.");

        using var tokenizer = new M2M100Tokenizer(ModelPath, configFile: "added_tokens.json");

        var ids = tokenizer.Encode("Hello, how are you?", "eng_Latn");
        var contentIds = ids[1..^1]; // skip lang token and EOS

        output.WriteLine($"Content IDs: [{string.Join(", ", contentIds)}]");

        // After +spOffset shift, BPE tokens must be in [4, 128003] — not special tokens (0-3) or lang tokens (128004+).
        foreach (var id in contentIds)
            Assert.InRange(id, 4L, 128003L);
    }

    [SkippableFact]
    public void Decode_Encode_RoundTrip()
    {
        Skip.If(!Directory.Exists(ModelPath), $"M2M-100 model not found at '{ModelPath}'.");

        using var tokenizer = new M2M100Tokenizer(ModelPath, configFile: "added_tokens.json");
        const string original = "Hello, how are you?";

        var ids = tokenizer.Encode(original, "eng_Latn");
        var contentIds = ids.Skip(1).SkipLast(1); // exclude lang token and EOS
        var decoded = tokenizer.Decode(contentIds);

        output.WriteLine($"Original : {original}");
        output.WriteLine($"Decoded  : {decoded}");

        Assert.False(string.IsNullOrWhiteSpace(decoded), "Decode produced empty string");
        Assert.Equal(original, decoded);
    }

    /// <summary>
    /// Verifies that the SentencePieceOffset maps content token IDs to the correct
    /// BPE pieces in vocab.json. A wrong offset would produce IDs that either don't
    /// exist in vocab.json or map to unrelated pieces.
    /// </summary>
    [SkippableFact]
    public void ContentTokenIds_MapToSensiblePiecesInVocab()
    {
        Skip.If(!Directory.Exists(ModelPath), $"M2M-100 model not found at '{ModelPath}'.");

        var vocabPath = Path.Combine(ModelPath, "vocab.json");
        Skip.If(!File.Exists(vocabPath), "vocab.json not found — cannot verify offset.");

        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(vocabPath, System.Text.Encoding.UTF8));
        var reverseVocab = new Dictionary<long, string>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            reverseVocab[prop.Value.GetInt64()] = prop.Name;

        using var tokenizer = new M2M100Tokenizer(ModelPath, configFile: "added_tokens.json");

        var ids = tokenizer.Encode("Hello", "eng_Latn");
        var contentIds = ids[1..^1]; // HF-space BPE IDs (after +spOffset)

        var pieces = contentIds.Select(id =>
            reverseVocab.TryGetValue(id, out var p) ? p : $"[UNKNOWN:{id}]").ToList();

        output.WriteLine($"Encoded IDs  : [{string.Join(", ", contentIds)}]");
        output.WriteLine($"Vocab pieces : [{string.Join(", ", pieces)}]");

        var reconstructed = string.Join("", pieces).Replace("▁", " ").Trim();
        output.WriteLine($"Reconstructed: {reconstructed}");

        Assert.All(contentIds, id =>
            Assert.True(reverseVocab.ContainsKey(id),
                $"ID {id} not found in vocab.json — SentencePieceOffset is likely wrong"));

        Assert.Contains("Hello", reconstructed, StringComparison.OrdinalIgnoreCase);
    }
}
