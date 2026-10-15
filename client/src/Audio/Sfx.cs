using Raylib_cs;

namespace RimFps.Audio;

/// <summary>
/// The player's own sounds, made at start: crunchy, low sample-rate, 8-bit, as a 90s sound
/// card played them. RimWorld plays its own sounds for everything that happens in it (its
/// guns, explosions, doors); these are the immediate ones in the player's hands and ears.
/// </summary>
public sealed class Sfx : IDisposable
{
    private const int Rate = 11025;
    private readonly Dictionary<string, Sound[]> sounds = new();
    private readonly Dictionary<string, int> next = new();
    private readonly Random random = new(3);
    public bool Ready { get; }
    public float Volume = 0.6f;

    public Sfx()
    {
        Raylib.InitAudioDevice();
        Ready = Raylib.IsAudioDeviceReady();
        if (!Ready)
        {
            Console.WriteLine("[fps] no audio device: playing silent");
            return;
        }
        Add("light", Gunshot(0.14f, 0.55f, 1400f, 0.5f));
        Add("rifle", Gunshot(0.2f, 0.7f, 900f, 0.7f));
        Add("heavy", Gunshot(0.32f, 0.85f, 520f, 1f));
        Add("shotgun", Gunshot(0.42f, 1f, 380f, 1.1f));
        Add("charge", Zap());
        Add("click", Click(1));
        Add("reload", Click(2));
        Add("swing", Whoosh(0.22f, 900f));
        Add("throw", Whoosh(0.3f, 600f));
        Add("hit", Thud(0.12f, 140f));
        Add("hurt", Thud(0.25f, 90f));
        Add("boom", Explosion());
        Add("whistle", Whistle());
        Add("whiz", Whiz());
        Add("door", Door());
    }

    private void Add(string name, float[] samples)
    {
        Wave wave = Wav(samples);
        Sound sound = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        // A few voices each, so rapid fire overlaps instead of cutting itself off.
        sounds[name] = new[] { sound, Raylib.LoadSoundAlias(sound), Raylib.LoadSoundAlias(sound), Raylib.LoadSoundAlias(sound) };
        next[name] = 0;
    }

    public void Play(string name, float volume = 1f, float pitch = 1f, float pan = 0.5f)
    {
        if (!Ready || !sounds.TryGetValue(name, out Sound[]? voices)) return;
        int i = next[name];
        next[name] = (i + 1) % voices.Length;
        Sound s = voices[i];
        Raylib.SetSoundVolume(s, Math.Clamp(volume * Volume, 0f, 1f));
        Raylib.SetSoundPitch(s, pitch * (0.96f + (float)random.NextDouble() * 0.08f));
        Raylib.SetSoundPan(s, Math.Clamp(pan, 0f, 1f));
        Raylib.PlaySound(s);
    }

    /// <summary>Which shot sound a RimWorld weapon makes.</summary>
    public static string ShotFor(string def)
    {
        if (def.Contains("Shotgun")) return "shotgun";
        if (def.Contains("Charge") || def.Contains("Plasma") || def.Contains("EMP") || def.Contains("Beam")) return "charge";
        if (def.Contains("Sniper") || def.Contains("LMG") || def.Contains("Minigun") || def.Contains("Bolt")) return "heavy";
        if (def.Contains("Pistol") || def.Contains("Revolver") || def.Contains("Autopistol")) return "light";
        return "rifle";
    }

    private float Noise() => (float)random.NextDouble() * 2f - 1f;

    private float[] Gunshot(float seconds, float loud, float bright, float body)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        float low = 0, lp = Math.Clamp(bright / Rate, 0.02f, 0.9f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = MathF.Exp(-t * (9f / seconds));
            low += (Noise() - low) * lp;
            float thump = MathF.Sin(t * MathF.Tau * (70f / body) * (1f + 3f * MathF.Exp(-t * 40f))) * MathF.Exp(-t * 18f / body);
            s[i] = (low * 0.9f + thump * 0.8f) * env * loud;
        }
        return s;
    }

    private float[] Zap()
    {
        int n = (int)(0.25f * Rate);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float f = 1800f * MathF.Exp(-t * 9f) + 120f;
            s[i] = (MathF.Sign(MathF.Sin(t * MathF.Tau * f)) * 0.5f + Noise() * 0.25f) * MathF.Exp(-t * 10f);
        }
        return s;
    }

    private float[] Click(int clicks)
    {
        int n = (int)(0.25f * Rate);
        var s = new float[n];
        for (int c = 0; c < clicks; c++)
        {
            int start = (int)(c * 0.12f * Rate);
            for (int i = 0; i < 300 && start + i < n; i++)
                s[start + i] += Noise() * MathF.Exp(-i / 40f) * 0.7f;
        }
        return s;
    }

    private float[] Whoosh(float seconds, float bright)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        float low = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = MathF.Sin(t * MathF.PI);
            low += (Noise() - low) * (bright * (0.3f + t) / Rate);
            s[i] = low * env * 2.2f;
        }
        return s;
    }

    private float[] Thud(float seconds, float pitch)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            s[i] = (MathF.Sin(t * MathF.Tau * pitch * (1f + MathF.Exp(-t * 30f))) * 0.9f + Noise() * 0.3f) * MathF.Exp(-t * 14f / seconds * 0.12f);
        }
        return s;
    }

    private float[] Explosion()
    {
        int n = (int)(1.4f * Rate);
        var s = new float[n];
        float low = 0, lower = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            low += (Noise() - low) * 0.12f;
            lower += (low - lower) * 0.05f;
            float env = t < 0.02f ? t / 0.02f : MathF.Exp(-(t - 0.02f) * 2.6f);
            s[i] = (lower * 3.2f + low * 0.6f) * env;
        }
        return s;
    }

    private float[] Whistle()
    {
        int n = (int)(1.5f * Rate);
        var s = new float[n];
        float phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            phase += MathF.Tau * (1400f - t * 600f) / Rate;
            s[i] = MathF.Sin(phase) * 0.35f * MathF.Min(1f, t * 3f);
        }
        return s;
    }

    private float[] Whiz()
    {
        int n = (int)(0.12f * Rate);
        var s = new float[n];
        float low = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            low += (Noise() - low) * 0.6f;
            s[i] = low * MathF.Sin(t * MathF.PI) * 0.6f;
        }
        return s;
    }

    private float[] Door()
    {
        int n = (int)(0.4f * Rate);
        var s = new float[n];
        float low = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            low += (Noise() - low) * 0.05f;
            s[i] = (low * 2f + MathF.Sin(t * MathF.Tau * 55f) * 0.2f) * MathF.Sin(t / 0.4f * MathF.PI);
        }
        return s;
    }

    /// <summary>Samples as an 8-bit-sounding 16-bit mono WAV wave.</summary>
    private static Wave Wav(float[] samples)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(bytes);
        foreach (float v in samples)
        {
            // Crushed to 64 levels: the grit of an old sound card.
            float crushed = MathF.Round(Math.Clamp(v, -1f, 1f) * 32f) / 32f;
            w.Write((short)(crushed * 32000));
        }
        w.Flush();
        return Raylib.LoadWaveFromMemory(".wav", stream.ToArray());
    }

    public void Dispose()
    {
        if (!Ready) return;
        foreach (Sound[] voices in sounds.Values)
        {
            for (int i = 1; i < voices.Length; i++) Raylib.UnloadSoundAlias(voices[i]);
            Raylib.UnloadSound(voices[0]);
        }
        Raylib.CloseAudioDevice();
    }
}
