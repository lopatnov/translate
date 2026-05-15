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
        // --ipa : output IPA phonemes
        // -q    : quiet mode (no audio, only text output)
        // -v    : voice name
        // Text is piped via stdin to avoid argument-length limits and shell injection.
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = "espeak-ng",
                Arguments              = $"--ipa -q -v {espeakVoice}",
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                StandardInputEncoding  = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
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

        // Write input text to stdin and close it so espeak-ng sees EOF.
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();

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
}
