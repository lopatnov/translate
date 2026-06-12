namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Admission control for model loads: refuses to start a load whose estimated footprint
/// exceeds the currently available system memory, instead of letting several large
/// models (e.g. m2m100_1.2B + whisper-medium + piper voices) overcommit RAM together.
///
/// <para>
/// Loads with a known footprint are serialised through a single lock so that two
/// concurrent first-requests cannot both pass the check and overcommit jointly: by the
/// time the second load enters the gate, the first one's allocation is already visible
/// to the memory probe. Loads with an unknown footprint (remote providers, missing
/// model files) bypass the gate entirely.
/// </para>
/// </summary>
public sealed class ModelLoadAdmissionGate(Func<long?> availableBytesProvider, ILogger? logger = null)
{
    private readonly object _gate = new();

    /// <summary>
    /// Runs <paramref name="load"/> if <paramref name="requiredBytes"/> fits into the
    /// currently available system memory. When availability cannot be determined the
    /// load is admitted optimistically (legacy behaviour).
    /// </summary>
    /// <exception cref="ModelMemoryBudgetException">
    /// The estimated footprint exceeds the available system memory.
    /// </exception>
    public T Run<T>(string modelKey, long requiredBytes, Func<T> load)
    {
        if (requiredBytes <= 0)
            return load(); // unknown footprint — nothing to admit against

        lock (_gate)
        {
            long? available = SafeAvailableBytes();
            long requiredMb = requiredBytes >> 20;

            if (available is long a && a < requiredBytes)
            {
                long availableMb = a >> 20;
                logger?.LogWarning(
                    "Refusing to load model '{Model}': estimated footprint {RequiredMb} MB exceeds " +
                    "available system memory {AvailableMb} MB",
                    modelKey, requiredMb, availableMb);
                throw new ModelMemoryBudgetException(
                    $"Model '{modelKey}' needs an estimated {requiredMb} MB of system memory but only " +
                    $"{availableMb} MB is available. Idle models are evicted after " +
                    "Translation:ModelTtlMinutes — retry later, or restrict Translation:AllowedModels.");
            }

            long knownAvailableMb = available.HasValue ? available.Value >> 20 : -1;
#pragma warning disable CA1873 // all arguments are cheap precomputed locals
            logger?.LogDebug(
                "Admitting model '{Model}': estimated footprint {RequiredMb} MB, available {AvailableMb} MB (-1 = unknown)",
                modelKey, requiredMb, knownAvailableMb);
#pragma warning restore CA1873

            return load();
        }
    }

    private long? SafeAvailableBytes()
    {
        try
        {
            return availableBytesProvider();
        }
        catch
        {
            return null; // probe failure must never block model loading
        }
    }
}
