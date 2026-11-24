using System;
using System.Collections.Generic;
using UnityEngine;

namespace RingingLoot.View
{
    /// 효과음(임시, 코드 합성). reference/v2b.html 의 Audio_ 객체(tone·noise 조합)를 그대로 옮긴 것이라 외부 파일과 출처 문제가 없다.
    /// 소리마다 최소 간격(gate)을 둬서 같은 소리가 겹쳐 노래처럼 들리지 않게 한다(v2b와 같은 값).
    /// 나중에 녹음·구한 효과음으로 바꿀 때는 이 파일의 Build()만 고치면 된다.
    public static class Sfx
    {
        const int Rate = 44100;
        const float Master = 0.75f;                       // v2b: 마스터 게인 0.75
        public static bool On = true;

        static Dictionary<string, AudioClip> clips;
        static readonly Dictionary<string, float> last = new();

        /// 이름 → (클립, 최소 간격). 간격이 0이면 겹쳐도 되는 소리(심장 등).
        static readonly Dictionary<string, float> Gap = new()
        {
            ["fire"] = 0.3f, ["pickup"] = 0.25f, ["danger"] = 0.8f, ["shard"] = 0.5f, ["step"] = 0.14f, ["growl"] = 1.2f,
            ["clack"] = 0.15f, ["zap"] = 0.4f, ["rock"] = 0.7f, ["rumble"] = 3f, ["heart"] = 0f, ["bell"] = 0.5f,
            ["escape"] = 0f, ["death"] = 0f, ["warn"] = 0.45f, ["out"] = 0.3f,
        };

        public static void Play(AudioSource src, string name, float vol = 1f)
        {
            if (!On || src == null || vol <= 0.001f) return;
            Build();
            if (!clips.TryGetValue(name, out var clip)) return;
            float now = Time.unscaledTime, gap = Gap.TryGetValue(name, out var g) ? g : 0f;
            if (gap > 0f && last.TryGetValue(name, out var t) && now - t < gap) return;
            last[name] = now;
            src.PlayOneShot(clip, Mathf.Clamp01(vol) * Master);
        }

        // ----- 클립 만들기 -----
        static void Build()
        {
            if (clips != null) return;
            clips = new Dictionary<string, AudioClip>();
            // 각 소리의 (주파수·길이·파형·볼륨·시작 지연·끝 주파수)는 v2b Audio_ 의 값 그대로. 거리에 따른 크기는 재생할 때 곱한다.
            Add("fire",   0.30f, b => { Noise(b, 0.14f, 0.22f, 0f, 300); Tone(b, 100, 0.14f, Wave.Saw, 0.10f, 0f, 55); });
            Add("pickup", 0.30f, b => { Tone(b, 880, 0.16f, Wave.Sine, 0.14f); Tone(b, 1319, 0.22f, Wave.Sine, 0.12f, 0.07f); });
            Add("danger", 0.95f, b => Tone(b, 185, 0.9f, Wave.Sine, 0.15f));
            Add("shard",  0.40f, b => { Noise(b, 0.30f, 0.32f, 0f, 2000); Tone(b, 1568, 0.2f, Wave.Sine, 0.10f, 0.02f); });
            Add("step",   0.13f, b => { Noise(b, 0.06f, 0.26f, 0f, 100); Tone(b, 70, 0.09f, Wave.Sine, 0.22f, 0f, 45); });
            Add("growl",  0.60f, b => Tone(b, 110, 0.55f, Wave.Saw, 0.13f, 0f, 55));
            Add("clack",  0.10f, b => { Noise(b, 0.07f, 0.30f, 0f, 2500); Tone(b, 700, 0.06f, Wave.Square, 0.08f, 0f, 380); });
            Add("zap",    0.30f, b => { Noise(b, 0.22f, 0.18f, 0f, 1500); Tone(b, 1100, 0.25f, Wave.Saw, 0.10f, 0f, 200); });
            Add("rock",   0.65f, b => { Noise(b, 0.50f, 0.24f, 0f, 120); Tone(b, 55, 0.6f, Wave.Tri, 0.18f, 0f, 35); });
            Add("rumble", 1.05f, b => { Noise(b, 1.0f, 0.07f, 0f, 60); Tone(b, 45, 1.0f, Wave.Sine, 0.10f); });
            Add("heart",  0.30f, b => { Tone(b, 55, 0.12f, Wave.Sine, 0.22f, 0f, 40); Tone(b, 50, 0.10f, Wave.Sine, 0.16f, 0.16f, 38); });
            Add("bell",   0.60f, b => { Tone(b, 1240, 0.55f, Wave.Sine, 0.12f, 0f, 1150); Tone(b, 1860, 0.30f, Wave.Sine, 0.05f, 0.02f); });
            Add("escape", 0.65f, b => { float[] f = { 523, 659, 784 }; for (int i = 0; i < 3; i++) Tone(b, f[i], 0.3f, Wave.Tri, 0.16f, i * 0.1f); });
            Add("death",  0.95f, b => { Tone(b, 300, 0.9f, Wave.Saw, 0.16f, 0f, 55); Noise(b, 0.4f, 0.20f, 0f, 200); });
            // 경고음: 음높이 없는 '다닥다닥'이라 겹쳐도 멜로디가 되지 않는다(v2b warn)
            Add("warn",   0.35f, b => { for (int i = 0; i < 3; i++) Noise(b, 0.045f, 0.30f, i * 0.1f, 1800); });
            Add("out",    0.40f, b => Noise(b, 0.35f, 0.18f, 0f, 500));                 // 기름이 떨어져 횃불이 꺼짐
        }

        static void Add(string name, float seconds, Action<float[]> fill)
        {
            var data = new float[(int)(Rate * seconds)];
            fill(data);
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            var clip = AudioClip.Create("sfx_" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        enum Wave { Sine, Saw, Square, Tri }

        /// WebAudio 오실레이터: 주파수가 끝 주파수까지 지수로 미끄러지고, 10ms 만에 올라와서 끝까지 지수로 줄어든다.
        static void Tone(float[] buf, float f0, float dur, Wave wave, float vol, float when = 0f, float slideTo = 0f)
        {
            int start = (int)(when * Rate), n = (int)(dur * Rate);
            double phase = 0;
            for (int i = 0; i < n && start + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float f = slideTo > 0f ? f0 * Mathf.Pow(slideTo / f0, t / dur) : f0;
                phase += f / Rate; double ph = phase - Math.Floor(phase);
                float s = wave switch
                {
                    Wave.Sine => Mathf.Sin((float)(2 * Math.PI * ph)),
                    Wave.Saw => (float)(2 * ph - 1),
                    Wave.Square => ph < 0.5 ? 1f : -1f,
                    _ => (float)(1 - 4 * Math.Abs(ph - 0.5)),
                };
                float env = t < 0.01f ? Mathf.Lerp(0.0001f, vol, t / 0.01f)
                                      : vol * Mathf.Pow(0.0001f / vol, (t - 0.01f) / Mathf.Max(0.001f, dur - 0.01f));
                buf[start + i] += s * env;
            }
        }

        /// 흰 잡음이 점점 줄어드는 소리 + 낮은 소리를 깎는 필터(v2b noise). 매번 같은 소리가 나도록 시드를 고정한다.
        static void Noise(float[] buf, float dur, float vol, float when, float hp)
        {
            int start = (int)(when * Rate), n = (int)(dur * Rate);
            var rng = new System.Random(12345 + (int)(hp * 7) + (int)(dur * 1000));
            float rc = 1f / (2f * Mathf.PI * hp), dt = 1f / Rate, a = rc / (rc + dt), y = 0f, xPrev = 0f;
            for (int i = 0; i < n && start + i < buf.Length; i++)
            {
                float x = ((float)rng.NextDouble() * 2f - 1f) * (1f - i / (float)n);
                y = a * (y + x - xPrev); xPrev = x;
                buf[start + i] += y * vol;
            }
        }
    }
}
