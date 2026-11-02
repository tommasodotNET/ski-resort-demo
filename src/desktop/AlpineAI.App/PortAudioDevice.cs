using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AlpineAI.Core;

namespace AlpineAI.App;

internal sealed class PortAudioDevice : IVoiceAudio
{
    private readonly Channel<byte[]> captured = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(20)
    {
        SingleReader = true,
        SingleWriter = true
    });
    private readonly Queue<byte[]> playback = [];
    private readonly object gate = new();
    private readonly AudioCallback callback;
    private IntPtr stream;
    private bool initialized;
    private int playbackOffset;
    private int queuedBytes;

    static PortAudioDevice()
    {
        NativeLibrary.SetDllImportResolver(typeof(PortAudioDevice).Assembly, ResolveLibrary);
    }

    public PortAudioDevice() => callback = ProcessAudio;

    private static IntPtr ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "portaudio") return IntPtr.Zero;
        var candidates = OperatingSystem.IsMacOS()
            ? new[] { "libportaudio.dylib", "/opt/homebrew/lib/libportaudio.dylib", "/usr/local/lib/libportaudio.dylib" }
            : ["libportaudio.so.2"];
        foreach (var candidate in candidates)
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle)) return handle;
        throw new DllNotFoundException("Voice needs PortAudio. Install portaudio on Fedora, libportaudio2 on Ubuntu, or brew install portaudio on macOS.");
    }

    public void Start()
    {
        Check(Pa_Initialize(), "Initialize audio");
        initialized = true;
        // The Voice Live bridge uses mono, signed PCM16 little-endian at 24 kHz.
        Check(Pa_OpenDefaultStream(out stream, 1, 1, 0x8, 24000, 1200, callback, IntPtr.Zero), "Open microphone and speakers");
        Check(Pa_StartStream(stream), "Start audio");
    }

    public IAsyncEnumerable<byte[]> CaptureAsync(CancellationToken token) => captured.Reader.ReadAllAsync(token);

    public void Play(byte[] pcm)
    {
        if (pcm.Length % 2 != 0) throw new InvalidDataException("Voice audio contains an incomplete PCM16 sample.");
        if (pcm.Length == 0) return;
        lock (gate)
        {
            if (queuedBytes + pcm.Length > 24000 * 2 * 60)
                throw new IOException("Voice playback fell more than a minute behind.");
            playback.Enqueue(pcm);
            queuedBytes += pcm.Length;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            playback.Clear();
            playbackOffset = 0;
            queuedBytes = 0;
        }
    }

    private int ProcessAudio(IntPtr input, IntPtr output, nuint frames, IntPtr timeInfo, nuint flags, IntPtr userData)
    {
        var length = checked((int)frames * 2);
        if (input != IntPtr.Zero)
        {
            var pcm = new byte[length];
            Marshal.Copy(input, pcm, 0, length);
            if (!captured.Writer.TryWrite(pcm))
            {
                captured.Writer.TryComplete(new IOException("Microphone streaming could not keep up with the connection."));
                return 2; // paAbort; never throw across the native callback boundary.
            }
        }
        var samples = new byte[length];
        lock (gate)
        {
            var written = 0;
            while (written < length && playback.TryPeek(out var chunk))
            {
                var count = Math.Min(length - written, chunk.Length - playbackOffset);
                chunk.AsSpan(playbackOffset, count).CopyTo(samples.AsSpan(written));
                written += count;
                playbackOffset += count;
                queuedBytes -= count;
                if (playbackOffset == chunk.Length)
                {
                    playback.Dequeue();
                    playbackOffset = 0;
                }
            }
        }
        if (output != IntPtr.Zero) Marshal.Copy(samples, 0, output, length);
        return 0;
    }

    public void Dispose()
    {
        if (stream != IntPtr.Zero)
        {
            var abort = Pa_AbortStream(stream);
            // paStreamIsStopped is expected after a callback aborts.
            if (abort != 0 && abort != -9983) Console.Error.WriteLine($"Audio stop failed: {ErrorText(abort)}");
            var close = Pa_CloseStream(stream);
            if (close != 0) Console.Error.WriteLine($"Audio close failed: {ErrorText(close)}");
            stream = IntPtr.Zero;
        }
        if (initialized)
        {
            var terminate = Pa_Terminate();
            if (terminate != 0) Console.Error.WriteLine($"Audio shutdown failed: {ErrorText(terminate)}");
            initialized = false;
        }
        captured.Writer.TryComplete();
        Clear();
        GC.KeepAlive(callback);
    }

    private static string ErrorText(int code) => Marshal.PtrToStringUTF8(Pa_GetErrorText(code)) ?? $"PortAudio error {code}";
    private static void Check(int code, string operation)
    {
        if (code != 0) throw new IOException($"{operation}: {ErrorText(code)}. Check the default audio devices and microphone permission.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AudioCallback(IntPtr input, IntPtr output, nuint frames, IntPtr timeInfo, nuint flags, IntPtr userData);
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern int Pa_Initialize();
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern int Pa_Terminate();
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr Pa_GetErrorText(int code);
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Pa_OpenDefaultStream(out IntPtr stream, int inputChannels, int outputChannels,
        nuint format, double sampleRate, nuint frames, AudioCallback callback, IntPtr userData);
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern int Pa_StartStream(IntPtr stream);
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern int Pa_AbortStream(IntPtr stream);
    [DllImport("portaudio", CallingConvention = CallingConvention.Cdecl)] private static extern int Pa_CloseStream(IntPtr stream);
}
