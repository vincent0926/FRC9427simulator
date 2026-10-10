using UnityEngine;

namespace FrcSim
{
    // 真實程式模式的「開賽前」:機器人程式連上後先 Disabled(和正式比賽一樣),讓使用者選自動程式,再開始 AUTO。
    //   [ / ] 或十字鍵左右 = 換選項,Tab 或十字鍵上下 = 換選單(有多個 SendableChooser 時),Enter 或 A = 開賽
    //   有選單:倒數 AutoStartSeconds 秒自動開賽;找不到選單:NoChooserSeconds 秒後開賽(照舊)
    //   程式啟動失敗/斷線 → 直接結束準備階段(比賽時鐘照常跑)
    public class PreMatch : MonoBehaviour
    {
        public static PreMatch I;
        public NtChooser Nt;
        public float AutoStartSeconds = 20f, NoChooserSeconds = 10f;   // 程式常在連上幾秒後才把選單放上 SmartDashboard,太短會在選單出現前就開賽
        public string StartInfo = "";   // HUD 用:機器人擺在哪個起點
        string placedFor;               // 已經依哪個自動程式擺過車
        public bool Done { get; private set; }
        public int ChooserIndex;
        float connectedAt = -1f;
        bool restored;

        public bool Active => !Done && GameSession.Hal != null && GameSession.Hal.Connected;
        public float SecondsLeft => connectedAt < 0f ? AutoStartSeconds : Mathf.Max(0f, (HasChooser ? AutoStartSeconds : NoChooserSeconds) - (Time.unscaledTime - connectedAt));

        // 把機器人擺到選的自動程式的起點(PathPlanner .auto);找不到就擺回站位。紅方依場地中心點對稱(和 PathPlanner 翻轉一致)
        void PlaceForAuto(string autoName)
        {
            var d = GameSession.Drive; var hal = GameSession.Hal;
            if (d == null || autoName == placedFor) return;
            placedFor = autoName;
            float x, y, h;
            bool fromPath = PathPlannerStart.TryGet(hal != null ? hal.ProjectDir : null, autoName, out x, out y, out h);
            if (fromPath)
            {
                if (AutoPlan.IsRed(GameSession.Station)) AutoPlan.Mirror(ref x, ref y, ref h);
                StartInfo = $"{autoName}  ({x:0.00}, {y:0.00})  {h:0}°";
            }
            else
            {
                AutoPlan.StartPose(GameSession.Station, out x, out y, out h);
                StartInfo = AutoPlan.Labels[GameSession.Station];
            }
            d.SetPose(new Vector2(x, y), h);
            if (Nt != null) { Nt.StartX = x; Nt.StartY = y; Nt.StartDeg = h; }
            Debug.Log($"[PreMatch] robot placed for auto '{autoName}': ({x:0.00},{y:0.00}) {h:0} deg fromPath={fromPath}");
        }
        public bool HasChooser => Nt != null && Nt.Choosers.Count > 0;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        static float Arg(string name, float def)
        {
            var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        void Start() { AutoStartSeconds = Arg("-prematch", AutoStartSeconds); }

        bool startRequested;
        public bool WaitingForMotors => startRequested && !Done;

        // 要求開賽:等馬達模擬(simagent)開始推資料才真的開始(最多等到連線後 30 秒)
        public void StartMatch() { startRequested = true; }
        void StartNow() { Done = true; if (GameSession.Hal != null) GameSession.Hal.Enabled = true; }
        bool MotorsReady()
        {
            var ms = GameSession.Drive != null ? GameSession.Drive.GetComponent<MechSim>() : null;
            if (ms == null || ms.AgentReady) return true;
            return connectedAt >= 0f && Time.unscaledTime - connectedAt > 30f;
        }

        void Update()
        {
            var hal = GameSession.Hal;
            if (Done || hal == null) return;
            if (!hal.Connected)
            {
                if (!hal.Starting) StartNow();   // 啟動失敗或斷線:不要卡在準備階段
                else hal.Enabled = false;          // 一連上就先 Disabled
                return;
            }
            hal.Enabled = false;
            if (connectedAt < 0f && (Nt == null || Nt.Connected)) connectedAt = Time.unscaledTime;   // 倒數從 NetworkTables 連上才開始
            if (ScoreManager.MatchTime >= 30f) { StartNow(); return; }   // 按了 N 跳過 AUTO
            if (startRequested) { if (MotorsReady()) StartNow(); return; }

            var list = Nt != null ? Nt.Choosers : null;
            if (list != null && list.Count > 0)
            {
                if (!restored)
                {
                    // 1. 有選項的名稱就是站位(例如 PathPlanner 的 "Blue3.auto" → "Blue3")→ 自動選它
                    // 2. 否則用上次在這個選單選過的選項(還在清單裡的話)
                    restored = true;
                    foreach (var c in list)
                    {
                        string byStation = MatchStation(c.Options, GameSession.Station);
                        string prev = PlayerPrefs.GetString(NtChooser.PrefKey(c.Name), "");
                        if (byStation != null) Nt.Select(c, byStation);
                        else if (prev != "" && System.Array.IndexOf(c.Options, prev) >= 0) Nt.Select(c, prev);
                    }
                }
                ChooserIndex = Mathf.Clamp(ChooserIndex, 0, list.Count - 1);
                var cur = list[ChooserIndex];
                PlaceForAuto(Current(list[0]));
                if (!MenuSystem.Blocking)
                {
                    if (Input.GetKeyDown(KeyCode.Tab) || Pad.Down(Pad.DDown)) ChooserIndex = (ChooserIndex + 1) % list.Count;
                    if (Pad.Down(Pad.DUp)) ChooserIndex = (ChooserIndex + list.Count - 1) % list.Count;
                    int step = (Input.GetKeyDown(KeyCode.RightBracket) || Pad.Down(Pad.DRight) ? 1 : 0) - (Input.GetKeyDown(KeyCode.LeftBracket) || Pad.Down(Pad.DLeft) ? 1 : 0);
                    if (step != 0 && cur.Options.Length > 0)
                    {
                        string now = Current(cur);
                        int idx = System.Array.IndexOf(cur.Options, now);
                        idx = ((idx < 0 ? 0 : idx) + step + cur.Options.Length) % cur.Options.Length;
                        Nt.Select(cur, cur.Options[idx]);
                        connectedAt = Time.unscaledTime;   // 有在操作就重新倒數
                    }
                }
            }
            if (!MenuSystem.Blocking && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Pad.Down(Pad.A))) { StartMatch(); return; }
            if (SecondsLeft <= 0f) StartMatch();
            if (startRequested && MotorsReady()) StartNow();
        }

        static string Norm(string s) { var sb = new System.Text.StringBuilder(); foreach (char ch in s.ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch); return sb.ToString(); }

        // 名稱和站位一樣的選項("Blue3"、"blue 3"、"Blue-3" 都算);沒有就 null
        public static string MatchStation(string[] options, int station)
        {
            string want = Norm(AutoPlan.Labels[AutoPlan.Clamp(station)]);
            foreach (var o in options) if (Norm(o) == want) return o;
            return null;
        }

        // 目前會跑的選項:使用者選的 > 程式回報的 active > default
        public static string Current(NtChooser.Chooser c) => c.Selected != "" ? c.Selected : c.Active != "" ? c.Active : c.Default;
    }
}
