using UnityEngine;

namespace FrcSim
{
    // 比賽提示音(程式即時合成,不需要音效檔):開賽、TELEOP 開始(TRANSITION 結束)、終局(剩 30 秒)、比賽結束蜂鳴。
    // 只在比賽計時開著時響;M 鍵靜音(記在 PlayerPrefs "matchAudio")。
    public class MatchAudio : MonoBehaviour
    {
        AudioSource src;
        AudioClip start, teleop, endgame, buzzer;
        string lastPhase = "";
        bool started;
        public static bool Muted { get => PlayerPrefs.GetInt("matchAudio", 1) == 0; set { PlayerPrefs.SetInt("matchAudio", value ? 0 : 1); PlayerPrefs.Save(); } }
        public static string Cues = "";   // 測試用:播過的提示音

        void Awake()
        {
            Cues = "";
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.5f;
            start = Tone("start", new[] { 523f, 659f, 784f }, 0.16f, 0.02f);
            teleop = Tone("teleop", new[] { 880f, 0f, 880f }, 0.12f, 0.02f);
            endgame = Tone("endgame", new[] { 660f, 0f, 660f, 0f, 660f }, 0.10f, 0.02f);
            buzzer = Tone("buzzer", new[] { 140f }, 1.2f, 0.05f, true);
        }

        // 依序接起來的音(頻率 0 = 靜音),每段 seg 秒,頭尾淡入淡出 fade 秒
        static AudioClip Tone(string name, float[] freqs, float seg, float fade, bool square = false)
        {
            const int rate = 44100;
            int n = (int)(rate * seg), total = n * freqs.Length;
            var data = new float[total];
            for (int k = 0; k < freqs.Length; k++)
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate, f = freqs[k];
                    if (f <= 0f) continue;
                    float env = Mathf.Min(1f, Mathf.Min(t, seg - t) / fade);
                    float s = Mathf.Sin(2f * Mathf.PI * f * t);
                    if (square) s = Mathf.Sign(s) * 0.6f + Mathf.Sin(4f * Mathf.PI * f * t) * 0.2f;
                    data[k * n + i] = s * env * 0.6f;
                }
            var clip = AudioClip.Create(name, total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip c) { Cues += c.name + " "; if (!Muted && src != null) src.PlayOneShot(c); }

        void Update()
        {
            if (!MenuSystem.Blocking && Input.GetKeyDown(KeyCode.M)) Muted = !Muted;
            if (!ScoreManager.ClockOn || ScoreManager.WaitingForCode) return;
            string ph = ScoreManager.Phase;
            if (!started && (ph == "AUTO" || ph.StartsWith("SHIFT"))) { started = true; Play(start); lastPhase = ph; }   // 自由練習從 SHIFT 1 開始
            if (ph != lastPhase)
            {
                if (lastPhase == "TRANSITION" && ph.StartsWith("SHIFT")) Play(teleop);
                if (ph == "END GAME") Play(endgame);
                if (ph == "SCORING GRACE") Play(buzzer);
                lastPhase = ph;
            }
        }
    }
}
