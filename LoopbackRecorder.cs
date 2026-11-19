using NAudio.Wave;

namespace LiveTranscriber;

public sealed class LoopbackRecorder : IDisposable
{
    private const int TargetSampleRate = 16000;
    private const int WavHeaderSize = 44;
    private const float SpeechThreshold = 0.01f;
    private static readonly TimeSpan PauseToFlush = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan MaxSegment = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxSilence = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(400);

    private readonly WasapiLoopbackCapture capture = new();
    private readonly WaveFormat format;
    private readonly Action<byte[]> onSegment;
    private readonly List<float> segment = [];
    private readonly Lock gate = new();
    private readonly Timer timer;
    private DateTime lastSpeech;
    private bool hasSpeech;
    private double speechSeconds;
    private volatile bool paused;

    public LoopbackRecorder(Action<byte[]> onSegment)
    {
        this.onSegment = onSegment;
        format = capture.WaveFormat;

        if (format.BitsPerSample is not (16 or 32))
        {
            throw new NotSupportedException($"Формат звуку {format} не підтримується");
        }

        capture.DataAvailable += OnDataAvailable;
        timer = new Timer(_ => CheckSegment(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Format => $"{format.SampleRate} Hz, {format.Channels} ch";

    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;

            if (value)
            {
                lock (gate)
                {
                    segment.Clear();
                    hasSpeech = false;
                    speechSeconds = 0;
                }
            }
        }
    }

    public void Start()
    {
        capture.StartRecording();
        timer.Change(200, 200);
    }

    public void Dispose()
    {
        timer.Dispose();
        capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (paused)
        {
            return;
        }

        var samples = ToMono(e.Buffer, e.BytesRecorded);

        lock (gate)
        {
            segment.AddRange(samples);

            if (Rms(samples) > SpeechThreshold)
            {
                hasSpeech = true;
                lastSpeech = DateTime.UtcNow;
                speechSeconds += (double)samples.Length / format.SampleRate;
            }
        }
    }

    private void CheckSegment()
    {
        float[] ready;

        lock (gate)
        {
            if (segment.Count == 0)
            {
                return;
            }

            var duration = TimeSpan.FromSeconds((double)segment.Count / format.SampleRate);

            if (!hasSpeech)
            {
                if (duration > MaxSilence)
                {
                    segment.Clear();
                }

                return;
            }

            if (DateTime.UtcNow - lastSpeech < PauseToFlush && duration < MaxSegment)
            {
                return;
            }

            ready = speechSeconds >= MinSpeech.TotalSeconds ? segment.ToArray() : [];
            segment.Clear();
            hasSpeech = false;
            speechSeconds = 0;
        }

        if (ready.Length > 0)
        {
            onSegment(ToWav(Resample(ready, format.SampleRate, TargetSampleRate), TargetSampleRate));
        }
    }

    private float[] ToMono(byte[] buffer, int count)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var channels = format.Channels;
        var frames = count / (bytesPerSample * channels);
        var mono = new float[frames];

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;

            for (var channel = 0; channel < channels; channel++)
            {
                var offset = (frame * channels + channel) * bytesPerSample;
                sum += bytesPerSample == 4
                    ? BitConverter.ToSingle(buffer, offset)
                    : BitConverter.ToInt16(buffer, offset) / 32768f;
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        var sum = 0d;

        foreach (var sample in samples)
        {
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate)
        {
            return input;
        }

        var ratio = (double)fromRate / toRate;
        var output = new float[(int)(input.Length / ratio)];

        for (var i = 0; i < output.Length; i++)
        {
            var start = (int)(i * ratio);
            var end = Math.Min(Math.Max((int)((i + 1) * ratio), start + 1), input.Length);
            var sum = 0f;

            for (var j = start; j < end; j++)
            {
                sum += input[j];
            }

            output[i] = sum / (end - start);
        }

        return output;
    }

    public static TimeSpan Duration(byte[] wav) => TimeSpan.FromSeconds(Math.Max(0, wav.Length - WavHeaderSize) / (TargetSampleRate * 2.0));

    private static byte[] ToWav(float[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);

        foreach (var sample in samples)
        {
            writer.Write((short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
