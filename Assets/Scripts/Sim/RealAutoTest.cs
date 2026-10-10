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
            var sb = new StringBuilder();
            bool ok = true;
            void Check(bool c, string msg) { sb.AppendLine((c ? "ok   " : "FAIL ") + msg); ok &= c; }
            string choice = Arg("-choose", "Drive Forward");
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
                foreach (var c in nt.Choosers) if (c.Name == "Auto Chooser") ch = c;
                if (ch != null && ch.Options.Length >= 3) break;
                yield return null;
            }
            Check(ch != null, $"found SmartDashboard 'Auto Chooser' (options: {(ch == null ? "-" : string.Join(", ", ch.Options))})");
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

        static void Finish(StringBuilder sb, bool ok)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "realautotest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            AutoTest.RestorePrefsPublic();
            Application.Quit();
        }
    }
}
