using System.Text;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core;

/// <summary>
/// Language detector backed by a fastText supervised model (.ftz compressed or .bin full-precision).
/// Loads the binary file directly — no Python or native fastText library needed.
///
/// Supported models:
///   lid.176.bin/.ftz — fastText LID-176, CC BY-SA 3.0, 176 languages (hierarchical softmax)
///   model.bin        — GlotLID v3 (cis-lmu/glotlid-model), Apache 2.0, 1633 languages (OVA)
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

    // --- model params ---
    private readonly int _dim;
    private readonly int _minn;
    private readonly int _maxn;
    private readonly int _bucket;
    private readonly int _nwords;
    private readonly string _labelPrefix;

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
        int dim, int minn, int maxn, int bucket, int nwords, string labelPrefix,
        Dictionary<string, int> wordToId,
        int pqNsubq, int pqDsub, int pqLastDsub, float[] pqCentroids, byte[] codes, long inputRows,
        bool qnorm, float[]? normCentroids, byte[]? normCodes,
        float[][]? dense,
        float[] output, string[] labels,
        HsNode[]? hsTree,
        Dictionary<int, int>? ngramPruneidx)
    {
        _dim = dim; _minn = minn; _maxn = maxn; _bucket = bucket; _nwords = nwords; _labelPrefix = labelPrefix;
        _wordToId = wordToId;
        _pqNsubq = pqNsubq; _pqDsub = pqDsub; _pqLastDsub = pqLastDsub;
        _pqCentroids = pqCentroids; _codes = codes; _inputRows = inputRows;
        _qnorm = qnorm; _normCentroids = normCentroids; _normCodes = normCodes;
        _dense = dense;
        _output = output; _labels = labels;
        _hsTree = hsTree;
        _ngramPruneidx = ngramPruneidx;
    }

    // -------------------------------------------------------------------------

    public static FastTextLanguageDetector Load(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path), Encoding.UTF8, leaveOpen: false);

        int magic = br.ReadInt32();
        if (magic != MAGIC)
            throw new InvalidDataException($"Not a fastText model (magic={magic}).");
        int version = br.ReadInt32();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported fastText version {version}.");

        // --- Args ---
        // GlotLID models start with dim at pos 8 (no lr/lrUpdateRate).
        // Standard fastText models start with lr (double) at pos 8.
        // Detect by reading the first double: a valid lr is in (1e-100, 100); near-zero → GlotLID format.
        double lr = br.ReadDouble();
        bool isStandardFormat = lr is > 1e-100 and < 100.0;

        int dim, minn, maxn, bucket, loss;
        if (isStandardFormat)
        {
            br.ReadInt32();            // lrUpdateRate
            dim = br.ReadInt32();
            br.ReadInt32();            // ws
            br.ReadInt32();            // epoch
            br.ReadInt32();            // minCount
            br.ReadInt32();            // neg
            br.ReadInt32();            // wordNgrams
            loss = br.ReadInt32();
            br.ReadInt32();            // model
            bucket = br.ReadInt32();
            minn = br.ReadInt32();
            maxn = br.ReadInt32();
            br.ReadDouble();           // t
            // Older fastText models (e.g. lid.176.bin) do not write verbose or
            // pretrainedVectors. Detect by peeking: verbose is always 0-5;
            // a dict size starts at 10 000+ so the ranges don't overlap.
            int maybeVerbose = br.ReadInt32();
            if (maybeVerbose <= 5)
                br.ReadBytes((int)br.ReadUInt32()); // pretrainedVectors path (ignored)
            else
                br.BaseStream.Seek(-4, SeekOrigin.Current); // was dict size — put back
        }
        else
        {
            // GlotLID format: dim starts at pos 8, extra nthreads field before t.
            br.BaseStream.Seek(8, SeekOrigin.Begin);
            dim = br.ReadInt32();
            br.ReadInt32();            // ws
            br.ReadInt32();            // epoch
            br.ReadInt32();            // minCount
            br.ReadInt32();            // neg
            br.ReadInt32();            // wordNgrams
            loss = br.ReadInt32();
            br.ReadInt32();            // model
            bucket = br.ReadInt32();
            minn = br.ReadInt32();
            maxn = br.ReadInt32();
            br.ReadInt32();            // nthreads (training-only)
            br.ReadDouble();           // t
        }

        const string labelPrefix = "__label__";

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

        // pruneidx_ in fastText only maps n-gram raw hashes (not words).
        // Words are always at row index equal to their position in the dictionary.
        var ngramPruneidx = new Dictionary<int, int>();
        for (long i = 0; i < pruneidxSize; i++)
        {
            int first = br.ReadInt32();
            int second = br.ReadInt32();
            ngramPruneidx[first] = second;
        }

        var wordToId = new Dictionary<string, int>(nwords, StringComparer.Ordinal);
        for (int i = 0; i < size; i++)
        {
            if (types[i] != 0) continue;
            wordToId[words[i]] = i; // row = position in dictionary (valid for both pruned and non-pruned)
        }

        var labelFlores = new List<string>(nlabels);
        var labelCounts = new List<long>(nlabels);
        for (int i = 0; i < size; i++)
        {
            if (types[i] == 1)
            {
                labelFlores.Add(MapToFlores(words[i], labelPrefix));
                labelCounts.Add(counts[i]);
            }
        }

        // --- Input matrix: quantized (.ftz) or dense (.bin) ---
        bool qinput = br.ReadBoolean();

        long inputM; float[][]? dense = null;
        int pqNsubq = 0, pqDsub = 0, pqLastDsub = 0;
        float[] pqCentroids = [];
        byte[] codes = [];
        bool qnorm; float[]? normCentroids = null; byte[]? normCodes = null;

        if (!qinput)
        {
            qnorm = false;
            inputM = br.ReadInt64();
            long inputN = br.ReadInt64();
            dense = new float[inputM][];
            for (long row = 0; row < inputM; row++)
            {
                var r = new float[inputN];
                for (long k = 0; k < inputN; k++)
                    r[k] = br.ReadSingle();
                dense[row] = r;
            }
        }
        else
        {
            // QMatrix::save order: qnorm, m, n, codesize, codes, then PQ metadata.
            qnorm = br.ReadBoolean();
            inputM = br.ReadInt64();
            br.ReadInt64();                          // inputN = dim
            int codesize = br.ReadInt32();
            codes = br.ReadBytes(codesize);          // codes before PQ metadata

            br.ReadInt32();                          // pqDimStored
            pqNsubq = br.ReadInt32();
            pqDsub = br.ReadInt32();
            pqLastDsub = br.ReadInt32();
            int centroidsCount = pqNsubq * KCENT * Math.Max(pqDsub, pqLastDsub);
            pqCentroids = new float[centroidsCount];
            for (int i = 0; i < centroidsCount; i++)
                pqCentroids[i] = br.ReadSingle();

            if (qnorm)
            {
                // QMatrix::save writes norm_codes before the norm PQ metadata.
                normCodes = br.ReadBytes((int)inputM);
                br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // norm PQ header
                normCentroids = new float[KCENT];
                for (int i = 0; i < KCENT; i++)
                    normCentroids[i] = br.ReadSingle();
            }
        }

        // --- Output matrix (full precision) ---
        br.ReadBoolean(); // output quant flag (always false)
        long outM = br.ReadInt64();
        long outN = br.ReadInt64();
        var output = new float[outM * outN];
        for (long i = 0; i < outM * outN; i++)
            output[i] = br.ReadSingle();

        // Build Huffman tree for hierarchical softmax models.
        // For softmax/ova/ns, simple argmax of dot products is used instead.
        HsNode[]? hsTree = (loss == LOSS_HS)
            ? BuildHuffmanTree(labelCounts.ToArray())
            : null;

        return new FastTextLanguageDetector(
            dim, minn, maxn, bucket, nwords, labelPrefix,
            wordToId,
            pqNsubq, pqDsub, pqLastDsub, pqCentroids, codes, inputM,
            qnorm, normCentroids, normCodes,
            dense,
            output, labelFlores.ToArray(),
            hsTree,
            pruneidxSize > 0 ? ngramPruneidx : null);
    }

    // -------------------------------------------------------------------------

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Language.EnglishLatin;

        var h = new float[_dim];
        int count = 0;

        foreach (var token in Tokenize(text))
            AddWordFeatures(token, h, ref count);

        if (count == 0)
            return Language.EnglishLatin;

        for (int i = 0; i < _dim; i++)
            h[i] /= count;

        return _hsTree != null ? PredictHs(h) : PredictArgmax(h);
    }

    // -------------------------------------------------------------------------

    // Prediction for hierarchical softmax (loss=hs): traverse the Huffman tree.
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

    private void AddWordFeatures(string word, float[] target, ref int count)
    {
        if (_wordToId.TryGetValue(word, out int wordRow))
        {
            AddRowEmbedding(wordRow, target);
            count++;
        }

        if (_maxn > 0)
        {
            var padded = Encoding.UTF8.GetBytes("<" + word + ">");
            int len = padded.Length;
            for (int i = 0; i < len; i++)
            {
                if ((padded[i] & 0xC0) == 0x80) continue;
                int start = i;
                int n = 0;
                for (int j = i; j < len && n < _maxn; )
                {
                    j++;
                    while (j < len && (padded[j] & 0xC0) == 0x80) j++;
                    n++;
                    if (n >= _minn)
                    {
                        uint h2 = FnvHash(padded.AsSpan(start, j - start)) % (uint)_bucket;
                        int rowIdx;
                        if (_ngramPruneidx != null)
                        {
                            if (!_ngramPruneidx.TryGetValue((int)h2, out int mapped))
                                continue; // n-gram not in pruned set
                            rowIdx = mapped + _nwords;
                        }
                        else
                        {
                            rowIdx = (int)h2 + _nwords;
                        }
                        if (rowIdx >= 0 && rowIdx < _inputRows)
                        {
                            AddRowEmbedding(rowIdx, target);
                            count++;
                        }
                    }
                }
            }
        }
    }

    private void AddRowEmbedding(int rowIdx, float[] target)
    {
        if (_dense != null)
        {
            float[] row = _dense[rowIdx];
            for (int k = 0; k < _dim; k++)
                target[k] += row[k];
            return;
        }

        int codeBase = rowIdx * _pqNsubq;
        int dimOffset = 0;

        for (int j = 0; j < _pqNsubq; j++)
        {
            int centroidIdx = _codes[codeBase + j];
            int dsub = (j == _pqNsubq - 1) ? _pqLastDsub : _pqDsub;
            int centBase = j < _pqNsubq - 1
                ? (j * KCENT + centroidIdx) * _pqDsub
                : (_pqNsubq - 1) * KCENT * _pqDsub + centroidIdx * _pqLastDsub;

            for (int d = 0; d < dsub; d++)
                target[dimOffset + d] += _pqCentroids[centBase + d];
            dimOffset += dsub;
        }

        if (_qnorm && _normCodes != null && _normCentroids != null)
        {
            float norm = _normCentroids[_normCodes[rowIdx]];
            dimOffset = 0;
            for (int j = 0; j < _pqNsubq; j++)
            {
                int centroidIdx = _codes[codeBase + j];
                int dsub = (j == _pqNsubq - 1) ? _pqLastDsub : _pqDsub;
                int centBase = j < _pqNsubq - 1
                    ? (j * KCENT + centroidIdx) * _pqDsub
                    : (_pqNsubq - 1) * KCENT * _pqDsub + centroidIdx * _pqLastDsub;
                for (int d = 0; d < dsub; d++)
                    target[dimOffset + d] += _pqCentroids[centBase + d] * (norm - 1f);
                dimOffset += dsub;
            }
        }
    }

    // -------------------------------------------------------------------------

    private static IEnumerable<string> Tokenize(string text)
        => text.Split(new[] { ' ', '\t', '\n', '\r', '\f', '\v' },
            StringSplitOptions.RemoveEmptyEntries);

    private static string ReadNullTerminated(BinaryReader br)
    {
        var sb = new StringBuilder();
        char c;
        while ((c = (char)br.ReadByte()) != '\0')
            sb.Append(c);
        return sb.ToString();
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

    private static string MapToFlores(string label, string prefix)
    {
        var iso = label.StartsWith(prefix, StringComparison.Ordinal) ? label[prefix.Length..] : label;
        return IsoToFlores.TryGetValue(iso, out var flores) ? flores : iso;
    }

    private static readonly Dictionary<string, string> IsoToFlores =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // --- ISO 639-1 (fastText LID-176) ---
            ["en"] = "eng_Latn", ["de"] = "deu_Latn", ["fr"] = "fra_Latn", ["es"] = "spa_Latn",
            ["it"] = "ita_Latn", ["pt"] = "por_Latn", ["nl"] = "nld_Latn", ["pl"] = "pol_Latn",
            ["cs"] = "ces_Latn", ["sk"] = "slk_Latn", ["sl"] = "slv_Latn", ["hu"] = "hun_Latn",
            ["ro"] = "ron_Latn", ["bg"] = "bul_Cyrl", ["hr"] = "hrv_Latn", ["sr"] = "srp_Cyrl",
            ["ru"] = "rus_Cyrl", ["uk"] = "ukr_Cyrl", ["be"] = "bel_Cyrl", ["mk"] = "mkd_Cyrl",
            ["bs"] = "bos_Latn", ["lt"] = "lit_Latn", ["lv"] = "lvs_Latn", ["et"] = "est_Latn",
            ["fi"] = "fin_Latn", ["sv"] = "swe_Latn", ["no"] = "nob_Latn", ["da"] = "dan_Latn",
            ["tr"] = "tur_Latn", ["az"] = "azj_Latn", ["kk"] = "kaz_Cyrl", ["ky"] = "kir_Cyrl",
            ["uz"] = "uzn_Latn", ["tg"] = "tgk_Cyrl", ["ar"] = "arb_Arab", ["fa"] = "pes_Arab",
            ["ur"] = "urd_Arab", ["hi"] = "hin_Deva", ["bn"] = "ben_Beng", ["mr"] = "mar_Deva",
            ["ta"] = "tam_Taml", ["te"] = "tel_Telu", ["ml"] = "mal_Mlym", ["kn"] = "kan_Knda",
            ["gu"] = "guj_Gujr", ["pa"] = "pan_Guru", ["ne"] = "npi_Deva", ["si"] = "sin_Sinh",
            ["zh"] = "zho_Hans", ["ja"] = "jpn_Jpan", ["ko"] = "kor_Hang", ["vi"] = "vie_Latn",
            ["th"] = "tha_Thai", ["km"] = "khm_Khmr", ["lo"] = "lao_Laoo", ["my"] = "mya_Mymr",
            ["ka"] = "kat_Geor", ["hy"] = "hye_Armn", ["he"] = "heb_Hebr", ["id"] = "ind_Latn",
            ["ms"] = "zsm_Latn", ["tl"] = "tgl_Latn", ["sw"] = "swh_Latn", ["cy"] = "cym_Latn",
            ["eu"] = "eus_Latn", ["gl"] = "glg_Latn", ["ca"] = "cat_Latn", ["af"] = "afr_Latn",
            ["is"] = "isl_Latn", ["mt"] = "mlt_Latn", ["sq"] = "als_Latn", ["mn"] = "khk_Cyrl",
            ["jv"] = "jav_Latn", ["su"] = "sun_Latn", ["mg"] = "plt_Latn", ["eo"] = "epo_Latn",

            // --- ISO 639-3 (GlotLID) ---
            ["eng"] = "eng_Latn", ["deu"] = "deu_Latn", ["fra"] = "fra_Latn", ["spa"] = "spa_Latn",
            ["ita"] = "ita_Latn", ["por"] = "por_Latn", ["nld"] = "nld_Latn", ["pol"] = "pol_Latn",
            ["ces"] = "ces_Latn", ["slk"] = "slk_Latn", ["slv"] = "slv_Latn", ["hun"] = "hun_Latn",
            ["ron"] = "ron_Latn", ["bul"] = "bul_Cyrl", ["hrv"] = "hrv_Latn", ["srp"] = "srp_Cyrl",
            ["rus"] = "rus_Cyrl", ["ukr"] = "ukr_Cyrl", ["bel"] = "bel_Cyrl", ["mkd"] = "mkd_Cyrl",
            ["bos"] = "bos_Latn", ["lit"] = "lit_Latn", ["lav"] = "lvs_Latn", ["est"] = "est_Latn",
            ["fin"] = "fin_Latn", ["swe"] = "swe_Latn", ["nob"] = "nob_Latn", ["nno"] = "nno_Latn",
            ["dan"] = "dan_Latn", ["tur"] = "tur_Latn", ["aze"] = "azj_Latn", ["kaz"] = "kaz_Cyrl",
            ["kir"] = "kir_Cyrl", ["uzb"] = "uzn_Latn", ["tgk"] = "tgk_Cyrl", ["ara"] = "arb_Arab",
            ["fas"] = "pes_Arab", ["urd"] = "urd_Arab", ["hin"] = "hin_Deva", ["ben"] = "ben_Beng",
            ["mar"] = "mar_Deva", ["tam"] = "tam_Taml", ["tel"] = "tel_Telu", ["mal"] = "mal_Mlym",
            ["kan"] = "kan_Knda", ["guj"] = "guj_Gujr", ["pan"] = "pan_Guru", ["nep"] = "npi_Deva",
            ["sin"] = "sin_Sinh", ["zho"] = "zho_Hans", ["cmn"] = "zho_Hans", ["yue"] = "yue_Hant",
            ["jpn"] = "jpn_Jpan", ["kor"] = "kor_Hang", ["vie"] = "vie_Latn", ["tha"] = "tha_Thai",
            ["khm"] = "khm_Khmr", ["lao"] = "lao_Laoo", ["mya"] = "mya_Mymr", ["kat"] = "kat_Geor",
            ["hye"] = "hye_Armn", ["heb"] = "heb_Hebr", ["ind"] = "ind_Latn", ["zsm"] = "zsm_Latn",
            ["msa"] = "zsm_Latn", ["tgl"] = "tgl_Latn", ["swa"] = "swh_Latn", ["cym"] = "cym_Latn",
            ["eus"] = "eus_Latn", ["glg"] = "glg_Latn", ["cat"] = "cat_Latn", ["afr"] = "afr_Latn",
            ["isl"] = "isl_Latn", ["mlt"] = "mlt_Latn", ["sqi"] = "als_Latn", ["khk"] = "khk_Cyrl",
            ["mon"] = "khk_Cyrl", ["jav"] = "jav_Latn", ["sun"] = "sun_Latn", ["mlg"] = "plt_Latn",
            ["epo"] = "epo_Latn", ["swh"] = "swh_Latn", ["lvs"] = "lvs_Latn", ["npi"] = "npi_Deva",
        };
}
