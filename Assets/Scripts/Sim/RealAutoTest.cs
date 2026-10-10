using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -realautotest <機器人專案> [-station red2] [-choose "Drive Forward"]:真實程式模式的自動階段端到端測試。
    // 專案要有 SmartDashboard 的 "Auto Chooser"(SendableChooser),autonomousInit 把 SimTest/AutoRan、SimTest/Alliance、
    // SimTest/StartPose、SimTest/Station 寫到 SmartDashboard(範例:LEO 測試副本)。結果寫到 realautotest.txt(最後一行 PASS / FAIL)
    public class RealAutoTest : MonoBehaviour
    {
        static string Arg(string name, string def)
        {
            var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        IEnumerator Start()
        {
            var sb = new StringBuilder(); sbRef = sb;
            bool ok = true;
            void Check(bool c, string msg) { sb.AppendLine((c ? "ok   " : "FAIL ") + msg); ok &= c; }
            string choice = Arg("-choose", "Drive Forward");
            string chooserName = Arg("-chooser", "Auto Chooser");
            bool generic = chooserName != "Auto Chooser";   // 使用者自己的專案:沒有 SimTest/* 回報,只檢查選單與效能
            var hal = GameSession.Hal; var nt = GameSession.Chooser; var d = GameSession.Drive;
            Check(hal != null && nt != null && PreMatch.I != null, "real-code mode with chooser client and pre-match");
            if (hal == null || nt == null || PreMatch.I == null) { Finish(sb, false); yield break; }

            float t0 = Time.realtimeSinceStartup;
            while (!hal.Connected && hal.Starting && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            sb.AppendLine($"robot code: {hal.Status} after {Time.realtimeSinceStartup - t0:0} s");
            Check(hal.Connected, "robot code connected");
            if (!hal.Connected) { Finish(sb, false); yield break; }

            float t1 = Time.realtimeSinceStartup;
            NtChooser.Chooser ch = null;
            while (Time.realtimeSinceStartup - t1 < 30f)
            {
                foreach (var c in nt.Choosers) if (c.Name == chooserName) ch = c;
                if (ch != null && ch.Options.Length >= (generic ? 1 : 3)) break;
                yield return null;
            }
            Check(ch != null, $"found SmartDashboard '{chooserName}' (options: {(ch == null ? "-" : string.Join(", ", ch.Options))})");
            Check(PreMatch.I.Active && !hal.Enabled && ScoreManager.MatchTime == 0f, "pre-match: robot disabled, match clock not started");
            if (ch == null) { Finish(sb, false); yield break; }

            nt.Select(ch, choice);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(PreMatch.Current(ch) == choice, $"selected '{choice}'");
            Vector2 p0 = d.Pose2d; float h0 = d.HeadingRad;
            PreMatch.I.StartMatch();
            float maxV = 0f, maxVl = 0f; string sample = "";
            var msim0 = d.GetComponent<MechSim>();
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 5f)
            {
                float v = Mathf.Abs(hal.Num("CANMotor/Talon FX (v6)[3]", "<motorVoltage"));
                if (v > maxV) { maxV = v; sample = msim0 != null ? msim0.Debug : ""; }
                maxVl = Mathf.Max(maxVl, d.Speed);
                yield return null;
            }
            sb.AppendLine($"during AUTO: max |motorVoltage[3]|={maxV:0.00} V, max speed={maxVl:0.00} m/s, mechsim at max: {sample}, enabled={hal.Enabled} auto={hal.Autonomous}");
            Check(ScoreManager.MatchTime > 3f && ScoreManager.Phase == "AUTO", $"match running in AUTO (t={ScoreManager.MatchTime:0.0})");

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-automode") >= 0)
            {
                // 自動模式:AUTO 20 秒 + 3 秒寬限後結束、機器人 Disabled;再跑一次(Restart)程式要能重新連上
                float te = Time.realtimeSinceStartup; float maxMove = 0f, maxTurn = 0f, path = 0f; Vector2 last = d.Pose2d; bool shot = false;
                var trace = new StringBuilder();
                while (!ScoreManager.Ended && Time.realtimeSinceStartup - te < 40f)
                {
                    if (GameSession.Drive != null)
                    {
                        var pp = GameSession.Drive.Pose2d;
                        path += Vector2.Distance(pp, last); last = pp;
                        maxMove = Mathf.Max(maxMove, Vector2.Distance(pp, p0));
                        maxTurn = Mathf.Max(maxTurn, Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, GameSession.Drive.HeadingRad * Mathf.Rad2Deg)));
                        if (Time.frameCount % 60 == 0 && trace.Length < 2000) trace.Append($"t={ScoreManager.MatchTime:0.0} ({pp.x:0.00},{pp.y:0.00}) ");
                    }
                    // AUTO 中途截圖(-autoshot 路徑):看機器人有沒有照自動路線動
                    string shotPath = Arg("-autoshot", null);
                    if (!shot && shotPath != null && ScoreManager.MatchTime > 8f) { shot = true; ScreenCapture.CaptureScreenshot(shotPath); }
                    yield return null;
                }
                sb.AppendLine($"during AUTO: max distance from start {maxMove:0.00} m, path {path:0.00} m, max turn {maxTurn:0} deg");
                sb.AppendLine("trace: " + trace);
                Warn2(maxMove > 0.5f, "robot drove during the user's auto");
                Check(ScoreManager.Ended && ScoreManager.Phase == "AUTO DONE" && !hal.Enabled, $"auto mode ends after AUTO (phase={ScoreManager.Phase}, t={ScoreManager.MatchTime:0.0}, enabled={hal.Enabled})");
                nt.Strings.TryGetValue("SimTest/AutoRan", out string ran1);
                Check(generic || ran1 == choice, $"robot code ran '{ran1}'");
                var rig = FindFirstObjectByType<CameraRig>();
                GameSession.Restart(rig);
                hal = GameSession.Hal; nt = GameSession.Chooser;
                float tr = Time.realtimeSinceStartup;
                while (hal != null && !hal.Connected && hal.Starting && Time.realtimeSinceStartup - tr < 300f) yield return null;
                sb.AppendLine($"restart: {(hal == null ? "no hal" : hal.Status)} after {Time.realtimeSinceStartup - tr:0} s");
                Check(hal != null && hal.Connected, "run again: robot code starts again (no log-file sharing violation)");
                float tc = Time.realtimeSinceStartup;
                while (PreMatch.I != null && PreMatch.I.Active && !PreMatch.I.HasChooser && Time.realtimeSinceStartup - tc < 30f) yield return null;
                Check(PreMatch.I != null && PreMatch.I.HasChooser, "run again: auto chooser found again");
                Finish(sb, ok);
                yield break;
            }
            if (generic)
            {
                yield return new WaitForSecondsRealtime(10f);   // 多跑一點時間收集效能數據
                sb.AppendLine($"robot moved {(d.Pose2d - p0).magnitude:0.00} m; mechsim: {(d.GetComponent<MechSim>() != null ? d.GetComponent<MechSim>().Info : "none")}");
                Finish(sb, ok);
                yield break;
            }
            nt.Strings.TryGetValue("SimTest/AutoRan", out string ran);
            nt.Strings.TryGetValue("SimTest/Alliance", out string alliance);
            nt.Strings.TryGetValue("SimTest/StartPose", out string start);
            nt.Strings.TryGetValue("SimTest/Station", out string station);
            sb.AppendLine($"robot code reported: AutoRan={ran} Alliance={alliance} StartPose={start} Station={station}");
            Check(ran == choice, "robot code ran the auto picked in the simulator");
            string want = (AutoPlan.IsRed(GameSession.Station) ? "Red" : "Blue") + AutoPlan.Number(GameSession.Station);
            Check(alliance == want, $"DriverStation.getAlliance()/getLocation() = {want}");
            AutoPlan.StartPose(GameSession.Station, out float sx, out float sy, out float sd);
            bool startOk = false;
            if (start != null)
            {
                var parts = start.Split(',');
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                startOk = parts.Length == 3 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out float rx) && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out float ry) && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, inv, out float rd)
                    && Mathf.Abs(rx - sx) < 0.02f && Mathf.Abs(ry - sy) < 0.02f && Mathf.Abs(Mathf.DeltaAngle(rd, sd)) < 1f;
            }
            Check(startOk, $"FrcSimPose.startPose() = {sx:0.00},{sy:0.00},{sd:0}");
            Check(station == AutoPlan.Names[GameSession.Station], "FrcSimPose.station()");

            var msim = d.GetComponent<MechSim>();
            sb.AppendLine("mechsim: " + (msim != null ? msim.Info : "none") + "  simDriven=" + d.SimDriven);
            foreach (var line in hal.Summary().Split('\n')) if (line.Contains("CANMotor") && (line.Contains("[1]") || line.Contains("[3]"))) sb.AppendLine("  " + line.Trim());
            Vector2 dp = d.Pose2d - p0;
            float along = dp.x * Mathf.Cos(h0) + dp.y * Mathf.Sin(h0);
            float turned = Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg));
            sb.AppendLine($"robot moved {dp.magnitude:0.00} m (forward {along:0.00} m), turned {turned:0} deg");
            // 底盤動不動取決於 simagent(Java agent 的馬達物理),不是這次自動模式的範圍:只記錄 WARN,不算 FAIL
            void Warn(bool c, string msg) => sb.AppendLine((c ? "ok   " : "WARN ") + msg);
            if (choice == "Drive Forward") Warn(along > 0.5f, "'Drive Forward' auto moved the robot forward");
            if (choice == "Spin") Warn(turned > 20f && dp.magnitude < 0.5f, "'Spin' auto turned the robot in place");
            if (choice == "Do Nothing") Check(dp.magnitude < 0.1f && turned < 3f, "'Do Nothing' auto kept the robot still");
            Finish(sb, ok);
        }

        // 每幀時間:分「程式啟動中」和「已連線」兩段統計(視窗模式才有實際算圖)
        static readonly System.Collections.Generic.List<float> bootFrames = new System.Collections.Generic.List<float>(), liveFrames = new System.Collections.Generic.List<float>();
        void Update()
        {
            if (Time.frameCount < 60 || GameSession.Hal == null) return;
            (GameSession.Hal.Connected ? liveFrames : bootFrames).Add(Time.unscaledDeltaTime * 1000f);
        }
        static string Stats(string name, System.Collections.Generic.List<float> l)
        {
            if (l.Count == 0) return name + ": no frames";
            var f = new System.Collections.Generic.List<float>(l); f.Sort();
            float avg = 0f; foreach (var x in f) avg += x; avg /= f.Count;
            int h = 0; foreach (var x in f) if (x > 50f) h++;
            return $"{name}: frames={f.Count} avg={avg:0.0} ms p95={f[(int)(f.Count * 0.95f)]:0.0} ms p99={f[(int)(f.Count * 0.99f)]:0.0} ms max={f[f.Count - 1]:0.0} ms hitches(>50ms)={h}";
        }

        void Warn2(bool c, string msg) { lastWarn = (c ? "ok   " : "WARN ") + msg; sbRef?.AppendLine(lastWarn); }
        StringBuilder sbRef; string lastWarn;

        static void Finish(StringBuilder sb, bool ok)
        {
            sb.AppendLine(Stats("perf while robot code boots", bootFrames));
            sb.AppendLine(Stats("perf while connected", liveFrames));
            if (GameSession.Hal != null) sb.AppendLine($"halsim: maxBacklog={GameSession.Hal.MaxBacklog} maxDt={GameSession.Hal.MaxDt * 1000f:0} ms");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "realautotest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            AutoTest.RestorePrefsPublic();
            Application.Quit();
        }
    }
}
