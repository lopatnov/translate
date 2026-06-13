using System.ComponentModel.DataAnnotations;

namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Settings for memory-aware execution-provider selection and model-load admission
/// control. Bound from <c>Translation:MemoryPolicy</c> in appsettings.json.
/// </summary>
public sealed class ModelMemoryPolicyOptions
{
    /// <summary>
    /// Master switch. <c>false</c> restores the legacy behaviour: no memory probing,
    /// every load admitted, GPU providers selected purely on API availability.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Multiplier applied to the on-disk size of a model's weight files to estimate its
    /// in-memory footprint (activations, workspace, KV-cache). Seq2seq inference
    /// typically needs ~1.5–2.0× the weight size; the default is the conservative end
    /// because the cost of underestimating is an OOM crash, while overestimating only
    /// causes an early CPU fallback.
    /// </summary>
    [Range(1.0, 16.0)]
    public double OverheadFactor { get; set; } = 2.0;

    /// <summary>
    /// Optional hard cap for the CUDA execution provider's memory arena
    /// (ORT <c>gpu_mem_limit</c>), in bytes. 0 = no cap.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long CudaGpuMemLimitBytes { get; set; }
}
