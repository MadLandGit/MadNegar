using NAudio.Wave;

namespace MadNegar;

/// <summary>Temporary PCM WAV chunking for multilingual Whisper auto-detection.</summary>
internal static class AutoLanguageTranscription
{
    public const int ChunkSeconds = 8;
    public const int OverlapMilliseconds = 600;
    public const int MinimumDurationForSegmentationSeconds = 12;

    internal sealed record WaveSegment(string Path, bool IsTemporary);

    public static IReadOnlyList<WaveSegment> SplitWaveFile(
        string sourcePath, string outputDirectory, CancellationToken cancellationToken = default,
        int chunkSeconds = ChunkSeconds, int overlapMilliseconds = OverlapMilliseconds,
        int minimumDurationSeconds = MinimumDurationForSegmentationSeconds)
    {
        using var reader = new WaveFileReader(sourcePath);
        var format = reader.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1)
            return [new WaveSegment(sourcePath, false)];

        var totalBytes = reader.Length;
        var totalFrames = totalBytes / Math.Max(1, format.BlockAlign);
        var sampleRate = format.SampleRate;
        chunkSeconds = Math.Clamp(chunkSeconds, 2, 30);
        overlapMilliseconds = Math.Clamp(overlapMilliseconds, 0, Math.Max(0, chunkSeconds * 1000 - 100));
        minimumDurationSeconds = Math.Clamp(minimumDurationSeconds, 0, 120);
        var chunkFrames = (long)sampleRate * chunkSeconds;
        var minDurationFrames = (long)sampleRate * minimumDurationSeconds;
        if (totalFrames <= minDurationFrames || totalFrames <= chunkFrames)
            return [new WaveSegment(sourcePath, false)];

        Directory.CreateDirectory(outputDirectory);
        var overlapFrames = Math.Max(0, (long)sampleRate * overlapMilliseconds / 1000);
        var stepFrames = Math.Max(1, chunkFrames - overlapFrames);
        var blockAlign = Math.Max(1, format.BlockAlign);
        var segments = new List<WaveSegment>();
        long startFrame = 0;
        var buffer = new byte[64 * 1024];

        try
        {
            while (startFrame < totalFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frameCount = Math.Min(chunkFrames, totalFrames - startFrame);
                if (frameCount <= 0) break;
                var segmentPath = Path.Combine(outputDirectory, $"segment-{segments.Count + 1:000}.wav");
                reader.Position = startFrame * blockAlign;
                var bytesLeft = checked((int)(frameCount * blockAlign));
                using (var writer = new WaveFileWriter(segmentPath, format))
                {
                    while (bytesLeft > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var request = Math.Min(buffer.Length, bytesLeft);
                        request -= request % blockAlign;
                        if (request <= 0) request = Math.Min(blockAlign, bytesLeft);
                        var read = reader.Read(buffer, 0, request);
                        if (read <= 0) break;
                        writer.Write(buffer, 0, read);
                        bytesLeft -= read;
                    }
                }

                if (bytesLeft > 0)
                    throw new EndOfStreamException("The audio file ended while a language-detection segment was being copied.");

                segments.Add(new WaveSegment(segmentPath, true));
                if (startFrame + frameCount >= totalFrames) break;
                startFrame += stepFrames;
            }

            return segments.Count == 0 ? [new WaveSegment(sourcePath, false)] : segments;
        }
        catch
        {
            foreach (var segment in segments)
            {
                if (!segment.IsTemporary) continue;
                try { if (File.Exists(segment.Path)) File.Delete(segment.Path); } catch { }
            }
            throw;
        }
    }
}
