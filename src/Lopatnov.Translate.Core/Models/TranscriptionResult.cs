namespace Lopatnov.Translate.Core.Models;

public sealed record TranscriptionSegment(string Text, float StartTime, float EndTime);

public sealed record TranscriptionResult(
    IReadOnlyList<TranscriptionSegment> Segments,
    string DetectedLanguage,
    string FullText);
