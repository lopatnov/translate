using System.Diagnostics;
using System.Text;

namespace Lopatnov.Translate.Piper;

/// <summary>
/// Converts text to IPA phoneme sequences by invoking the <c>espeak-ng</c>
/// command-line tool as a subprocess.
///
/// <para>
/// Piper TTS voices with <c>"phoneme_type": "espeak"</c> require espeak-ng
/// phonemisation to convert input text into IPA characters, which are then
/// mapped to integer IDs via the <c>phoneme_id_map</c> from the voice config.
/// </para>
///
/// <para>
/// <b>Dependency:</b> <c>espeak-ng</c> must be installed and on the system PATH.
/// On Debian/Ubuntu: <c>apt-get install -y espeak-ng</c>.
/// On Windows: install from <c>https://github.com/espeak-ng/espeak-ng/releases</c>.
/// </para>
/// </summary>
internal static class EspeakPhonemizer
{
    /// <summary>
    /// Phonemises <paramref name="text"/> using espeak-ng with the given voice
    /// and returns a flat IPA string (whitespace preserved as word boundaries).
    /// </summary>
    /// <param name="text">Input text (UTF-8).</param>
    /// <param name="espeakVoice">espeak-ng voice name, e.g. "en-us", "ru", "uk".</param>
    /// <param name="cancellationToken">Propagated to the async wait.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when espeak-ng is not found on PATH or exits with a non-zero code.
    /// </exception>
    internal static async Task<string> PhonemizeAsync(
        string text,
        string espeakVoice,
        CancellationToken cancellationToken = default)
    {
        // Write text to a temp file in UTF-8 (no BOM) and pass it via the -f flag.
        // This avoids a Windows-specific issue where the .NET Process stdin pipe
        // may silently mis-encode non-ASCII characters (Cyrillic, etc.) regardless
        // of StandardInputEncoding, causing espeak-ng to fall back to English
        // phonemisation or produce garbled IPA output.
        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmpFile, text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = "espeak-ng",
                    // --ipa : output IPA phonemes
                    // -q    : quiet (no audio output)
                    // -v    : voice name
                    // -f    : read input from file (bypasses stdin encoding issues)
                    Arguments              = $"--ipa -q -v {espeakVoice} -f \"{tmpFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    StandardErrorEncoding  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                },
                EnableRaisingEvents = false,
            };

            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
            {
                throw new InvalidOperationException(
                    "espeak-ng was not found. " +
                    "Install it via 'apt-get install -y espeak-ng' (Linux/Docker) or " +
                    "from https://github.com/espeak-ng/espeak-ng/releases (Windows).", ex);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"espeak-ng exited with code {process.ExitCode}. stderr: {stderr.Trim()}");

            return stdout;
        }
        finally
        {
            try { File.Delete(tmpFile); } catch { /* best-effort cleanup */ }
        }
    }
}
