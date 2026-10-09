using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -autotest [-station blue1]:內建行為 + 比賽時鐘,看 AUTO 20 秒內兩台機器人有沒有照內建自動路線開到射擊點、射完預載、得分;
    // AUTO 期間手把應無效,AUTO 結束後應交還手動。結果寫到 autotest.txt(最後一行 PASS / FAIL)
    public class AutoTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            bool ok = true;
            void Check(bool c, string msg) { sb.AppendLine((c ? "ok   " : "FAIL ") + msg); ok &= c; }

            yield return null;   // 第一幀:還沒開始移動前先檢查起點
            var d = GameSession.Drive; var m = GameSession.Mech;
            var a1 = GameSession.Auto; var a2 = GameSession.Auto2;
            Check(a1 != null && a2 != null, "both robots have AutoDrive");
            AutoPlan.StartPose(GameSession.Station, out float sx, out float sy, out _);
            sb.AppendLine($"station={AutoPlan.Names[GameSession.Station]} start=({sx:0.00},{sy:0.00}) robot=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00})");
            Check(Vector2.Distance(d.Pose2d, new Vector2(sx, sy)) < 0.3f, "player robot spawned at its station");
            Check(AutoDrive.AutoPhase, "match starts in AUTO");

            float t0 = Time.time;
            while (ScoreManager.Phase == "AUTO" && Time.time - t0 < 25f) yield return null;
            yield return null; yield return null;   // 讓 AutoDrive 這一幀看到階段改變
            sb.AppendLine($"after AUTO: blue={ScoreManager.BlueScore} red={ScoreManager.RedScore} shots p1={m.ShotsFired} held={m.Held} step={a1?.StepName}");
            var m2 = a2 != null ? a2.GetComponent<RobotMechanisms>() : null;
            if (m2 != null) sb.AppendLine($"robot2 shots={m2.ShotsFired} held={m2.Held} step={a2.StepName}");
            Check(m.ShotsFired >= 8, "player robot fired its 8 preloaded FUEL in AUTO");
            Check(m2 != null && m2.ShotsFired >= 8, "robot2 fired its 8 preloaded FUEL in AUTO");
            Check(ScoreManager.BlueScore >= 4 && ScoreManager.RedScore >= 4, "both alliances scored in AUTO");
            Check(!a1.Running && !a2.Running, "auto stopped after AUTO phase");

            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            Application.Quit();
        }
    }
}
