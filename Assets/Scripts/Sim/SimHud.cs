using UnityEngine;

namespace FrcSim
{
    // 精簡 HUD:頂部中央 = 比分與計時;左上 = 一個小面板(速度/持球/吸球/飛輪/射擊);
    // F3 顯示詳細數據、F1 顯示按鍵說明(開場 15 秒自動顯示)。真實程式啟動中時中央有提示。
    public class SimHud : MonoBehaviour
    {
        public SwerveDrive Drive;
        public CameraRig Rig;
        public RobotMechanisms Mech;
        float fps;
        float born;
        bool showDebug, showHelp;
        Camera hudCam;
        static bool Zh => Loc.Lang == "zh";
        static string L(string zh, string en) => Zh ? zh : en;
        static string PhaseName(string p)
        {
            if (!Zh) return p;
            if (p.StartsWith("SHIFT ")) return "輪替 " + p.Substring(6);
            switch (p)
            {
                case "AUTO": return "自動階段";
                case "TRANSITION": return "過渡期";
                case "END GAME": return "終局";
                case "SCORING GRACE": return "計分寬限";
                case "MATCH OVER": return "比賽結束";
                case "AUTO DONE": return "自動模式結束";
                case "FREE PLAY": return "自由練習";
                default: return p;
            }
        }

        void Start() { born = Time.unscaledTime; }

        void Update()
        {
            // 自動模式結束:Enter / A 同樣設定再跑一次
            if (GameSession.AutoOnly && ScoreManager.Ended && !MenuSystem.Blocking && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Pad.Down(Pad.A)) && Rig != null)
            { GameSession.Restart(Rig); return; }
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
            if (Input.GetKeyDown(KeyCode.F1)) { showHelp = !showHelp; born = showHelp ? Time.unscaledTime - 9999f : Time.unscaledTime - 9999f; }
        }

        static GUIStyle Style(int size, Color c, TextAnchor a = TextAnchor.UpperLeft, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, font = UiTheme.Font, alignment = a, fontStyle = fs, wordWrap = false };
            s.normal.textColor = c;
            return s;
        }

        static void Panel(Rect r, float alpha = 0.55f)
        {
            var old = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.10f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.30f, 0.50f, 0.75f, 0.45f);
            GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height), Texture2D.whiteTexture);   // 左側細強調條
            GUI.color = old;
        }

        void OnGUI()
        {
            if (Drive == null) return;
            GUI.depth = -100;   // HUD 畫在最上層(超取樣貼圖在 depth 1000,畫在最底)
            // 依螢幕高度縮放(高解析度螢幕上字不會太小/面板不會太擠)
            float sc = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.8f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            float W = Screen.width / sc, H = Screen.height / sc;
            var dim = new Color(0.78f, 0.84f, 0.92f);

            // ---- 左上小面板
            var lines = new System.Collections.Generic.List<string>();
            lines.Add($"{Drive.Speed:0.0} m/s   {L("朝向", "hdg")} {Drive.HeadingRad * Mathf.Rad2Deg:0}°   FPS {fps:0}");
            if (Mech != null)
            {
                lines.Add($"{L("持球", "Fuel")} {Mech.Held}/{RobotMechanisms.Capacity}   {L("吸球器", "intake")} {(Mech.IntakeDown ? L("放下", "DOWN") : L("收起", "up"))}   {L("球道", "chute")} {(AutoPlan.IsRed(GameSession.Station) ? HumanPlayer.RedChute : HumanPlayer.BlueChute)}(H)");
                lines.Add($"{L("飛輪", "flywheel")} {Mech.FlywheelRps:0} rps   {(Mech.Shooting ? (Mech.Ready ? L("發射中", "firing") : L("加速中…", "spinning up…")) : L("待機", "idle"))}   {L("已射", "shots")} {Mech.ShotsFired}");
            }
            if (showDebug)
            {
                Vector2 p = Drive.Pose2d;
                lines.Add($"x {p.x:0.00}  y {p.y:0.00} m   ω {Drive.Omega:0.0} rad/s   {(Drive.FieldCentric ? L("場地座標", "field") : L("車體座標", "robot"))}  {Rig.ModeName}");
                if (Mech != null) lines.Add($"{L("仰角板", "hood")} {Mech.HoodDeg:0.0}°   {L("距 HUB", "hub dist")} {Mech.TargetDistance:0.00} m");
                if (GameSession.Hal != null) lines.Add(L("機器人程式: ", "ROBOT CODE: ") + GameSession.Hal.Status + L("  訊息 ", "  msgs ") + GameSession.Hal.MessagesIn);
            }
            float lh = 22f;
            var pr = new Rect(10, 10, 420, 14 + lines.Count * lh);
            Panel(pr);
            var st = Style(15, Color.white);
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(pr.x + 12, pr.y + 7 + i * lh, pr.width - 16, lh), lines[i], st);
            GUI.Label(new Rect(pr.x + 12, pr.yMax + 2, 300, 20), L("F1 按鍵說明   F3 詳細數據", "F1 help   F3 details"), Style(12, new Color(1f, 1f, 1f, 0.45f)));

            // ---- 頂部中央:比分 + 計時
            float cx = W / 2f;
            Panel(new Rect(cx - 150, 10, 300, 40), 0.6f);
            GUI.Label(new Rect(cx - 150, 12, 300, 38),
                $"{L("藍", "BLUE")} {ScoreManager.BlueScore}  :  {ScoreManager.RedScore} {L("紅", "RED")}", Style(26, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
            if (ScoreManager.ClockOn)
            {
                int tl = Mathf.CeilToInt(ScoreManager.Ended ? 0f : ScoreManager.TimeLeft);
                string hubs = ScoreManager.Ended ? "" : $"   {L("藍", "B")} HUB {(ScoreManager.BlueActive ? "●" : "○")}  {L("紅", "R")} HUB {(ScoreManager.RedActive ? "●" : "○")}";
                Panel(new Rect(cx - 220, 54, 440, 26), 0.5f);
                GUI.Label(new Rect(cx - 220, 56, 440, 24), $"{tl / 60}:{tl % 60:00}   {PhaseName(ScoreManager.Phase)}{hubs}", Style(15, dim, TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式模式的 AUTO 階段:機器人程式自己跑,手把無效(符合真實比賽),提示並可按 N 跳過
            if (GameSession.Hal != null && GameSession.Hal.Connected && ScoreManager.ClockOn && ScoreManager.MatchTime < 20f && !(PreMatch.I != null && PreMatch.I.Active))
            {
                Panel(new Rect(cx - 280, 90, 560, 30), 0.75f);
                GUI.Label(new Rect(cx - 280, 93, 560, 26), L($"自動階段({AutoPlan.Labels[GameSession.Station]}):你的機器人程式自己跑,手把暫時無效 — 按 N 跳過", $"AUTO ({AutoPlan.Labels[GameSession.Station]}): your robot code is driving, sticks disabled — press N to skip"), Style(15, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式模式的開賽前:選自動程式
            var pm = PreMatch.I;
            if (pm != null && pm.Active)
            {
                var list = pm.Nt != null ? pm.Nt.Choosers : new System.Collections.Generic.List<NtChooser.Chooser>();
                float ph = 66 + list.Count * 24;
                Panel(new Rect(cx - 330, 90, 660, ph), 0.8f);
                var head = Style(16, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold);
                GUI.Label(new Rect(cx - 330, 94, 660, 24), L($"開賽前(Disabled,{AutoPlan.Labels[GameSession.Station]})— Enter / A 開始比賽,{pm.SecondsLeft:0} 秒後自動開始", $"PRE-MATCH (disabled, {AutoPlan.Labels[GameSession.Station]}) — Enter / A to start, auto-start in {pm.SecondsLeft:0}s"), head);
                if (list.Count == 0)
                    GUI.Label(new Rect(cx - 330, 120, 660, 22), L("沒找到自動程式選單(SmartDashboard 的 SendableChooser),會跑程式預設的自動", "No auto chooser found on SmartDashboard — the code's default auto will run"), Style(14, dim, TextAnchor.UpperCenter));
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    string mark = i == Mathf.Clamp(pm.ChooserIndex, 0, list.Count - 1) ? "▶ " : "   ";
                    GUI.Label(new Rect(cx - 310, 120 + i * 24, 620, 22), $"{mark}{c.Name}:  ◀ {PreMatch.Current(c)} ▶   ({c.Options.Length} {L("個選項", "options")})", Style(15, Color.white));
                }
                if (list.Count > 0)
                    GUI.Label(new Rect(cx - 330, 122 + list.Count * 24, 660, 22), L("[ / ] 或十字鍵左右 換選項   Tab 或十字鍵上下 換選單", "[ / ] or D-pad L/R: option   Tab or D-pad U/D: chooser"), Style(13, dim, TextAnchor.UpperCenter));
                if (pm.WaitingForMotors)
                {
                    Panel(new Rect(cx - 330, 120 + ph, 660, 26), 0.8f);
                    GUI.Label(new Rect(cx - 330, 123 + ph, 660, 22), L("等待馬達模擬就緒…(機器人程式啟動後約 10 秒)", "Waiting for motor simulation… (~10 s after the robot code starts)"), Style(14, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter));
                }
                if (pm.StartInfo != "")
                {
                    Panel(new Rect(cx - 330, 92 + ph, 660, 26), 0.8f);
                    GUI.Label(new Rect(cx - 330, 95 + ph, 660, 22), L("起點:", "Start: ") + pm.StartInfo, Style(14, Color.white, TextAnchor.UpperCenter));
                }
            }

            // ---- 內建行為的 AUTO 階段:機器人跑內建自動路線(站位 + 目前步驟)
            if (GameSession.Auto != null && AutoDrive.AutoPhase)
            {
                string stp = GameSession.Auto.StepName;
                Panel(new Rect(cx - 300, 90, 600, 30), 0.75f);
                GUI.Label(new Rect(cx - 300, 93, 600, 26), L($"自動階段({AutoPlan.Labels[GameSession.Station]}):內建自動路線 {stp},手把暫時無效 — 按 N 跳過", $"AUTO ({AutoPlan.Labels[GameSession.Station]}): built-in routine {stp}, sticks disabled — press N to skip"), Style(15, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 比賽結束成績單:AUTO / TELEOP / 總分、勝負
            if (ScoreManager.ClockOn && ScoreManager.Ended && GameSession.AutoOnly)
            {
                Panel(new Rect(cx - 240, H * 0.30f, 480, 150), 0.85f);
                GUI.Label(new Rect(cx - 240, H * 0.30f + 10, 480, 34), L("自動模式結束", "AUTO FINISHED"), Style(26, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
                bool me = AutoPlan.IsRed(GameSession.Station);
                int mine = me ? ScoreManager.RedAuto : ScoreManager.BlueAuto;
                GUI.Label(new Rect(cx - 240, H * 0.30f + 52, 480, 26), L($"你的 AUTO 得分:{mine}   (藍 {ScoreManager.BlueAuto} : 紅 {ScoreManager.RedAuto})", $"Your AUTO points: {mine}   (blue {ScoreManager.BlueAuto} : red {ScoreManager.RedAuto})"), Style(18, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                if (GameSession.Chooser != null)
                    foreach (var c in GameSession.Chooser.Choosers) { GUI.Label(new Rect(cx - 240, H * 0.30f + 82, 480, 22), L("跑的自動程式:", "Auto: ") + PreMatch.Current(c), Style(15, dim, TextAnchor.UpperCenter)); break; }
                GUI.Label(new Rect(cx - 240, H * 0.30f + 110, 480, 24), L("Enter / A 再跑一次     Esc 回主選單", "Enter / A run again     Esc main menu"), Style(15, dim, TextAnchor.UpperCenter));
            }
            else if (ScoreManager.ClockOn && ScoreManager.Ended)
            {
                int bt = ScoreManager.BlueScore - ScoreManager.BlueAuto, rt = ScoreManager.RedScore - ScoreManager.RedAuto;
                string win = ScoreManager.BlueScore > ScoreManager.RedScore ? L("藍方獲勝", "BLUE WINS") : ScoreManager.RedScore > ScoreManager.BlueScore ? L("紅方獲勝", "RED WINS") : L("平手", "TIE");
                Panel(new Rect(cx - 220, H * 0.30f, 440, 170), 0.85f);
                GUI.Label(new Rect(cx - 220, H * 0.30f + 10, 440, 34), win, Style(26, new Color(1f, 0.9f, 0.4f), TextAnchor.UpperCenter, FontStyle.Bold));
                var cs = Style(17, Color.white, TextAnchor.UpperCenter);
                GUI.Label(new Rect(cx - 220, H * 0.30f + 52, 440, 24), L("             藍      紅", "             BLUE   RED"), Style(17, dim, TextAnchor.UpperCenter));
                GUI.Label(new Rect(cx - 220, H * 0.30f + 78, 440, 24), $"AUTO        {ScoreManager.BlueAuto,4}   {ScoreManager.RedAuto,4}", cs);
                GUI.Label(new Rect(cx - 220, H * 0.30f + 102, 440, 24), $"TELEOP      {bt,4}   {rt,4}", cs);
                GUI.Label(new Rect(cx - 220, H * 0.30f + 128, 440, 26), $"{L("總分", "TOTAL")}        {ScoreManager.BlueScore,4}   {ScoreManager.RedScore,4}", Style(18, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
            }

            // ---- 真實程式啟動中提示
            if (GameSession.Hal != null && !GameSession.Hal.Connected && !GameSession.Hal.Starting)
            {
                Panel(new Rect(cx - 260, H * 0.42f, 520, 54), 0.75f);
                GUI.Label(new Rect(cx - 260, H * 0.42f + 6, 520, 24), L("機器人程式沒有連上,改用內建操控", "Robot code not connected — using built-in controls"), Style(20, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                GUI.Label(new Rect(cx - 260, H * 0.42f + 30, 520, 22), GameSession.Hal.Status, Style(13, dim, TextAnchor.UpperCenter));
            }
            else if (GameSession.Hal != null && !GameSession.Hal.Connected)
            {
                Panel(new Rect(cx - 260, H * 0.42f, 520, 54), 0.75f);
                GUI.Label(new Rect(cx - 260, H * 0.42f + 6, 520, 24), L("機器人程式啟動中…", "Starting robot code…"), Style(20, Color.white, TextAnchor.UpperCenter, FontStyle.Bold));
                GUI.Label(new Rect(cx - 260, H * 0.42f + 30, 520, 22), L("第一次約 20~60 秒;比賽計時等程式連上才開始(先跑自動階段)", "First start takes 20–60 s; match clock starts (with AUTO) once it connects"), Style(13, dim, TextAnchor.UpperCenter));
            }

            // ---- 在自己的機器人上方標「你」
            if (hudCam == null) hudCam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            var cam = hudCam;
            if (cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(Drive.transform.position + Vector3.up * 1.3f);
                if (SuperSample.Scale > 1.01f) { sp.x /= SuperSample.Scale; sp.y /= SuperSample.Scale; }   // 超取樣時相機像素是螢幕的 Scale 倍
                if (sp.z > 0f)
                {
                    var ys = Style(16, new Color(1f, 0.92f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
                    var shadow = Style(16, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
                    var rc = new Rect(sp.x / sc - 40, (Screen.height - sp.y) / sc - 14 - 30, 80, 28);
                    GUI.Label(new Rect(rc.x + 1.2f, rc.y + 1.2f, rc.width, rc.height), L("▼ 你", "▼ YOU"), shadow);
                    GUI.Label(rc, L("▼ 你", "▼ YOU"), ys);
                }
            }

            // ---- 按鍵說明:開場 15 秒顯示,之後 F1 切換
            bool help = showHelp || Time.unscaledTime - born < 15f;
            if (help)
            {
                bool tank = PlayerPrefs.GetInt("tankMode", 1) == 1;
                string t1 = tank
                    ? L("手把:左/右搖桿 = 左/右側輪   A 放手臂   B 滾輪   RT 發射   RB 送球   十字鍵左右 砲塔   Start 暫停",
                        "Pad: L/R stick = left/right side   A arm   B roller   RT shoot   RB feed   D-pad L/R turret   Start pause")
                    : L("WASD 移動   Q/E 旋轉   Shift 慢速   I 吸球   空白/滑鼠 射擊   F 場地/車體座標", "WASD move   Q/E rotate   Shift slow   I intake   Space/Mouse shoot   F field/robot");
                string t2 = L("H 人類球員放球   C 視角   M 靜音   R 重置   Esc 暫停   2P:方向鍵移動  ,. 旋轉  / 吸球  右Ctrl 射擊  \\ 放球", "H human-player chute   C camera   M mute   R reset   Esc pause   P2: arrows move  ,. rotate  / intake  RCtrl shoot  \\ chute");
                float hw = Mathf.Min(W - 20, 1100);
                Panel(new Rect(cx - hw / 2f, H - 62, hw, 52), 0.6f);
                var hs = Style(14, dim);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - 58, hw - 16, 22), t1, hs);
                GUI.Label(new Rect(cx - hw / 2f + 12, H - 36, hw - 16, 22), t2, hs);
            }
        }
    }
}
