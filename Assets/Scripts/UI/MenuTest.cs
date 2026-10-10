using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FrcSim
{
    // -menutest [-menushot 路徑.png]:正常開啟主選單,逐一打開每個選單畫面(主選單、模式、自由練習設定、自動模式設定、設定、操作說明),
    // 任何例外(例如 Loc 字典重複 key 造成整個選單黑畫面)都算 FAIL。結果 menutest.txt(最後一行 PASS / FAIL)
    public class MenuTest : MonoBehaviour
    {
        public MenuSystem Menu;
        readonly StringBuilder errors = new StringBuilder();
        int errorCount;

        void Awake() { Application.logMessageReceived += OnLog; }
        void OnDestroy() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error) { errorCount++; if (errors.Length < 4000) errors.AppendLine(type + ": " + msg); }
        }

        IEnumerator Start()
        {
            var sb = new StringBuilder();
            bool ok = true;
            void Check(bool c, string m) { sb.AppendLine((c ? "ok   " : "FAIL ") + m); ok &= c; }
            yield return new WaitForSeconds(1.5f);
            Check(MenuSystem.Blocking, "main menu is showing");
            try { Check(!string.IsNullOrEmpty(Loc.T("mode.auto")) && Loc.T("mode.auto") != "mode.auto", "localization table loads (mode.auto = " + Loc.T("mode.auto") + ")"); }
            catch (System.Exception e) { Check(false, "localization table loads: " + e.GetType().Name + " " + e.Message); }

            var t = typeof(MenuSystem);
            foreach (var name in new[] { "ShowMain", "ShowModes", "ShowRobotSetup", "ShowAutoSetup", "ShowControls", "ShowSettings", "ShowMain" })
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (m == null) { Check(false, name + " exists"); continue; }
                int before = errorCount;
                try { m.Invoke(Menu, m.GetParameters().Length == 1 ? new object[] { false } : null); }
                catch (System.Exception e) { Check(false, name + ": " + (e.InnerException ?? e).Message); continue; }
                yield return new WaitForSeconds(0.4f);
                Check(errorCount == before, name + " opens without errors");
            }

            string shot = null;
            var a = System.Environment.GetCommandLineArgs(); int si = System.Array.IndexOf(a, "-menushot");
            if (si >= 0 && si + 1 < a.Length) shot = a[si + 1];
            if (shot != null) { ScreenCapture.CaptureScreenshot(shot); yield return new WaitForSeconds(1.5f); }

            Check(errorCount == 0, $"no errors/exceptions logged ({errorCount})");
            if (errorCount > 0) sb.Append(errors);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "menutest.txt"), sb.ToString() + (ok ? "PASS" : "FAIL") + "\n");
            Application.Quit();
        }
    }
}
