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

        // Re-use the incoming ID (traceable chain) or generate a fresh one for the first hop.
        var requestId = incoming ?? Guid.NewGuid().ToString("N");

        // Only remove the entry from the detector in finally if we were the one
        // that registered it. If TryRegister returns false (a concurrent request
        // is already registered with this ID), calling Complete would remove that
        // other request's active marker and weaken the cycle-detection guarantee.
        bool registered = _cycleDetector.TryRegister(requestId);

        // If registration failed the ID is already active on this server, which
        // means the chain has looped back — abort immediately instead of forwarding
        // a request that will either loop again or silently corrupt cycle-detection.
        if (!registered)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Redirect cycle detected (duplicate request-id: {requestId}). " +
                "Check your redirect model configuration for routing loops."));

        var headers = new Metadata { { RedirectIdHeader, requestId } };
        try
        {
            // sourceLanguage / targetLanguage arrive as BCP-47 (the system interchange
            // format) — forward them as-is and let the remote instance's model adapter
            // convert to its own native codes.
            var response = await _client.TranslateTextAsync(
                new TranslateTextRequest
                {
                    Text = text,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    Model = _remoteModelName,
                    LanguageFormat = "bcp47",
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
