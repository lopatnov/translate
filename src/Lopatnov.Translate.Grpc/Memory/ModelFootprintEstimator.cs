namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Estimates the in-memory footprint of an ONNX model from its files on disk.
/// Weight files dominate a session's size; activations, workspace and KV-cache are
/// covered by the multiplicative overhead factor
/// (<c>Translation:MemoryPolicy:OverheadFactor</c>).
/// </summary>
public static class ModelFootprintEstimator
{
    /// <summary>
    /// Sums the sizes of the given model files plus any external-data companions —
    /// optimum exports the weights of &gt;2 GB models into sibling files such as
    /// <c>encoder_model.onnx_data</c> or <c>encoder_model.onnx.data</c>, which share
    /// the model file's name as a prefix.
    /// Missing or invalid paths contribute 0; a total of 0 means "footprint unknown".
    /// </summary>
    public static long EstimateFileBytes(IEnumerable<string> modelFiles)
    {
        long total = 0;
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in modelFiles)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            // Best-effort per path: an I/O or permission failure (unreadable directory,
            // file deleted mid-scan…) contributes what was readable so far — it must
            // never surface as an exception that blocks model loading.
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || !counted.Add(file.FullName))
                    continue;

                total += file.Length;

                var directory = file.Directory;
                if (directory is null)
                    continue;

                foreach (var companion in directory.EnumerateFiles(file.Name + "*"))
                {
                    if (IsExternalDataCompanion(file.Name, companion.Name) &&
                        counted.Add(companion.FullName))
                        total += companion.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException
                                          or System.Security.SecurityException)
            {
                // skip the unreadable path
            }
        }

        return total;
    }

    // ONNX external-data conventions only ("model.onnx_data" / "model.onnx.data") —
    // exact match, so unrelated same-prefix siblings ("model.onnx.bak", "model.onnx.sha256")
    // never inflate the estimate and wrongly trip the admission gate or skip a GPU.
    private static bool IsExternalDataCompanion(string modelFileName, string candidateFileName) =>
        candidateFileName.Equals(modelFileName + "_data", StringComparison.OrdinalIgnoreCase) ||
        candidateFileName.Equals(modelFileName + ".data", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies the activation/workspace overhead factor to a raw file-size estimate.
    /// Factors below 1.0 are clamped to 1.0 (a session can never be smaller than its
    /// weights); a non-positive file size stays 0 ("unknown").
    /// </summary>
    public static long ApplyOverhead(long fileBytes, double overheadFactor) =>
        fileBytes <= 0 ? 0 : (long)(fileBytes * Math.Max(1.0, overheadFactor));
}
