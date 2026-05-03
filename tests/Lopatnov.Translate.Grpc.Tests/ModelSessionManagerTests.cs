using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class ModelSessionManagerTests
{
    private static ModelSessionManager Build(
        Dictionary<string, Func<ITextTranslator>> factories,
        string[] allowed = null!,
        int ttlMinutes = 30)
        => new(factories, allowed ?? [], TimeSpan.FromMinutes(ttlMinutes));

    [Fact]
    public void Get_ReturnsTranslator_WhenProviderConfigured()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object });

        Assert.Same(mock.Object, mgr.Get("nllb"));
    }

    [Fact]
    public void Get_ReturnsSameInstance_OnSubsequentCalls()
    {
        var callCount = 0;
        using var mgr = Build(new() { ["nllb"] = () => { callCount++; return new Mock<ITextTranslator>().Object; } });

        var t1 = mgr.Get("nllb");
        var t2 = mgr.Get("nllb");

        Assert.Same(t1, t2);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["NLLB"] = () => mock.Object });

        Assert.Same(mock.Object, mgr.Get("nllb"));
        Assert.Same(mock.Object, mgr.Get("NLLB"));
    }

    [Fact]
    public void Get_ThrowsKeyNotFoundException_ForUnknownProvider()
    {
        using var mgr = Build(new());

        Assert.Throws<KeyNotFoundException>(() => mgr.Get("unknown"));
    }

    [Fact]
    public void Get_ThrowsUnauthorizedAccess_ForNotAllowedProvider()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(
            new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object },
            allowed: ["nllb"]);

        Assert.Throws<UnauthorizedAccessException>(() => mgr.Get("m2m100"));
    }

    [Fact]
    public void Get_AllowsProvider_WhenAllowlistIsEmpty()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object });

        Assert.Same(mock.Object, mgr.Get("nllb"));
        Assert.Same(mock.Object, mgr.Get("m2m100"));
    }

    // --- Rent() / TranslatorLease ---

    [Fact]
    public void Rent_ReturnsLease_WithCorrectTranslatorAndKey()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object });

        using var lease = mgr.Rent("nllb");

        Assert.Same(mock.Object, lease.Translator);
        Assert.Equal("nllb", lease.Key);
    }

    [Fact]
    public void Rent_ReturnsSameTranslatorInstance_AcrossLeases()
    {
        var callCount = 0;
        using var mgr = Build(new() { ["nllb"] = () => { callCount++; return new Mock<ITextTranslator>().Object; } });

        using var l1 = mgr.Rent("nllb");
        using var l2 = mgr.Rent("nllb");

        Assert.Same(l1.Translator, l2.Translator);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Rent_IsCaseInsensitive()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["NLLB"] = () => mock.Object });

        using var lease = mgr.Rent("nllb");

        Assert.Same(mock.Object, lease.Translator);
    }

    [Fact]
    public void Rent_ThrowsKeyNotFoundException_ForUnknownProvider()
    {
        using var mgr = Build(new());

        Assert.Throws<KeyNotFoundException>(() => mgr.Rent("unknown"));
    }

    [Fact]
    public void Rent_ThrowsUnauthorizedAccess_ForNotAllowedProvider()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(
            new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object },
            allowed: ["nllb"]);

        Assert.Throws<UnauthorizedAccessException>(() => mgr.Rent("m2m100"));
    }

    [Fact]
    public void TranslatorLease_Dispose_IsIdempotent()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object });

        var lease = mgr.Rent("nllb");
        lease.Dispose();

        Assert.Null(Record.Exception(() => lease.Dispose()));
    }

    [Fact]
    public void Dispose_DisposesTranslator_LoadedViaRent()
    {
        var mockDisposable = new Mock<ITextTranslator>();
        mockDisposable.As<IDisposable>();

        var mgr = Build(new() { ["nllb"] = () => mockDisposable.Object });
        using (mgr.Rent("nllb")) { }

        mgr.Dispose();

        mockDisposable.As<IDisposable>().Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Rent_AllowsProvider_WhenAllowlistIsEmpty()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object });

        using var l1 = mgr.Rent("nllb");
        using var l2 = mgr.Rent("m2m100");

        Assert.Same(mock.Object, l1.Translator);
        Assert.Same(mock.Object, l2.Translator);
    }

    [Fact]
    public void GetAvailableModels_ReturnsAll_WhenNoAllowlist()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object });

        var providers = mgr.GetAvailableModels();

        Assert.Contains("nllb", providers);
        Assert.Contains("m2m100", providers);
        Assert.Equal(2, providers.Count);
    }

    [Fact]
    public void GetAvailableModels_ReturnsOnlyAllowed_WhenAllowlistSet()
    {
        var mock = new Mock<ITextTranslator>();
        using var mgr = Build(
            new() { ["nllb"] = () => mock.Object, ["m2m100"] = () => mock.Object, ["libretranslate"] = () => mock.Object },
            allowed: ["nllb", "libretranslate"]);

        var providers = mgr.GetAvailableModels();

        Assert.Contains("nllb", providers);
        Assert.Contains("libretranslate", providers);
        Assert.DoesNotContain("m2m100", providers);
    }

    [Fact]
    public void Dispose_DisposesLoadedTranslators()
    {
        var mockDisposable = new Mock<ITextTranslator>();
        mockDisposable.As<IDisposable>();

        var mgr = Build(new() { ["nllb"] = () => mockDisposable.Object });
        _ = mgr.Get("nllb"); // trigger lazy load

        mgr.Dispose();

        mockDisposable.As<IDisposable>().Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenProviderNeverLoaded()
    {
        var mgr = Build(new() { ["nllb"] = () => new Mock<ITextTranslator>().Object });

        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }
}
