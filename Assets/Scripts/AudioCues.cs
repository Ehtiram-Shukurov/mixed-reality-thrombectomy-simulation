using UnityEngine;

// Short sound cues, generated in code (the project has no audio files).
public class AudioCues : MonoBehaviour
{
    public enum Cue { Tick, Fork, DeadEnd, Clot, Warning, Stent, Success, Phase, Injection, Click }

    public static AudioCues Instance { get; private set; }

    [Range(0f, 1f)] public float volume = 0.5f;

    private const int SampleRate = 24000;
    private AudioSource _source;
    private AudioClip[] _clips;

    void Awake()
    {
        if (Instance != null) { Destroy(this); return; }
        Instance = this;

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;

        _clips = new AudioClip[System.Enum.GetValues(typeof(Cue)).Length];
        _clips[(int)Cue.Injection] = MakeWhoosh("injection", 0.9f, 0.35f);
        _clips[(int)Cue.Click] = MakeClick("click", 0.6f);
        _clips[(int)Cue.Tick] = Make("tick", new[] { 1200f }, 0.03f, 0.5f, false);
        _clips[(int)Cue.Fork] = Make("fork", new[] { 660f, 880f }, 0.07f, 0.6f, false);
        _clips[(int)Cue.DeadEnd] = Make("deadend", new[] { 220f, 165f }, 0.11f, 0.5f, true);
        _clips[(int)Cue.Clot] = Make("clot", new[] { 130f, 260f }, 0.12f, 0.8f, false);
        _clips[(int)Cue.Warning] = Make("warning", new[] { 200f, 200f }, 0.07f, 0.5f, true);
        _clips[(int)Cue.Stent] = Make("stent", new[] { 440f, 550f, 660f, 880f }, 0.05f, 0.6f, false);
        _clips[(int)Cue.Success] = Make("success", new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.7f, false);
        _clips[(int)Cue.Phase] = Make("phase", new[] { 880f }, 0.06f, 0.35f, false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Play(Cue cue)
    {
        if (_source == null || _clips == null) return;
        var clip = _clips[(int)cue];
        if (clip != null) _source.PlayOneShot(clip, volume);
    }

    // Soft noise that swells and fades, with the brightness falling: a syringe push.
    private static AudioClip MakeWhoosh(string name, float seconds, float gain)
    {
        int n = Mathf.RoundToInt(SampleRate * seconds);
        var data = new float[n];
        var rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            float cutoff = Mathf.Lerp(0.25f, 0.03f, t);  // one-pole low-pass, closing over time
            low += cutoff * (white - low);
            float env = Mathf.Clamp01(t / 0.18f) * (1f - Mathf.SmoothStep(0.35f, 1f, t));
            data[i] = low * env * gain * 2.5f;
        }
        var clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // A short dry click, like a device locking open.
    private static AudioClip MakeClick(string name, float gain)
    {
        int n = Mathf.RoundToInt(SampleRate * 0.04f);
        var data = new float[n];
        var rng = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float decay = Mathf.Exp(-i / (SampleRate * 0.004f));
            float tone = Mathf.Sin(2f * Mathf.PI * 2400f * i / SampleRate);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            data[i] = (tone * 0.6f + noise * 0.4f) * decay * gain;
        }
        var clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Plays each frequency in turn, with a quick attack and a decay.
    private static AudioClip Make(string name, float[] freqs, float noteSeconds, float gain, bool square)
    {
        int noteSamples = Mathf.RoundToInt(SampleRate * noteSeconds);
        var data = new float[noteSamples * freqs.Length];
        for (int n = 0; n < freqs.Length; n++)
        {
            for (int i = 0; i < noteSamples; i++)
            {
                float t = i / (float)SampleRate;
                float phase = Mathf.Sin(2f * Mathf.PI * freqs[n] * t);
                float s = square ? Mathf.Sign(phase) * 0.6f : phase;
                float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));
                float decay = 1f - i / (float)noteSamples;
                data[n * noteSamples + i] = s * attack * decay * decay * gain;
            }
        }
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
