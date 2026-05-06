using System.Buffers;
using System.Text;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Settings for <see cref="FastTextLanguageDetector"/>.
/// Controls how model labels are stripped and converted to FLORES-200 at load time.
/// </summary>
public sealed class FastTextLanguageDetectorSettings
{
    /// <summary>Format of the labels in the model file. Default: Flores200 (GlotLID).</summary>
    public LanguageCodeFormat LabelFormat { get; set; } = LanguageCodeFormat.Flores200;

    /// <summary>Prefix to strip from each label. Default: "__label__".</summary>
    public string LabelPrefix { get; set; } = "__label__";

    /// <summary>Optional suffix to strip from each label. Default: empty.</summary>
    public string LabelSuffix { get; set; } = string.Empty;
}

/// <summary>
/// Language detector backed by a fastText supervised model (.ftz compressed or .bin full-precision).
/// Loads the binary file directly — no Python or native fastText library needed.
///
/// Supported models:
///   lid.176.bin/.ftz — fastText LID-176, CC BY-SA 3.0, 176 languages (hierarchical softmax)
///                      Use LabelFormat = ISO639_1 ("iso639-1") in settings.
///   model.bin        — GlotLID v3 (cis-lmu/glotlid-model), Apache 2.0, 1633 languages (OVA)
///                      Use LabelFormat = Flores200 ("flores200") in settings (default).
///
/// Binary format reference: fastText v12 (magic = 793712314).
/// Loss type 1 (hierarchical softmax): Huffman tree is reconstructed for correct prediction.
/// Loss types 2/3/4 (ns/softmax/ova): simple argmax of dot products.
/// </summary>
public sealed class FastTextLanguageDetector : ILanguageDetector
{
    private const int KCENT = 256;
    private const int MAGIC = 793712314;
    private const int VERSION = 12;
    private const int LOSS_HS = 1;

    // Huffman tree node — used only when loss == hs
    private struct HsNode
    {
        public int Left;   // child node index, -1 = leaf
        public int Right;
        public long Count; // frequency; used during tree construction only
    }

    private readonly record struct ModelParams(int Dim, int Minn, int Maxn, int Bucket, int Nwords);

    private readonly record struct MatrixState(
        float[][]? Dense, long Rows,
        int PqNsubq, int PqDsub, int PqLastDsub,
        float[] PqCentroids, byte[] Codes,
        bool QNorm, float[]? NormCentroids, byte[]? NormCodes);

    // --- model params ---
    private readonly int _dim;
    private readonly int _minn;
    private readonly int _maxn;
    private readonly int _bucket;
    private readonly int _nwords;

    // --- vocabulary ---
    private readonly Dictionary<string, int> _wordToId;

    // --- product-quantized input matrix ---
    private readonly int _pqNsubq;
    private readonly int _pqDsub;
    private readonly int _pqLastDsub;
    private readonly float[] _pqCentroids;
    private readonly byte[] _codes;
    private readonly long _inputRows;

    // --- optional norm quantization ---
    private readonly bool _qnorm;
    private readonly float[]? _normCentroids;
    private readonly byte[]? _normCodes;

    // --- dense input matrix (.bin models) ---
    private readonly float[][]? _dense;

    // --- output matrix (full precision) ---
    private readonly float[] _output;
    private readonly string[] _labels;

    // --- hierarchical softmax tree (null for non-hs models) ---
    private readonly HsNode[]? _hsTree;

    // --- n-gram pruneidx for quantized/pruned models (null = no pruning) ---
    // Maps raw_hash (0..bucket-1) → new_ngram_idx; row = _nwords + new_ngram_idx
    private readonly Dictionary<int, int>? _ngramPruneidx;

    // -------------------------------------------------------------------------

    private FastTextLanguageDetector(
        ModelParams model, Dictionary<string, int> wordToId,
        MatrixState matrix, float[] output, string[] labels,
        HsNode[]? hsTree, Dictionary<int, int>? ngramPruneidx)
    {
        _dim = model.Dim; _minn = model.Minn; _maxn = model.Maxn;
        _bucket = model.Bucket; _nwords = model.Nwords;
        _wordToId = wordToId;
        _dense = matrix.Dense; _inputRows = matrix.Rows;
        _pqNsubq = matrix.PqNsubq; _pqDsub = matrix.PqDsub; _pqLastDsub = matrix.PqLastDsub;
        _pqCentroids = matrix.PqCentroids; _codes = matrix.Codes;
        _qnorm = matrix.QNorm; _normCentroids = matrix.NormCentroids; _normCodes = matrix.NormCodes;
        _output = output; _labels = labels;
        _hsTree = hsTree;
        _ngramPruneidx = ngramPruneidx;
    }

    // -------------------------------------------------------------------------

    public static FastTextLanguageDetector Load(string path) =>
        Load(path, new FastTextLanguageDetectorSettings());

    public static FastTextLanguageDetector Load(string path, FastTextLanguageDetectorSettings settings)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);

        int magic = br.ReadInt32();
        if (magic != MAGIC)
            throw new InvalidDataException($"Not a fastText model (magic={magic}).");
        int version = br.ReadInt32();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported fastText version {version}.");

        var (model, loss) = ReadModelArgs(br);

        // --- Dictionary ---
        int size = br.ReadInt32();
        int nwords = br.ReadInt32();
        int nlabels = br.ReadInt32();
        br.ReadInt64();            // ntokens
        long pruneidxSize = br.ReadInt64();

        var words = new string[size];
        var types = new byte[size];
        var counts = new long[size];
        for (int i = 0; i < size; i++)
        {
            words[i] = ReadNullTerminated(br);
            counts[i] = br.ReadInt64();
            types[i] = br.ReadByte(); // 0=word, 1=label
        }

        // pruneidx_ maps n-gram raw hashes only; words use their dictionary position directly.
        var ngramPruneidx = new Dictionary<int, int>();
        for (long i = 0; i < pruneidxSize; i++)
            ngramPruneidx[br.ReadInt32()] = br.ReadInt32();

        var wordToId = new Dictionary<string, int>(nwords, StringComparer.Ordinal);
        for (int i = 0; i < size; i++)
            if (types[i] == 0) wordToId[words[i]] = i;

        var labelFlores = new List<string>(nlabels);
        var labelCounts = new List<long>(nlabels);
        for (int i = 0; i < size; i++)
        {
            if (types[i] == 1)
            {
                labelFlores.Add(MapLabel(words[i], settings));
                labelCounts.Add(counts[i]);
            }
        }

        model = model with { Nwords = nwords };
        var matrix = ReadInputMatrixSection(br);

        // --- Output matrix (full precision) ---
        br.ReadBoolean(); // output quant flag (always false)
        long outM = br.ReadInt64();
        long outN = br.ReadInt64();
        var output = new float[outM * outN];
        for (long i = 0; i < outM * outN; i++)
            output[i] = br.ReadSingle();

        HsNode[]? hsTree = loss == LOSS_HS ? BuildHuffmanTree(labelCounts.ToArray()) : null;

        return new FastTextLanguageDetector(
            model, wordToId, matrix,
            output, labelFlores.ToArray(),
            hsTree,
            pruneidxSize > 0 ? ngramPruneidx : null);
    }

    private static (ModelParams model, int loss) ReadModelArgs(BinaryReader br)
    {
        // All current fastText models write: dim, ws, epoch, minCount, neg, wordNgrams,
        // loss, model, bucket, minn, maxn, lrUpdateRate, t (double) — 12 ints + 1 double.
        // Detect format by reading the first 8 bytes as double: valid lr is (1e-100, 100);
        // near-zero means dim+ws bytes → rewind and read from offset 8.
        double lr = br.ReadDouble();
        if (lr is > 1e-100 and < 100.0)
        {
            // Older models stored lr before dim; also skip verbose/pretrainedVectors tail.
            br.ReadInt32();            // lrUpdateRate
            int dim = br.ReadInt32();
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // ws epoch minCount neg
            br.ReadInt32();            // wordNgrams
            int loss = br.ReadInt32();
            br.ReadInt32();            // model
            int bucket = br.ReadInt32();
            int minn = br.ReadInt32();
            int maxn = br.ReadInt32();
            br.ReadDouble();           // t
            // verbose is always 0-5; dict size starts at 10 000+ — ranges don't overlap.
            int maybeVerbose = br.ReadInt32();
            if (maybeVerbose <= 5)
                br.ReadBytes((int)br.ReadUInt32()); // pretrainedVectors path (skip)
            else
                br.BaseStream.Seek(-4, SeekOrigin.Current); // put back: it was dict size
            return (new ModelParams(dim, minn, maxn, bucket, 0 /* nwords set from dict */), loss);
        }

        // Standard layout: dim starts at offset 8.
        br.BaseStream.Seek(8, SeekOrigin.Begin);
        {
            int dim = br.ReadInt32();
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // ws epoch minCount neg wordNgrams
            int loss = br.ReadInt32();
            br.ReadInt32();            // model
            int bucket = br.ReadInt32();
            int minn = br.ReadInt32();
            int maxn = br.ReadInt32();
            br.ReadInt32();            // nthreads (training-only)
            br.ReadDouble();           // t
            return (new ModelParams(dim, minn, maxn, bucket, 0), loss);
        }
    }

    private static MatrixState ReadInputMatrixSection(BinaryReader br)
    {
        bool qinput = br.ReadBoolean();

        if (!qinput)
        {
            long inputM = br.ReadInt64();
            long inputN = br.ReadInt64();
            var dense = new float[inputM][];
            for (long row = 0; row < inputM; row++)
            {
                var r = new float[inputN];
                for (long k = 0; k < inputN; k++) r[k] = br.ReadSingle();
                dense[row] = r;
            }
            return new MatrixState(dense, inputM, 0, 0, 0, [], [], false, null, null);
        }

        // Quantized (QMatrix::save): qnorm, m, n, codesize, codes, then PQ metadata.
        bool qnorm = br.ReadBoolean();
        long qM = br.ReadInt64();
        br.ReadInt64();                          // n = dim
        int codesize = br.ReadInt32();
        byte[] codes = br.ReadBytes(codesize);
        br.ReadInt32();                          // pqDimStored
        int pqNsubq = br.ReadInt32();
        int pqDsub = br.ReadInt32();
        int pqLastDsub = br.ReadInt32();
        int centroidsCount = pqNsubq * KCENT * Math.Max(pqDsub, pqLastDsub);
        float[] pqCentroids = new float[centroidsCount];
        for (int i = 0; i < centroidsCount; i++) pqCentroids[i] = br.ReadSingle();

        float[]? normCentroids = null;
        byte[]? normCodes = null;
        if (qnorm)
        {
            normCodes = br.ReadBytes((int)qM);
            br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // norm PQ header
            normCentroids = new float[KCENT];
            for (int i = 0; i < KCENT; i++) normCentroids[i] = br.ReadSingle();
        }

        return new MatrixState(null, qM, pqNsubq, pqDsub, pqLastDsub, pqCentroids, codes, qnorm, normCentroids, normCodes);
    }

    // -------------------------------------------------------------------------

    public LanguageDetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new LanguageDetectionResult(Language.EnglishLatin, LanguageCodeFormat.Flores200);

        var h = new float[_dim];
        int count = 0;

        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            int start = remaining.IndexOfAnyExcept(_whitespace);
            if (start < 0) break;
            remaining = remaining[start..];
            int end = remaining.IndexOfAny(_whitespace);
            var token = end < 0 ? remaining : remaining[..end];
            remaining = end < 0 ? default : remaining[end..];
            AddWordFeatures(token, h, ref count);
        }

        if (count == 0)
            return new LanguageDetectionResult(Language.EnglishLatin, LanguageCodeFormat.Flores200);

        for (int i = 0; i < _dim; i++)
            h[i] /= count;

        return new LanguageDetectionResult(
            _hsTree != null ? PredictHs(h) : PredictArgmax(h),
            LanguageCodeFormat.Flores200);
    }

    // -------------------------------------------------------------------------

    // Prediction for hierarchical softmax (loss=hs): full log-probability traversal.
    // A greedy descent would be O(log N) but incorrect — the product of branch probabilities
    // is not monotone so the best leaf may be in the "less likely" subtree at the root.
    private string PredictHs(float[] h)
    {
        int osz = _labels.Length;
        int root = 2 * osz - 2;

        float bestScore = float.NegativeInfinity;
        int bestLabel = 0;

        var stack = new Stack<(int Node, float LogScore)>();
        stack.Push((root, 0f));

        while (stack.Count > 0)
        {
            var (node, logScore) = stack.Pop();

            if (_hsTree![node].Left == -1) // leaf
            {
                if (logScore > bestScore) { bestScore = logScore; bestLabel = node; }
                continue;
            }

            // Internal node: sigmoid(wo[node - osz] · h)
            int woRow = node - osz;
            float dot = 0f;
            int rowBase = woRow * _dim;
            for (int k = 0; k < _dim; k++)
                dot += h[k] * _output[rowBase + k];
            float f = 1f / (1f + MathF.Exp(-dot));

            // Left child uses log(1-f), right child uses log(f)
            stack.Push((_hsTree[node].Right, logScore + MathF.Log(f)));
            stack.Push((_hsTree[node].Left, logScore + MathF.Log(1f - f)));
        }

        return bestLabel < osz ? _labels[bestLabel] : Language.EnglishLatin;
    }

    // Prediction for softmax/ova/ns: argmax of dot products.
    private string PredictArgmax(float[] h)
    {
        int best = 0;
        float bestScore = float.NegativeInfinity;
        int nlabels = _labels.Length;
        for (int j = 0; j < nlabels; j++)
        {
            float score = 0f;
            int rowBase = j * _dim;
            for (int k = 0; k < _dim; k++)
                score += h[k] * _output[rowBase + k];
            if (score > bestScore) { bestScore = score; best = j; }
        }
        return best < nlabels ? _labels[best] : Language.EnglishLatin;
    }

    // Reconstruct the Huffman tree from label frequencies.
    // Mirrors fastText's Model::initTreeStructure exactly.
    // labelCounts must be in descending frequency order (as stored in the dictionary).
    private static HsNode[] BuildHuffmanTree(long[] labelCounts)
    {
        int osz = labelCounts.Length;
        var tree = new HsNode[2 * osz];

        for (int i = 0; i < osz; i++)
            tree[i] = new HsNode { Left = -1, Right = -1, Count = labelCounts[i] };
        for (int i = osz; i < 2 * osz; i++)
            tree[i] = new HsNode { Left = -1, Right = -1, Count = long.MaxValue / 2 };

        int leaf = osz - 1; // starts at least-frequent leaf, moves left
        int node = osz;     // starts at first internal node, moves right

        for (int i = osz; i < 2 * osz - 1; i++)
        {
            int[] mini = new int[2];
            for (int j = 0; j < 2; j++)
            {
                if (leaf >= 0 && tree[leaf].Count < tree[node].Count)
                    mini[j] = leaf--;
                else
                    mini[j] = node++;
            }
            tree[i] = new HsNode
            {
                Left = mini[0],
                Right = mini[1],
                Count = tree[mini[0]].Count + tree[mini[1]].Count
            };
        }

        return tree;
    }

    // -------------------------------------------------------------------------

    private void AddWordFeatures(ReadOnlySpan<char> word, float[] target, ref int count)
    {
        // GetAlternateLookup returns a lightweight struct — no allocation.
        // StringComparer.Ordinal (used at construction) implements IAlternateEqualityComparer<ReadOnlySpan<char>, string>.
        if (_wordToId.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out int wordRow))
        {
            AddRowEmbedding(wordRow, target);
            count++;
        }

        if (_maxn <= 0) return;

        int maxLen = Encoding.UTF8.GetMaxByteCount(word.Length) + 2;
        byte[] rentedBuf = ArrayPool<byte>.Shared.Rent(maxLen);
        try
        {
            rentedBuf[0] = (byte)'<';
            int encoded = Encoding.UTF8.GetBytes(word, rentedBuf.AsSpan(1));
            rentedBuf[encoded + 1] = (byte)'>';
            AddNgramFeatures(rentedBuf, encoded + 2, target, ref count);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuf);
        }
    }

    private void AddNgramFeatures(byte[] buf, int len, float[] target, ref int count)
    {
        for (int i = 0; i < len; i++)
        {
            if ((buf[i] & 0xC0) == 0x80) continue;
            int start = i;
            int n = 0;
            for (int j = i; j < len && n < _maxn;)
            {
                j++;
                while (j < len && (buf[j] & 0xC0) == 0x80) j++;
                n++;
                if (n < _minn) continue;
                int rowIdx = GetNgramRowIdx(FnvHash(buf.AsSpan(start, j - start)) % (uint)_bucket);
                if (rowIdx >= 0)
                {
                    AddRowEmbedding(rowIdx, target);
                    count++;
                }
            }
        }
    }

    private int GetNgramRowIdx(uint rawHash)
    {
        if (_ngramPruneidx != null)
            return _ngramPruneidx.TryGetValue((int)rawHash, out int mapped) ? mapped + _nwords : -1;
        int idx = (int)rawHash + _nwords;
        return idx < _inputRows ? idx : -1;
    }

    private void AddRowEmbedding(int rowIdx, float[] target)
    {
        if (_dense != null)
        {
            float[] row = _dense[rowIdx];
            for (int k = 0; k < _dim; k++) target[k] += row[k];
            return;
        }
        AddPqRowEmbedding(rowIdx, target);
    }

    private void AddPqRowEmbedding(int rowIdx, float[] target)
    {
        int codeBase = rowIdx * _pqNsubq;
        int dimOffset = 0;
        for (int j = 0; j < _pqNsubq; j++)
        {
            int centroidIdx = _codes[codeBase + j];
            int dsub = j == _pqNsubq - 1 ? _pqLastDsub : _pqDsub;
            int centBase = j < _pqNsubq - 1
                ? (j * KCENT + centroidIdx) * _pqDsub
                : (_pqNsubq - 1) * KCENT * _pqDsub + centroidIdx * _pqLastDsub;
            for (int d = 0; d < dsub; d++) target[dimOffset + d] += _pqCentroids[centBase + d];
            dimOffset += dsub;
        }

        if (!_qnorm || _normCodes == null || _normCentroids == null) return;

        float norm = _normCentroids[_normCodes[rowIdx]];
        dimOffset = 0;
        for (int j = 0; j < _pqNsubq; j++)
        {
            int centroidIdx = _codes[codeBase + j];
            int dsub = j == _pqNsubq - 1 ? _pqLastDsub : _pqDsub;
            int centBase = j < _pqNsubq - 1
                ? (j * KCENT + centroidIdx) * _pqDsub
                : (_pqNsubq - 1) * KCENT * _pqDsub + centroidIdx * _pqLastDsub;
            for (int d = 0; d < dsub; d++) target[dimOffset + d] += _pqCentroids[centBase + d] * (norm - 1f);
            dimOffset += dsub;
        }
    }

    // -------------------------------------------------------------------------

    private static readonly SearchValues<char> _whitespace =
        SearchValues.Create(" \t\n\r\f\v");

    private static string ReadNullTerminated(BinaryReader br)
    {
        var bytes = new List<byte>();
        byte b;
        while (br.BaseStream.Position < br.BaseStream.Length && (b = br.ReadByte()) != 0)
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static uint FnvHash(ReadOnlySpan<byte> bytes)
    {
        uint h = 2166136261u;
        foreach (var b in bytes)
        {
            h ^= (uint)(sbyte)b;
            h *= 16777619u;
        }
        return h;
    }

    // -------------------------------------------------------------------------

    private static string MapLabel(string label, FastTextLanguageDetectorSettings s)
    {
        if (label.StartsWith(s.LabelPrefix, StringComparison.Ordinal))
            label = label[s.LabelPrefix.Length..];
        if (!string.IsNullOrEmpty(s.LabelSuffix) && label.EndsWith(s.LabelSuffix, StringComparison.Ordinal))
            label = label[..^s.LabelSuffix.Length];
        if (s.LabelFormat == LanguageCodeFormat.Flores200)
            return label;
        return LanguageCodeConverter.Convert(label, s.LabelFormat, LanguageCodeFormat.Flores200);
    }
}
