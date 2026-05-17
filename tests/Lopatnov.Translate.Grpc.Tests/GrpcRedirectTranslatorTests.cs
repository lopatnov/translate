using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="GrpcRedirectTranslator"/> covering cycle-detection
/// paths that execute before any gRPC network call is made.
/// </summary>
public sealed class GrpcRedirectTranslatorTests
{
    private static GrpcRedirectTranslator Build(
        RedirectCycleDetector detector,
        IHttpContextAccessor accessor)
        => new(
            remoteUrl: "http://localhost:59999", // unreachable — tests abort before network
            remoteModelName: "test-model",
            cycleDetector: detector,
            httpContextAccessor: accessor);

    private static IHttpContextAccessor NoContext()
    {
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        return mock.Object;
    }

    private static IHttpContextAccessor WithHeader(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["x-redirect-id"] = headerValue;
        var mock = new Mock<IHttpContextAccessor>();
        mock.Setup(a => a.HttpContext).Returns(context);
        return mock.Object;
    }

    // ── Cycle via active incoming header ──────────────────────────────────────

    [Fact]
    public async Task TranslateAsync_ThrowsFailedPrecondition_WhenIncomingIdIsActive()
    {
        var detector = new RedirectCycleDetector();
        detector.TryRegister("cycle-id");  // simulate this server already issued it

        using var sut = Build(detector, WithHeader("cycle-id"));

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => sut.TranslateAsync("text", "eng_Latn", "fra_Latn",
                TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Contains("cycle-id", ex.Status.Detail);
    }

    // ── Cycle via duplicate TryRegister ──────────────────────────────────────

    [Fact]
    public async Task TranslateAsync_ThrowsFailedPrecondition_WhenRequestIdAlreadyRegistered()
    {
        // Pre-register the exact same ID that would be generated from the incoming header.
        const string requestId = "dup-id";
        var detector = new RedirectCycleDetector();
        detector.TryRegister(requestId);   // first registration (simulates another concurrent hop)
        detector.TryRegister(requestId);   // TryRegister is idempotent — still active

        // The incoming header brings "dup-id"; IsActive is true → FailedPrecondition.
        using var sut = Build(detector, WithHeader(requestId));

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => sut.TranslateAsync("text", "eng_Latn", "fra_Latn",
                TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var detector = new RedirectCycleDetector();
        var sut = Build(detector, NoContext());
        var ex = Record.Exception(sut.Dispose);
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var detector = new RedirectCycleDetector();
        var sut = Build(detector, NoContext());
        sut.Dispose();
        var ex = Record.Exception(sut.Dispose);
        Assert.Null(ex);
    }
}
