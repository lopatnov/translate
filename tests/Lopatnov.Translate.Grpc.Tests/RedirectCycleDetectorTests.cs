namespace Lopatnov.Translate.Grpc.Tests;

public sealed class RedirectCycleDetectorTests
{
    [Fact]
    public void IsActive_ReturnsFalse_ForUnknownId()
    {
        var sut = new RedirectCycleDetector();
        Assert.False(sut.IsActive("abc"));
    }

    [Fact]
    public void TryRegister_ReturnsTrue_ForNewId()
    {
        var sut = new RedirectCycleDetector();
        Assert.True(sut.TryRegister("abc"));
    }

    [Fact]
    public void IsActive_ReturnsTrue_AfterRegister()
    {
        var sut = new RedirectCycleDetector();
        sut.TryRegister("abc");
        Assert.True(sut.IsActive("abc"));
    }

    [Fact]
    public void TryRegister_ReturnsFalse_ForDuplicateId()
    {
        var sut = new RedirectCycleDetector();
        sut.TryRegister("abc");
        Assert.False(sut.TryRegister("abc"));
    }

    [Fact]
    public void Complete_RemovesId()
    {
        var sut = new RedirectCycleDetector();
        sut.TryRegister("abc");
        sut.Complete("abc");
        Assert.False(sut.IsActive("abc"));
    }

    [Fact]
    public void Complete_IsIdempotent_WhenIdNotRegistered()
    {
        var sut = new RedirectCycleDetector();
        var ex = Record.Exception(() => sut.Complete("nonexistent"));
        Assert.Null(ex);
    }

    [Fact]
    public void MultipleIds_AreTrackedIndependently()
    {
        var sut = new RedirectCycleDetector();
        sut.TryRegister("a");
        sut.TryRegister("b");

        sut.Complete("a");

        Assert.False(sut.IsActive("a"));
        Assert.True(sut.IsActive("b"));
    }
}
