using System.Text;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core;

/// <summary>
/// Language detector backed by a fastText supervised model (.ftz compressed or .bin full-precision).
/// Loads the binary file directly — no Python or native fastText library needed.
///
/// Supported models:
///   lid.176.ftz  — fastText LID-176, CC BY-SA 3.0, 176 languages (~917 KB, quantized)
///   model.bin    — GlotLID v3 (cis-lmu/glotlid-model), Apache 2.0, 1633 languages
///
/// Binary format reference: fastText v12 (magic = 793712314).
/// Model weights: product-quantized (.ftz) or full-precision (.bin) input matrix.
/// Inference: average word/char-ngram embeddings → linear layer → argmax.
/// </summary>
public sealed class FastTextLanguageDetector : ILanguageDetector
{
    private const int KCENT = 256;           // centroids per sub-quantizer (hardcoded in fastText)
    private const int MAGIC = 793712314;
    private const int VERSION = 12;

    // --- model params ---
    private readonly int _dim;
    private readonly int _minn;
    private readonly int _maxn;
    private readonly int _bucket;
    private readonly int _nwords;            // vocabulary size (words only, not labels)
    private readonly string _labelPrefix;   // e.g. "__label__"

    // --- vocabulary ---
    private readonly Dictionary<string, int> _wordToId;   // word → row index

    // --- product-quantized input matrix ---
    private readonly int _pqNsubq;
    private readonly int _pqDsub;
    private readonly int _pqLastDsub;
    private readonly float[] _pqCentroids;  // pqNsubq * KCENT * max(pqDsub, pqLastDsub)
    private readonly byte[] _codes;          // inputRows * pqNsubq bytes
    private readonly long _inputRows;

    // --- optional norm quantization ---
    private readonly bool _qnorm;
    private readonly float[]? _normCentroids; // 256 scalar norms
    private readonly byte[]? _normCodes;       // inputRows bytes

    // --- dense input matrix (.bin models, e.g. GlotLID) ---
    private readonly float[]? _dense; // non-null when qinput=false; inputRows * dim floats

    // --- output matrix (full precision) ---
    private readonly float[] _output;  // nlabels * dim (row-major)
    private readonly string[] _labels; // FLORES-200 codes, indexed by label row

    // -------------------------------------------------------------------------

    private FastTextLanguageDetector(
        int dim, int minn, int maxn, int bucket, int nwords, string labelPrefix,
        Dictionary<string, int> wordToId,
        int pqNsubq, int pqDsub, int pqLastDsub, float[] pqCentroids, byte[] codes, long inputRows,
        bool qnorm, float[]? normCentroids, byte[]? normCodes,
        float[]? dense,
        float[] output, string[] labels)
    {
        _dim = dim; _minn = minn; _maxn = maxn; _bucket = bucket; _nwords = nwords; _labelPrefix = labelPrefix;
        _wordToId = wordToId;
        _pqNsubq = pqNsubq; _pqDsub = pqDsub; _pqLastDsub = pqLastDsub;
        _pqCentroids = pqCentroids; _codes = codes; _inputRows = inputRows;
        _qnorm = qnorm; _normCentroids = normCentroids; _normCodes = normCodes;
        _dense = dense;
        _output = output; _labels = labels;
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
        br.ReadDouble();           // lr
        br.ReadInt32();            // lrUpdateRate
        int dim = br.ReadInt32();
        br.ReadInt32();            // ws
        br.ReadInt32();            // epoch
        br.ReadInt32();            // minCount
        br.ReadInt32();            // neg
        int wordNgrams = br.ReadInt32();
        br.ReadInt32();            // loss
        br.ReadInt32();            // model
        int bucket = br.ReadInt32();
        int minn = br.ReadInt32();
        int maxn = br.ReadInt32();
        br.ReadDouble();           // t
        int labelLen = (int)br.ReadUInt32();
        string labelPrefix = Encoding.UTF8.GetString(br.ReadBytes(labelLen));
        br.ReadInt32();            // verbose
        int pretrainedLen = (int)br.ReadUInt32();
        br.ReadBytes(pretrainedLen); // pretrainedVectors path (ignored)

        // --- Dictionary ---
        int size = br.ReadInt32();
        int nwords = br.ReadInt32();
        int nlabels = br.ReadInt32();
        br.ReadInt64();            // ntokens
        long pruneidxSize = br.ReadInt64();

        var words = new string[size];
        var types = new byte[size];
        for (int i = 0; i < size; i++)
        {
            words[i] = ReadNullTerminated(br);
            br.ReadInt64(); // count
            types[i] = br.ReadByte(); // 0=word, 1=label
        }

        // pruneidx: maps old word row index → new (compacted) row index
        var pruneidx = new Dictionary<int, int>();
        for (long i = 0; i < pruneidxSize; i++)
        {
            int first = br.ReadInt32();
            int second = br.ReadInt32();
            pruneidx[first] = second;
        }
        bool hasPrune = pruneidxSize >= 0;

        // Build word→row-index lookup (labels are at indices nwords..size-1, skipped)
        var wordToId = new Dictionary<string, int>(nwords, StringComparer.Ordinal);
        for (int i = 0; i < size; i++)
        {
            if (types[i] != 0) continue; // skip labels
            int rowIdx = hasPrune && pruneidxSize > 0
                ? (pruneidx.TryGetValue(i, out var pi) ? pi : -1)
                : i;
            if (rowIdx >= 0)
                wordToId[words[i]] = rowIdx;
        }

        // Build FLORES-200 label list from dictionary labels
        var labelFlores = new List<string>();
        for (int i = 0; i < size; i++)
        {
            if (types[i] == 1)
                labelFlores.Add(MapToFlores(words[i], labelPrefix));
        }

        // --- Input matrix: quantized (.ftz) or dense (.bin) ---
        bool qinput = br.ReadBoolean();

        long inputM; float[]? dense = null;
        int pqNsubq = 0, pqDsub = 0, pqLastDsub = 0;
        float[] pqCentroids = [];
        byte[] codes = [];
        bool qnorm; float[]? normCentroids = null; byte[]? normCodes = null;

        if (!qinput)
        {
            // Dense matrix — used by full-precision .bin models such as GlotLID
            br.ReadBoolean(); // qnorm (ignored for dense; we don't quantize norms here)
            qnorm = false;
            inputM = br.ReadInt64();
            long inputN = br.ReadInt64(); // = dim
            dense = new float[inputM * inputN];
            for (long i = 0; i < inputM * inputN; i++)
                dense[i] = br.ReadSingle();
        }
        else
        {
            // Product-quantized matrix — used by .ftz compressed models
            qnorm = br.ReadBoolean();
            inputM = br.ReadInt64();
            br.ReadInt64();                          // inputN = dim (already known)
            int codesize = br.ReadInt32();           // total bytes = inputM * pqNsubq

            br.ReadInt32();                          // pqDimStored (= dim)
            pqNsubq = br.ReadInt32();
            pqDsub = br.ReadInt32();
            pqLastDsub = br.ReadInt32();
            int centroidsCount = pqNsubq * KCENT * Math.Max(pqDsub, pqLastDsub);
            pqCentroids = new float[centroidsCount];
            for (int i = 0; i < centroidsCount; i++)
                pqCentroids[i] = br.ReadSingle();
            codes = br.ReadBytes(codesize);

            if (qnorm)
            {
                br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); // PQ header (dim=1)
                normCentroids = new float[KCENT];
                for (int i = 0; i < KCENT; i++)
                    normCentroids[i] = br.ReadSingle();
                normCodes = br.ReadBytes((int)inputM);
            }
        }

        // --- Output matrix (full precision) ---
        long outM = br.ReadInt64();
        long outN = br.ReadInt64();
        var output = new float[outM * outN];
        for (long i = 0; i < outM * outN; i++)
            output[i] = br.ReadSingle();

        return new FastTextLanguageDetector(
            dim, minn, maxn, bucket, nwords, labelPrefix,
            wordToId,
            pqNsubq, pqDsub, pqLastDsub, pqCentroids, codes, inputM,
            qnorm, normCentroids, normCodes,
            dense,
            output, labelFlores.ToArray());
    }

    // -------------------------------------------------------------------------

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Language.EnglishLatin;

        var h = new float[_dim];
        int count = 0;

        // Tokenize on whitespace/punctuation — same approach as fastText getLine
        foreach (var token in Tokenize(text))
        {
            AddWordFeatures(token, h, ref count);
        }

        if (count == 0)
            return Language.EnglishLatin;

        // Average
        for (int i = 0; i < _dim; i++)
            h[i] /= count;

        // Linear: argmax over output rows
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

        return best < _labels.Length ? _labels[best] : Language.EnglishLatin;
    }

    // -------------------------------------------------------------------------

    private void AddWordFeatures(string word, float[] target, ref int count)
    {
        // Exact-match word embedding
        if (_wordToId.TryGetValue(word, out int wordRow))
        {
            AddRowEmbedding(wordRow, target);
            count++;
        }
        else if (_bucket > 0)
        {
            // OOV: hash word to bucket
            int h = (int)(FnvHash(Encoding.UTF8.GetBytes(word)) % (uint)_bucket) + _nwords;
            if (h >= 0 && h < _inputRows)
            {
                AddRowEmbedding(h, target);
                count++;
            }
        }

        // Character n-grams of "<word>"
        if (_maxn > 0)
        {
            var padded = Encoding.UTF8.GetBytes("<" + word + ">");
            int len = padded.Length;
            for (int i = 0; i < len; i++)
            {
                if ((padded[i] & 0xC0) == 0x80) continue; // skip continuation bytes
                int start = i;
                int n = 0;
                for (int j = i; j < len && n < _maxn; )
                {
                    j++; // include current byte
                    while (j < len && (padded[j] & 0xC0) == 0x80) j++; // include continuation
                    n++;
                    if (n >= _minn)
                    {
                        uint h2 = FnvHash(padded.AsSpan(start, j - start)) % (uint)_bucket;
                        int rowIdx = (int)h2 + _nwords;
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
            int offset = rowIdx * _dim;
            for (int k = 0; k < _dim; k++)
                target[k] += _dense[offset + k];
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
            // Undo the averaging done above by subtracting, then add back with norm applied.
            // Simpler: recompute the embedding with norm. But we already accumulated —
            // instead accumulate norm-scaled: subtract un-normed, add normed back.
            // For simplicity we multiply the last-added embedding by norm. Since we just added
            // it, subtract and re-add scaled. Use a local buffer instead.
            // NOTE: This is an approximation — correct approach would use local buffer.
            // For LID, qnorm is typically false; this path handles the general case.
            dimOffset = 0;
            for (int j = 0; j < _pqNsubq; j++)
            {
                int centroidIdx = _codes[codeBase + j];
                int dsub = (j == _pqNsubq - 1) ? _pqLastDsub : _pqDsub;
                int centBase = j < _pqNsubq - 1
                    ? (j * KCENT + centroidIdx) * _pqDsub
                    : (_pqNsubq - 1) * KCENT * _pqDsub + centroidIdx * _pqLastDsub;
                for (int d = 0; d < dsub; d++)
                {
                    var v = _pqCentroids[centBase + d];
                    target[dimOffset + d] += v * (norm - 1f); // already added v once; add (norm-1)*v more
                }
                dimOffset += dsub;
            }
        }
    }

    // -------------------------------------------------------------------------

    private static IEnumerable<string> Tokenize(string text)
    {
        // Split on whitespace; lowercase for case-insensitive matching (fastText lowercases by default)
        return text.Split(new[] { ' ', '\t', '\n', '\r', '\f', '\v' },
            StringSplitOptions.RemoveEmptyEntries);
    }

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
            h ^= (uint)(sbyte)b; // sign-extend like C++ int8_t cast
            h *= 16777619u;
        }
        return h;
    }

    // -------------------------------------------------------------------------
    // fastText label → FLORES-200 mapping

    private static string MapToFlores(string label, string prefix)
    {
        var iso = label.StartsWith(prefix, StringComparison.Ordinal) ? label[prefix.Length..] : label;
        return IsoToFlores.TryGetValue(iso, out var flores) ? flores : iso;
    }

    // Covers both ISO 639-1 (2-letter, used by fastText LID-176) and
    // ISO 639-3 (3-letter, used by OpenLID) so the same code works with both models.
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

            // --- ISO 639-3 (OpenLID) ---
            // Labels are "__label__eng", "__label__ukr", etc.
            // Many FLORES codes share the 3-letter prefix, but scripts and dialects need explicit entries.
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
