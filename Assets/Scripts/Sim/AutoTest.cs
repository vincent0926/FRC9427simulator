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
        static readonly string[] Keys = { "matchClock", "secondRobot", "useRealCode", "tankMode" };
        static readonly int[] Test = { 1, 1, 0, 1 };
        static int[] saved; static bool[] had;

        public static void SaveAndOverridePrefs()
        {
            saved = new int[Keys.Length]; had = new bool[Keys.Length];
            for (int i = 0; i < Keys.Length; i++) { had[i] = PlayerPrefs.HasKey(Keys[i]); saved[i] = PlayerPrefs.GetInt(Keys[i], 0); PlayerPrefs.SetInt(Keys[i], Test[i]); }
        }

        public static void RestorePrefsPublic() => RestorePrefs();

        static void RestorePrefs()
        {
            if (saved == null) return;
            for (int i = 0; i < Keys.Length; i++) { if (had[i]) PlayerPrefs.SetInt(Keys[i], saved[i]); else PlayerPrefs.DeleteKey(Keys[i]); }
            PlayerPrefs.Save();
        }

        IEnumerator Start()
        {
            var sb = new StringBuilder();
            bool ok = true;
            void Check(bool c, string msg) { sb.AppendLine((c ? "ok   " : "FAIL ") + msg); ok &= c; }

            yield return null;   // 第一幀:還沒開始移動前先檢查起點
            if (GameSession.Hal != null)
            {
                // -fakecode:機器人程式一啟動就結束 → 比賽時鐘不能永遠停在 AUTO,第二台機器人照樣跑自動
                float w0 = Time.time;
                while (GameSession.Hal.Starting && Time.time - w0 < 30f) yield return null;
                sb.AppendLine("robot code status: " + GameSession.Hal.Status);
                Check(!GameSession.Hal.Starting && !GameSession.Hal.Connected, "failed robot code is detected (not 'starting' forever)");
                float m0 = ScoreManager.MatchTime;
                yield return new WaitForSeconds(5f);
                Check(ScoreManager.MatchTime - m0 > 4f, $"match clock runs after robot code failed ({m0:0.0} -> {ScoreManager.MatchTime:0.0})");
                Check(GameSession.Auto2 != null && (GameSession.Auto2.Running || GameSession.Auto2.StepName != "to-shoot"), "robot2 runs its auto");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
                RestorePrefs();
                Application.Quit();
                yield break;
            }
            firstAttachMs = RobotModels.LastAttachMs;
            var d = GameSession.Drive; var m = GameSession.Mech;
            var a1 = GameSession.Auto; var a2 = GameSession.Auto2;
            Check(a1 != null && a2 != null, "both robots have AutoDrive");
            AutoPlan.StartPose(GameSession.Station, out float sx, out float sy, out _);
            sb.AppendLine($"routine={a1?.Routine} station={AutoPlan.Names[GameSession.Station]} start=({sx:0.00},{sy:0.00}) robot=({d.Pose2d.x:0.00},{d.Pose2d.y:0.00})");
            Check(Vector2.Distance(d.Pose2d, new Vector2(sx, sy)) < 0.3f, "player robot spawned at its station");
            Check(AutoDrive.AutoPhase, "match starts in AUTO");

            float t0 = Time.time;
            while (ScoreManager.Phase == "AUTO" && Time.time - t0 < 25f) yield return null;
            yield return null; yield return null;   // 讓 AutoDrive 這一幀看到階段改變
            sb.AppendLine($"after AUTO: blue={ScoreManager.BlueScore} red={ScoreManager.RedScore} shots p1={m.ShotsFired} held={m.Held} step={a1?.StepName}");
            var m2 = a2 != null ? a2.GetComponent<RobotMechanisms>() : null;
            if (m2 != null) sb.AppendLine($"robot2 shots={m2.ShotsFired} held={m2.Held} step={a2.StepName}");
            if (a1.Routine == AutoRoutine.None)
            {
                Check(m.ShotsFired == 0 && ScoreManager.BlueScore + ScoreManager.RedScore == 0, "'do nothing' routine fires nothing");
                Check(Vector2.Distance(d.Pose2d, new Vector2(sx, sy)) < 0.3f, "'do nothing' routine stays at the start");
            }
            else
            {
                Check(m.ShotsFired >= 8, "player robot fired its 8 preloaded FUEL in AUTO");
                Check(m2 != null && m2.ShotsFired >= 8, "robot2 fired its 8 preloaded FUEL in AUTO");
                Check(ScoreManager.BlueScore >= 4 && ScoreManager.RedScore >= 4, "both alliances scored in AUTO");
                var eff = AutoPlan.Resolve(GameSession.Station, a1.Routine);
                if (eff == AutoRoutine.Depot || eff == AutoRoutine.Neutral) Check(m.ShotsFired > 8 && a1.StepName == "done", $"{eff} routine collected more FUEL and finished ({m.ShotsFired} shots)");
            }
            Check(!a1.Running && !a2.Running, "auto stopped after AUTO phase");

            // 比賽提示音 + 結束成績單:把時鐘快轉到各階段交界
            Check(MatchAudio.Cues.Contains("start"), "match-start cue played");
            int blueAuto = ScoreManager.BlueScore, redAuto = ScoreManager.RedScore;
            Check(ScoreManager.BlueAuto == blueAuto && ScoreManager.RedAuto == redAuto, $"AUTO points recorded for the summary (blue {ScoreManager.BlueAuto}, red {ScoreManager.RedAuto})");
            ScoreManager.MatchTime = 29.5f; yield return new WaitForSeconds(1.5f);
            ScoreManager.MatchTime = 129.5f; yield return new WaitForSeconds(1.5f);
            ScoreManager.MatchTime = 159.5f; yield return new WaitForSeconds(1.5f);
            ScoreManager.MatchTime = 162.8f; yield return new WaitForSeconds(1.0f);
            sb.AppendLine("cues: " + MatchAudio.Cues);
            Check(MatchAudio.Cues.Contains("teleop") && MatchAudio.Cues.Contains("endgame") && MatchAudio.Cues.Contains("buzzer"), "teleop / endgame / end-buzzer cues played");
            Check(ScoreManager.Ended && ScoreManager.BlueAuto == blueAuto, "match ended; summary keeps AUTO points");

            // 匯入的機器人模型:零件合併成少數網格(卡頓主因是 573 個零件各自一次 draw call)
            if (RobotModels.Selected != "")
            {
                sb.AppendLine($"robot model {RobotModels.Selected}: draw parts {RobotModels.LastDrawParts}");
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nomerge") < 0) Check(RobotModels.LastDrawParts > 0 && RobotModels.LastDrawParts < 80, "robot model merged into < 80 meshes");
                firstAttachMs = RobotModels.LastAttachMs;
                sb.AppendLine($"first model load: {firstAttachMs} ms");
                // 第二次掛同一個模型(像下一場比賽):要用快取樣板,幾乎不花時間
                var dummy = new GameObject("CacheProbe"); bool done2 = false, ok2 = false;
                RobotModels.Attach(dummy.transform, RobotModels.Selected, RobotModels.YawDeg, r => { ok2 = r; done2 = true; });
                float tc = Time.realtimeSinceStartup;
                while (!done2 && Time.realtimeSinceStartup - tc < 10f) yield return null;
                Check(ok2 && RobotModels.LastAttachMs < 300, $"next match reuses cached model ({RobotModels.LastAttachMs} ms)");
                Check(dummy.transform.Find("ModelPivot") != null && dummy.GetComponentsInChildren<MeshRenderer>().Length < 80, "cached copy has the merged meshes");
                Destroy(dummy);
            }

            if (Perf) sb.AppendLine(PerfLine());

            // 再開一場(像回主選單後按開始):開賽那一幀不能卡,之後幾秒也不能有大於 100 ms 的卡頓
            var rig = FindFirstObjectByType<CameraRig>();
            if (rig != null)
            {
                GameSession.End(rig);
                yield return null;
                var swb = System.Diagnostics.Stopwatch.StartNew();
                GameSession.Begin(rig);
                long beginMs = swb.ElapsedMilliseconds;
                worstFrame = 0f;
                yield return new WaitForSeconds(4f);
                sb.AppendLine($"second match: Begin() {beginMs} ms, worst frame in next 4 s {worstFrame:0} ms, model attach {RobotModels.LastAttachMs} ms");
                Check(beginMs < 500 && worstFrame < 150f, "starting the next match does not freeze");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            RestorePrefs();
            Application.Quit();
        }

        // -perf:記錄每幀時間(視窗模式才有實際算圖),結果寫在 autotest.txt
        readonly System.Collections.Generic.List<float> frames = new System.Collections.Generic.List<float>();
        float firstAttachMs, worstFrame;
        static bool Perf => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perf") >= 0;
        void Update()
        {
            if (Time.frameCount > 2) worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime * 1000f);
            if (Perf && Time.frameCount > 60 && ScoreManager.Phase == "AUTO") frames.Add(Time.unscaledDeltaTime * 1000f);
        }
        string PerfLine()
        {
            if (frames.Count == 0) return "perf: no frames";
            var f = new System.Collections.Generic.List<float>(frames); f.Sort();
            float avg = 0f; foreach (var x in f) avg += x; avg /= f.Count;
            int hitches = 0; foreach (var x in f) if (x > 50f) hitches++;
            return $"perf: worst frame since start={worstFrame:0} ms; frames={f.Count} avg={avg:0.0} ms p95={f[(int)(f.Count * 0.95f)]:0.0} ms max={f[f.Count - 1]:0.0} ms hitches(>50ms)={hitches} drawParts={RobotModels.LastDrawParts}";
        }
    }
}
