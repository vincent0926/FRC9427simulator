using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -shottest:內建行為的射擊命中率。把機器人放在自己聯盟區與中立區(中場)各點,每點射 8 顆,數進 HUB 的球。
    // 結果 shottest.txt:每點一行,最後一行 PASS(每個點至少 6/8 進)/ FAIL
    public class ShotTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            var sb = new StringBuilder();
            yield return new WaitForSeconds(1f);
            var d = GameSession.Drive; var m = GameSession.Mech;
            var ri = d.GetComponent<RobotInput>(); if (ri != null) ri.enabled = false;
            var ad = d.GetComponent<AutoDrive>(); if (ad != null) ad.enabled = false;
            ScoreManager.ClockOn = false;   // 自由練習:HUB 一律啟動
            int worst = 8, total = 0, shots = 0;
            float[] xs = { 2.2f, 6.0f, 7.0f, 8.0f, 8.5f, 9.0f, 9.5f, 10.0f };
            float[] ys = { 1.5f, 4.035f, 6.6f };
            foreach (float x in xs)
                foreach (float y in ys)
                {
                    if (Mathf.Abs(x - 4.63f) < 1.4f && Mathf.Abs(y - 4.035f) < 1.4f) continue;   // HUB 本體旁邊跳過
                    d.SetPose(new Vector2(x, y), 0f);
                    d.Drive(0f, 0f, 0f);
                    m.Shooting = false; m.IntakeDown = false;
                    yield return new WaitForSeconds(0.5f);
                    m.Held = 8;
                    int before = ScoreManager.BlueScore, fired0 = m.ShotsFired;
                    m.Shooting = true;
                    float t0 = Time.time;
                    while (m.Held > 0 && Time.time - t0 < 6f) yield return null;
                    m.Shooting = false;
                    yield return new WaitForSeconds(3f);   // 等球落下
                    int fired = m.ShotsFired - fired0, scored = ScoreManager.BlueScore - before;
                    float dist = Vector2.Distance(new Vector2(x, y), RobotMechanisms.BlueHub);
                    sb.AppendLine($"x={x:0.0} y={y:0.00} dist={dist:0.00} m  fired={fired} scored={scored}  hood={m.HoodDeg:0.0} fly={m.FlywheelRps:0}");
                    worst = Mathf.Min(worst, scored); total += scored; shots += fired;
                }
            bool ok = worst >= 6;
            sb.AppendLine($"total {total}/{shots}, worst spot {worst}/8");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "shottest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            Application.Quit();
        }
    }
}
