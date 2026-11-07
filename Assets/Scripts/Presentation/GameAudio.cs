using System;
using System.Collections.Generic;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>
    /// Port of the prototype's Web Audio synthesizer. Every effect is synthesized once into an
    /// AudioClip at startup with the same oscillators, envelopes and filters, so the game ships
    /// no audio files and sounds like the original.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        const int Rate = 44100;
        const float Master = .76f, MusicMix = .56f;
        // The prototype's levels were tuned for a browser and are far too quiet on a phone speaker.
        // Effects are driven harder into a soft limiter; music gets a plain boost.
        const float EffectsDrive = 5.5f, MusicBoost = 4.5f;
        const string PrefKey = "colonia09.audio";

        AudioSource sfxSource, musicSource;
        float[] noiseBuffer;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        readonly Dictionary<string, AudioClip> musicClips = new Dictionary<string, AudioClip>();
        int musicStep;

        public bool Enabled { get; private set; } = true;

        static readonly Dictionary<string, float> MinGap = new Dictionary<string, float>
        {
            { "pulse", .1f }, { "tesla", .28f }, { "cryo", .2f }, { "hit", .26f }, { "destroy", .18f }, { "heal", 1.1f },
            { "emp", 1.3f }, { "build", .08f }, { "upgrade", .1f }, { "select", .1f }, { "core", .38f }, { "alert", .85f }, { "sapper", 1.3f },
        };

        public void Init()
        {
            Enabled = PlayerPrefs.GetInt(PrefKey, 1) == 1;
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            sfxSource = gameObject.AddComponent<AudioSource>();
            musicSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = musicSource.playOnAwake = false;

            // Decaying white noise, 0.22 s, like the prototype's shared noise buffer.
            var random = new System.Random(5);
            noiseBuffer = new float[(int)(Rate * .22f)];
            for (int i = 0; i < noiseBuffer.Length; i++) noiseBuffer[i] = (float)(random.NextDouble() * 2 - 1) * (1 - i / (float)noiseBuffer.Length);

            Build("select", .1f, b => Tone(b, 580, 790, .055f, Wave.Sine, .045f));
            Build("build", .14f, b => { Thump(b, 105, .11f, .08f); Noise(b, .09f, .025f, 2400, true); });
            Build("upgrade", .23f, b => { Thump(b, 130, .2f, .11f); Noise(b, .12f, .04f, 1100, false); });
            Build("recycle", .16f, b => Tone(b, 600, 250, .13f, Wave.Triangle, .08f));
            Build("pulse", .16f, b => { Thump(b, 112, .13f, .085f); Noise(b, .075f, .032f, 1100, true); });
            Build("cryo", .31f, b => { Noise(b, .28f, .09f, 450, false); Tone(b, 210, 120, .22f, Wave.Sine, .018f); });
            Build("tesla", .24f, b => { Noise(b, .21f, .13f, 1700, true); Thump(b, 90, .19f, .1f); });
            Build("hit", .1f, b => Tone(b, 180, 100, .07f, Wave.Triangle, .045f));
            Build("destroy", .38f, b => { Thump(b, 82, .35f, .2f); Noise(b, .31f, .17f, 850, false); });
            Build("heavyDestroy", .61f, b => { Thump(b, 62, .58f, .29f); Noise(b, .52f, .25f, 650, false); });
            Build("heal", .15f, b => Tone(b, 630, 830, .12f, Wave.Sine, .036f));
            Build("emp", .36f, b => { Noise(b, .33f, .12f, 3000, true); Tone(b, 190, 70, .29f, Wave.Saw, .05f); });
            Build("sapper", .35f, b => { Tone(b, 450, 80, .32f, Wave.Saw, .1f); PlainNoise(b, .18f, .075f); });
            Build("core", .53f, b => { Thump(b, 68, .5f, .25f); Noise(b, .32f, .12f, 600, false); });
            Build("alert", .26f, b => { Thump(b, 104, .23f, .075f); Tone(b, 360, 250, .2f, Wave.Sine, .037f); });
            Build("wave", .35f, b => { Thump(b, 75, .32f, .13f); Noise(b, .19f, .043f, 900, false); });
            Build("win", .39f, b => Tone(b, 470, 850, .36f, Wave.Triangle, .14f));
            Build("lost", .43f, b => Tone(b, 360, 70, .4f, Wave.Saw, .12f));
            Build("musicThump", .35f, b => Thump(b, 52, .32f, 1f));
        }

        public void Toggle()
        {
            Enabled = !Enabled;
            PlayerPrefs.SetInt(PrefKey, Enabled ? 1 : 0);
            if (Enabled) Play("select", true);
        }

        public void Play(string name, bool force = false)
        {
            if (!Enabled || !clips.TryGetValue(name, out var clip)) return;
            float now = Time.unscaledTime;
            float gap = MinGap.TryGetValue(name, out var g) ? g : .24f;
            if (!force && lastPlayed.TryGetValue(name, out var last) && last + gap > now) return;
            lastPlayed[name] = now;
            sfxSource.PlayOneShot(clip, 1);
        }

        /// <summary>One step of the ambient soundtrack, every 1.48 s while playing.</summary>
        public void MusicStep(int wave, bool heavyThreat)
        {
            if (!Enabled) return;
            float tension = Mathf.Clamp01((wave - 1) / 8f + (heavyThreat ? .26f : 0));
            float root = new[] { 55f, 49f, 61.735f, 43.655f }[(musicStep / 8) % 4];
            PlayNote(root, 1.95f, .062f + tension * .018f, Wave.Sine);
            if (musicStep % 2 == 0) PlayNote(root * 1.498f, 1.45f, .025f + tension * .015f, Wave.Triangle);
            if (tension > .34f && musicStep % 2 == 1) musicSource.PlayOneShot(clips["musicThump"], Mathf.Min(1, (.028f + tension * .015f) * MusicMix * Master * MusicBoost));
            musicStep++;
        }

        void PlayNote(float freq, float duration, float volume, Wave wave)
        {
            string key = $"{freq:0.000}-{wave}";
            if (!musicClips.TryGetValue(key, out var clip))
            {
                var buffer = new float[(int)((duration + .05f) * Rate)];
                Note(buffer, freq, duration, wave);
                clip = AudioClip.Create("music-" + key, buffer.Length, 1, Rate, false);
                clip.SetData(buffer, 0);
                musicClips[key] = clip;
            }
            musicSource.PlayOneShot(clip, Mathf.Min(1, volume * MusicMix * Master * MusicBoost));
        }

        // ---- Synthesis ----------------------------------------------------------------

        enum Wave { Sine, Triangle, Saw }

        void Build(string name, float length, Action<float[]> synth)
        {
            var buffer = new float[(int)((length + .03f) * Rate)];
            synth(buffer);
            if (name != "musicThump")
                for (int i = 0; i < buffer.Length; i++) buffer[i] = (float)Math.Tanh(buffer[i] * Master * EffectsDrive);
            var clip = AudioClip.Create(name, buffer.Length, 1, Rate, false);
            clip.SetData(buffer, 0);
            clips[name] = clip;
        }

        static float Exp(float from, float to, float k) => from * Mathf.Pow(to / from, Mathf.Clamp01(k));

        static float Osc(double phase, Wave wave)
        {
            switch (wave)
            {
                case Wave.Triangle: return (float)(2 / Math.PI * Math.Asin(Math.Sin(phase)));
                case Wave.Saw:
                    double cycles = phase / (2 * Math.PI);
                    return (float)(2 * (cycles - Math.Floor(cycles + .5)));
                default: return (float)Math.Sin(phase);
            }
        }

        static void Tone(float[] b, float freq, float end, float dur, Wave wave, float vol)
        {
            freq = Mathf.Max(40, freq); end = Mathf.Max(40, end); vol = Mathf.Max(.0002f, vol);
            double phase = 0;
            int count = Mathf.Min(b.Length, (int)((dur + .02f) * Rate));
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float f = Exp(freq, end, t / dur);
                phase += 2 * Math.PI * f / Rate;
                float gain = t < .008f ? Exp(.0001f, vol, t / .008f) : t < dur ? Exp(vol, .0001f, (t - .008f) / (dur - .008f)) : 0;
                b[i] += Osc(phase, wave) * gain;
            }
        }

        static void Note(float[] b, float freq, float dur, Wave wave)
        {
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += 2 * Math.PI * Exp(freq, freq * .996f, t / dur) / Rate;
                float gain = t < .045f ? Exp(.0001f, 1, t / .045f) : t < dur ? Exp(1, .0001f, (t - .045f) / (dur - .045f)) : 0;
                b[i] += Osc(phase, wave) * gain;
            }
        }

        static void Thump(float[] b, float freq, float dur, float vol)
        {
            var part = new float[Mathf.Min(b.Length, (int)((dur + .015f) * Rate))];
            double phase = 0;
            for (int i = 0; i < part.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Exp(freq * 2.3f, Mathf.Max(30, freq * .48f), t / (dur * .85f));
                phase += 2 * Math.PI * f / Rate;
                float gain = t < .007f ? Mathf.Lerp(.0001f, vol, t / .007f) : t < dur ? Exp(vol, .0001f, (t - .007f) / (dur - .007f)) : 0;
                part[i] = (float)Math.Sin(phase) * gain;
            }
            Filter(part, 540, false);
            for (int i = 0; i < part.Length; i++) b[i] += part[i];
        }

        void Noise(float[] b, float dur, float vol, float cutoff, bool highpass)
        {
            bool loop = dur > .2f;
            var part = new float[Mathf.Min(b.Length, (int)(dur * Rate))];
            for (int i = 0; i < part.Length; i++)
            {
                if (!loop && i >= noiseBuffer.Length) break;
                float t = i / (float)Rate;
                float gain = t < .01f ? Mathf.Lerp(.0001f, vol, t / .01f) : Exp(vol, .0001f, (t - .01f) / (dur - .01f));
                part[i] = noiseBuffer[i % noiseBuffer.Length] * gain;
            }
            Filter(part, cutoff, highpass);
            for (int i = 0; i < part.Length; i++) b[i] += part[i];
        }

        void PlainNoise(float[] b, float dur, float vol)
        {
            var part = new float[Mathf.Min(b.Length, Mathf.Min(noiseBuffer.Length, (int)(Mathf.Min(.21f, dur) * Rate)))];
            for (int i = 0; i < part.Length; i++)
                part[i] = noiseBuffer[i] * Exp(vol, .0001f, i / (float)Rate / dur);
            Filter(part, 950, false);
            for (int i = 0; i < part.Length; i++) b[i] += part[i];
        }

        /// <summary>Biquad filter with Web Audio's default Q of 1 dB, as in the prototype.</summary>
        static void Filter(float[] x, float cutoff, bool highpass)
        {
            double w0 = 2 * Math.PI * cutoff / Rate, q = Math.Pow(10, 1 / 20.0);
            double alpha = Math.Sin(w0) / (2 * q), cos = Math.Cos(w0);
            double b0 = highpass ? (1 + cos) / 2 : (1 - cos) / 2, b1 = highpass ? -(1 + cos) : 1 - cos, b2 = b0;
            double a0 = 1 + alpha, a1 = -2 * cos, a2 = 1 - alpha;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double y = (b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = y;
                x[i] = (float)y;
            }
        }
    }
}
