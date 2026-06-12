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

            FileInfo file;
            try
            {
                file = new FileInfo(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                          or PathTooLongException or System.Security.SecurityException)
            {
                continue;
            }

            if (!file.Exists || !counted.Add(file.FullName))
                continue;

            total += file.Length;

            var directory = file.Directory;
            if (directory is null)
                continue;

            foreach (var companion in directory.EnumerateFiles(file.Name + "*"))
            {
                if (counted.Add(companion.FullName))
                    total += companion.Length;
            }
        }

        return total;
    }

    /// <summary>
    /// Applies the activation/workspace overhead factor to a raw file-size estimate.
    /// Factors below 1.0 are clamped to 1.0 (a session can never be smaller than its
    /// weights); a non-positive file size stays 0 ("unknown").
    /// </summary>
    public static long ApplyOverhead(long fileBytes, double overheadFactor) =>
        fileBytes <= 0 ? 0 : (long)(fileBytes * Math.Max(1.0, overheadFactor));
}
