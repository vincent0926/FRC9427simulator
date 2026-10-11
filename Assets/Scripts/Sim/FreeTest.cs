using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -freetest:自由練習(內建行為、比賽計時開著)。不跑自動、直接從 TELEOP 開始;沒按任何鍵時車子不能自己動;手把一開始就能開車。
    // 結果 freetest.txt(最後一行 PASS / FAIL)
    public class FreeTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            bool ok = true;
            void Check(bool c, string m) { sb.AppendLine((c ? "ok   " : "FAIL ") + m); ok &= c; }
            yield return null;
            var d = GameSession.Drive;
            Check(GameSession.Auto == null, "player robot has no built-in auto in free practice");
            Check(ScoreManager.ClockOn && ScoreManager.Phase == "SHIFT 1" && ScoreManager.MatchTime >= 30f, $"free practice starts in TELEOP (phase {ScoreManager.Phase}, t={ScoreManager.MatchTime:0.0})");
            Check(GameSession.Auto2 == null, "second robot has no auto in free practice");
            Check(PreMatch.I == null || PreMatch.I.FreePlay, "no pre-match auto chooser in free practice");
            Pad.SetFake(true);   // 手把放開
            Vector2 p0 = d.Pose2d; float h0 = d.HeadingRad;
            yield return new WaitForSeconds(4f);
            float moved = (d.Pose2d - p0).magnitude, turned = Mathf.Abs(Mathf.DeltaAngle(h0 * Mathf.Rad2Deg, d.HeadingRad * Mathf.Rad2Deg));
            Check(moved < 0.05f && turned < 2f, $"no input for 4 s: robot stays still (moved {moved:0.000} m, turned {turned:0.0} deg)");
            // 推搖桿(坦克:兩支往後;2 號站位正前方就是 HUB,往前會直接撞上)
            p0 = d.Pose2d;
            Pad.SetFake(true, ly: -1f, ry: -1f);
            yield return new WaitForSeconds(1.5f);
            Pad.SetFake(true);
            float drove = (d.Pose2d - p0).magnitude;
            Check(drove > 1f, $"sticks drive the robot right away (moved {drove:0.00} m)");
            yield return new WaitForSeconds(1f);
            p0 = d.Pose2d;
            yield return new WaitForSeconds(2f);
            Check((d.Pose2d - p0).magnitude < 0.05f, "released sticks: robot stops and stays");
            Pad.SetFake(false);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "freetest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            AutoTest.RestorePrefsPublic();
            Application.Quit();
        }
    }
}
