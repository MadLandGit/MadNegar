using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MadNegar;

/// <summary>
/// Converts the actual WASAPI mix-format microphone recording into the mono 16 kHz
/// PCM WAV expected by whisper.cpp. The audio never leaves this computer.
/// </summary>
internal static class AudioProcessor
{
    private const int WhisperSampleRate = 16000;
    private const float HighPassCutoffHz = 80f;
    private const double TargetRmsDb = -22d;

    public static void PrepareForWhisper(
        string sourcePath,
        string destinationPath,
        int microphoneBoostDb,
        bool autoLevel,
        bool reduceLowRumble,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AudioStats stats;
        using (var reader = new AudioFileReader(sourcePath))
        {
            var provider = CreateMono16kProvider(reader, reduceLowRumble);
            stats = Measure(provider, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var autoGainDb = 0d;
        if (autoLevel && stats.Rms > 0.000001)
        {
            var rmsDb = 20d * Math.Log10(stats.Rms);
            autoGainDb = Math.Clamp(TargetRmsDb - rmsDb, -12d, 18d);
        }

        var totalGainDb = microphoneBoostDb + autoGainDb;
        if (stats.Peak > 0.000001)
        {
            // Keep headroom for natural speech peaks instead of allowing boosted audio to clip.
            var peakSafeDb = 20d * Math.Log10(0.94d / stats.Peak);
            totalGainDb = Math.Min(totalGainDb, peakSafeDb);
        }
        totalGainDb = Math.Clamp(totalGainDb, -36d, 36d);
        var gain = (float)Math.Pow(10d, totalGainDb / 20d);

        using var secondReader = new AudioFileReader(sourcePath);
        var outputProvider = CreateMono16kProvider(secondReader, reduceLowRumble);
        outputProvider = new GainLimiterSampleProvider(outputProvider, gain);
        outputProvider = new CancellationAwareSampleProvider(outputProvider, cancellationToken);
        WaveFileWriter.CreateWaveFile16(destinationPath, outputProvider);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    public static bool TryMeasurePeak(byte[] buffer, int bytesRecorded, WaveFormat format, int gainDb, out float measuredPeak) =>
        TryMeasurePeakAndRms(buffer, bytesRecorded, format, gainDb, out measuredPeak, out _);

    public static bool TryMeasurePeakAndRms(byte[] buffer, int bytesRecorded, WaveFormat format, int gainDb,
        out float measuredPeak, out float measuredRms)
    {
        measuredPeak = 0f;
        measuredRms = 0f;
        if (bytesRecorded <= 0) return true;
        if (buffer is null || format is null || bytesRecorded > buffer.Length) return false;

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var isPcm = format.Encoding == WaveFormatEncoding.Pcm;
        if (format.Encoding == WaveFormatEncoding.Extensible)
        {
            // WAVE_FORMAT_EXTENSIBLE uses the subtype GUID, not Encoding, to distinguish
            // 32-bit IEEE float from integer PCM. WASAPI can expose either the typed format
            // or the generic extra-data representation depending on the capture path.
            Guid subFormat;
            if (format is WaveFormatExtensible extensible)
                subFormat = extensible.SubFormat;
            else if (format is WaveFormatExtraData extra && extra.ExtraSize >= 22 && extra.ExtraData.Length >= 22)
                subFormat = new Guid(extra.ExtraData.AsSpan(6, 16));
            else
                return false;

            isFloat = subFormat == IeeeFloatSubFormat;
            isPcm = subFormat == PcmSubFormat;
        }

        var bits = format.BitsPerSample;
        var gain = (float)Math.Pow(10d, Math.Clamp(gainDb, 0, 30) / 20d);
        double sumSquares = 0d;
        long sampleCount = 0;
        var peakLocal = 0f;
        void AddSample(float value)
        {
            if (!float.IsFinite(value)) value = 0f;
            value = Math.Clamp(value * gain, -1f, 1f);
            var absolute = Math.Abs(value);
            if (absolute > peakLocal) peakLocal = absolute;
            sumSquares += value * value;
            sampleCount++;
        }

        if (isFloat)
        {
            if (bits != 32) return false;
            for (var i = 0; i + 3 < bytesRecorded; i += 4)
                AddSample(BitConverter.ToSingle(buffer, i));
        }
        else if (isPcm)
        {
            switch (bits)
            {
                case 8:
                    for (var i = 0; i < bytesRecorded; i++) AddSample((buffer[i] - 128) / 128f);
                    break;
                case 16:
                    for (var i = 0; i + 1 < bytesRecorded; i += 2)
                        AddSample(BitConverter.ToInt16(buffer, i) / 32768f);
                    break;
                case 24:
                    for (var i = 0; i + 2 < bytesRecorded; i += 3)
                    {
                        var value = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
                        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                        AddSample(value / 8388608f);
                    }
                    break;
                case 32:
                    for (var i = 0; i + 3 < bytesRecorded; i += 4)
                        AddSample(BitConverter.ToInt32(buffer, i) / 2147483648f);
                    break;
                default:
                    return false;
            }
        }
        else
        {
            // Unknown compressed/subtyped formats are not PCM by assumption.
            return false;
        }

        measuredPeak = peakLocal;
        measuredRms = sampleCount == 0 ? 0f : (float)Math.Sqrt(sumSquares / sampleCount);
        return true;
    }

    private static ISampleProvider CreateMono16kProvider(AudioFileReader reader, bool reduceLowRumble)
    {
        ISampleProvider provider = reader;
        if (provider.WaveFormat.Channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }
        else if (provider.WaveFormat.Channels > 2)
        {
            provider = new MultiChannelToMonoSampleProvider(provider);
        }

        if (provider.WaveFormat.SampleRate != WhisperSampleRate)
            provider = new WdlResamplingSampleProvider(provider, WhisperSampleRate);

        if (reduceLowRumble)
            provider = new HighPassSampleProvider(provider, HighPassCutoffHz);
        return provider;
    }

    private static AudioStats Measure(ISampleProvider provider, CancellationToken cancellationToken)
    {
        var buffer = new float[8192];
        long count = 0;
        double sumSquares = 0;
        var peak = 0f;
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i++)
            {
                var sample = float.IsFinite(buffer[i]) ? buffer[i] : 0f;
                sumSquares += sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }
            count += read;
        }

        return new AudioStats(count == 0 ? 0 : Math.Sqrt(sumSquares / count), peak);
    }

    private readonly record struct AudioStats(double Rms, float Peak);

    private sealed class CancellationAwareSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly CancellationToken _cancellationToken;

        public CancellationAwareSampleProvider(ISampleProvider source, CancellationToken cancellationToken)
        {
            _source = source;
            _cancellationToken = cancellationToken;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _source.Read(buffer, offset, count);
        }
    }

    private sealed class GainLimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _gain;

        public GainLimiterSampleProvider(ISampleProvider source, float gain)
        {
            _source = source;
            _gain = gain;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            var end = offset + read;
            for (var i = offset; i < end; i++)
            {
                var value = buffer[i] * _gain;
                buffer[i] = Math.Clamp(float.IsFinite(value) ? value : 0f, -0.98f, 0.98f);
            }
            return read;
        }
    }

    private sealed class HighPassSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _alpha;
        private float _previousInput;
        private float _previousOutput;

        public HighPassSampleProvider(ISampleProvider source, float cutoffHz)
        {
            _source = source;
            _alpha = (float)Math.Exp(-2d * Math.PI * cutoffHz / source.WaveFormat.SampleRate);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            var end = offset + read;
            for (var i = offset; i < end; i++)
            {
                var input = buffer[i];
                var output = _alpha * (_previousOutput + input - _previousInput);
                _previousInput = input;
                _previousOutput = output;
                buffer[i] = output;
            }
            return read;
        }
    }

    private sealed class MultiChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;

        public MultiChannelToMonoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var temp = new float[count * _channels];
            var read = _source.Read(temp, 0, temp.Length);
            var frames = read / _channels;
            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                var baseIndex = frame * _channels;
                for (var channel = 0; channel < _channels; channel++)
                    sum += temp[baseIndex + channel];
                buffer[offset + frame] = (float)(sum / _channels);
            }
            return frames;
        }
    }
}
