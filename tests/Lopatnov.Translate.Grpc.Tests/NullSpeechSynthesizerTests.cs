namespace Lopatnov.Translate.Grpc.Tests;

public sealed class NullSpeechSynthesizerTests
{
    [Fact]
    public async Task SynthesizeAsync_ThrowsNotSupportedException()
    {
        var sut = new NullSpeechSynthesizer();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SynthesizeAsync("hello", "en",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SynthesizeAsync_MessageMentionsTts()
    {
        var sut = new NullSpeechSynthesizer();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SynthesizeAsync("hello", "en",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Text-to-speech", ex.Message);
    }
}
