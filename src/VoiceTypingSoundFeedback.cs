using NAudio.Wave;

namespace MadNegar;

/// <summary>
/// Plays short, application-generated PCM tones. No Windows toast/balloon is used for
/// the global voice-typing start/stop feedback.
/// </summary>
internal static class VoiceTypingSoundFeedback
{
    private const int SampleRate = 22050;

    public static void PlayStart() => PlayTone([(740, 75), (0, 24), (1046, 100)]);

    public static void PlayStop() => PlayTone([(1046, 70), (0, 24), (622, 115)]);

    private static void PlayTone((int Frequency, int DurationMs)[] notes)
    {
        MemoryStream? memory = null;
        RawSourceWaveStream? stream = null;
        WaveOutEvent? output = null;
        var cleanupStarted = 0;

        void Cleanup()
        {
            if (Interlocked.Exchange(ref cleanupStarted, 1) != 0) return;
            try { output?.Dispose(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { memory?.Dispose(); } catch { }
        }

        try
        {
            memory = new MemoryStream(CreatePcm(notes), writable: false);
            stream = new RawSourceWaveStream(memory, new WaveFormat(SampleRate, 16, 1));
            output = new WaveOutEvent { DesiredLatency = 80 };
            output.PlaybackStopped += (_, _) => Cleanup();
            output.Init(stream);
            output.Play();
        }
        catch
        {
            Cleanup();
        }
    }

    private static byte[] CreatePcm((int Frequency, int DurationMs)[] notes)
    {
        const int bytesPerSample = 2;
        const double amplitude = 0.18;
        var totalSamples = notes.Sum(note => SampleRate * note.DurationMs / 1000);
        var result = new byte[totalSamples * bytesPerSample];
        var sampleIndex = 0;

        foreach (var (frequency, durationMs) in notes)
        {
            var sampleCount = SampleRate * durationMs / 1000;
            for (var i = 0; i < sampleCount; i++, sampleIndex++)
            {
                double sample = 0;
                if (frequency > 0)
                {
                    var attack = Math.Min(1.0, i / Math.Max(1.0, SampleRate * 0.008));
                    var release = Math.Min(1.0, (sampleCount - i - 1) / Math.Max(1.0, SampleRate * 0.015));
                    var envelope = Math.Max(0, Math.Min(attack, release));
                    sample = amplitude * envelope * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate);
                }

                var pcm = (short)Math.Clamp((int)Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
                var offset = sampleIndex * bytesPerSample;
                result[offset] = (byte)(pcm & 0xFF);
                result[offset + 1] = (byte)((pcm >> 8) & 0xFF);
            }
        }

        return result;
    }
}
