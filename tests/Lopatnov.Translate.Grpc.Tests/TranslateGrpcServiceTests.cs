using Grpc.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class TranslateGrpcServiceTests
{
    [Theory]
    [InlineData("nllb")]
    [InlineData("libretranslate")]
    public async Task TranslateText_DispatchesToCorrectProvider(string provider)
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextTranslator>(provider, mockTranslator.Object);
        var sp = services.BuildServiceProvider();

        var svc = new TranslateGrpcService(sp);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var request = new TranslateTextRequest
        {
            Text = "hello",
            SourceLanguage = "eng_Latn",
            TargetLanguage = "ukr_Cyrl",
            Provider = provider,
        };

        var response = await svc.TranslateText(request, ctx.Object);

        Assert.Equal("translated", response.TranslatedText);
        Assert.Equal(provider, response.ProviderUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_DefaultsToNllbWhenProviderIsEmpty()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITextTranslator>("nllb", mockTranslator.Object);
        var sp = services.BuildServiceProvider();

        var svc = new TranslateGrpcService(sp);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var request = new TranslateTextRequest
        {
            Text = "hello",
            SourceLanguage = "eng_Latn",
            TargetLanguage = "ukr_Cyrl",
        };

        var response = await svc.TranslateText(request, ctx.Object);

        Assert.Equal("nllb", response.ProviderUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
