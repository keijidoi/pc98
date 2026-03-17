using System.Runtime.InteropServices;
using SDL2;

namespace PC98Emu.Sound;

/// <summary>
/// Audio output using SDL2. Bridges YM2608 sample generation to the audio device.
/// Uses a ring buffer for thread-safe communication between emulation and audio callback.
/// </summary>
public class AudioOutput : IDisposable
{
    private const int SAMPLE_RATE = 44100;
    private const int CHANNELS = 2;
    private const int BUFFER_SAMPLES = 1024;
    private const int RING_BUFFER_SIZE = 16384; // samples (stereo pairs * 2)

    private readonly YM2608 _ym2608;
    private uint _audioDevice;
    private bool _initialized;

    // Ring buffer for thread-safe sample transfer
    private readonly short[] _ringBuffer = new short[RING_BUFFER_SIZE];
    private volatile int _readPos;
    private volatile int _writePos;

    // Must keep a reference to prevent GC collection of the callback delegate
    private SDL.SDL_AudioCallback? _callbackDelegate;

    public AudioOutput(YM2608 ym2608)
    {
        _ym2608 = ym2608;
    }

    public void Init()
    {
        if (_initialized) return;

        if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) < 0)
        {
            throw new InvalidOperationException($"SDL audio init failed: {SDL.SDL_GetError()}");
        }

        _callbackDelegate = AudioCallback;

        var desired = new SDL.SDL_AudioSpec
        {
            freq = SAMPLE_RATE,
            format = SDL.AUDIO_S16,
            channels = CHANNELS,
            samples = BUFFER_SAMPLES,
            callback = _callbackDelegate
        };

        _audioDevice = SDL.SDL_OpenAudioDevice(
            null, 0, ref desired, out _, 0);

        if (_audioDevice == 0)
        {
            throw new InvalidOperationException($"SDL_OpenAudioDevice failed: {SDL.SDL_GetError()}");
        }

        // Start playback
        SDL.SDL_PauseAudioDevice(_audioDevice, 0);
        _initialized = true;
    }

    /// <summary>
    /// Feed samples into the ring buffer from the emulation thread.
    /// Call this periodically to keep the audio buffer filled.
    /// </summary>
    public void FeedSamples(int samplePairs)
    {
        var tempBuffer = new short[samplePairs * 2];
        _ym2608.GenerateSamples(tempBuffer, 0, samplePairs);

        for (int i = 0; i < tempBuffer.Length; i++)
        {
            int nextWrite = (_writePos + 1) % RING_BUFFER_SIZE;
            if (nextWrite == _readPos)
                break; // Buffer full, drop samples
            _ringBuffer[_writePos] = tempBuffer[i];
            _writePos = nextWrite;
        }
    }

    private void AudioCallback(IntPtr userdata, IntPtr stream, int len)
    {
        int sampleCount = len / 2; // 16-bit samples
        var buffer = new short[sampleCount];

        int available = (_writePos - _readPos + RING_BUFFER_SIZE) % RING_BUFFER_SIZE;

        if (available >= sampleCount)
        {
            // Read from ring buffer
            for (int i = 0; i < sampleCount; i++)
            {
                buffer[i] = _ringBuffer[_readPos];
                _readPos = (_readPos + 1) % RING_BUFFER_SIZE;
            }
        }
        else
        {
            // Underrun: generate samples directly (fallback)
            _ym2608.GenerateSamples(buffer, 0, sampleCount / 2);
        }

        Marshal.Copy(buffer, 0, stream, sampleCount);
    }

    public void Shutdown()
    {
        if (!_initialized) return;

        SDL.SDL_PauseAudioDevice(_audioDevice, 1);
        SDL.SDL_CloseAudioDevice(_audioDevice);
        _audioDevice = 0;
        _initialized = false;
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }
}
