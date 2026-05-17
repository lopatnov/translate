using Grpc.Core;
using Grpc.Net.Client;
using Lopatnov.Translate.Core.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Forwards <see cref="ITextTranslator.TranslateAsync"/> calls to another
/// Lopatnov.Translate gRPC service instance (e.g. on a different machine).
///
/// The <see cref="GrpcChannel"/> is long-lived and reused across requests;
/// <see cref="ModelSessionManager"/> disposes it when the TTL expires.
///
/// Cycle detection: a random <c>x-redirect-id</c> header is added to every
/// outgoing call and propagated by downstream redirects.  If a request
/// returns to this server while its ID is still registered in
/// <see cref="RedirectCycleDetector"/>, a <c>FailedPrecondition</c> error
/// is returned immediately.
/// </summary>
public sealed class GrpcRedirectTranslator : ITextTranslator, IDisposable
{
    private const string RedirectIdHeader = "x-redirect-id";

    private readonly GrpcChannel _channel;
    private readonly TranslateService.TranslateServiceClient _client;
    private readonly string _remoteModelName;
    private readonly RedirectCycleDetector _cycleDetector;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GrpcRedirectTranslator(
        string remoteUrl,
        string remoteModelName,
        RedirectCycleDetector cycleDetector,
        IHttpContextAccessor httpContextAccessor)
    {
        _channel = GrpcChannel.ForAddress(remoteUrl);
        _client = new TranslateService.TranslateServiceClient(_channel);
        _remoteModelName = remoteModelName;
        _cycleDetector = cycleDetector;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        // Read the redirect ID propagated by an upstream hop (if any).
        var incoming = _httpContextAccessor.HttpContext?
            .Request.Headers[RedirectIdHeader].FirstOrDefault();

        // If this server already issued a request with this ID, the call has
        // looped back — abort to prevent infinite recursion.
        if (incoming != null && _cycleDetector.IsActive(incoming))
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Redirect cycle detected (request-id: {incoming}). " +
                "Check your redirect model configuration for routing loops."));

        // Re-use the upstream ID so the chain is traceable end-to-end;
        // generate a fresh one for the first hop in the chain.
        var requestId = incoming ?? Guid.NewGuid().ToString("N");
        _cycleDetector.TryRegister(requestId);

        var headers = new Metadata { { RedirectIdHeader, requestId } };
        try
        {
            // sourceLanguage / targetLanguage are already FLORES-200 at this
            // point (TranslateGrpcService converts them before calling TranslateAsync).
            var response = await _client.TranslateTextAsync(
                new TranslateTextRequest
                {
                    Text = text,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    Model = _remoteModelName,
                    LanguageFormat = "flores200",
                },
                headers,
                cancellationToken: cancellationToken);

            return response.TranslatedText;
        }
        finally
        {
            _cycleDetector.Complete(requestId);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _channel.Dispose();
}
