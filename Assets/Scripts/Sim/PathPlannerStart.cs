using System.IO;
using Newtonsoft.Json.Linq;

namespace FrcSim
{
    // 讀機器人專案裡 PathPlanner 的自動程式(src/main/deploy/pathplanner/autos/<名稱>.auto),找出它的起點:
    //   .auto 有 startingPose 就用它;否則用第一條路徑(.path)的第一個路徑點 + idealStartingState 的朝向。
    // 這樣模擬器能把機器人擺在自動路線的起點(和實際比賽擺車一樣),路線才會照著跑。座標是藍方(PathPlanner 原始座標)。
    public static class PathPlannerStart
    {
        public static bool TryGet(string projectDir, string autoName, out float x, out float y, out float headingDeg)
        {
            x = y = headingDeg = 0f;
            if (string.IsNullOrEmpty(projectDir) || string.IsNullOrEmpty(autoName)) return false;
            try
            {
                string root = Path.Combine(projectDir, "src", "main", "deploy", "pathplanner");
                string autoFile = Path.Combine(root, "autos", autoName + ".auto");
                if (!File.Exists(autoFile)) return false;
                var auto = JObject.Parse(File.ReadAllText(autoFile));
                var sp = auto["startingPose"] as JObject;
                if (sp != null && sp["position"] is JObject pos)
                {
                    x = (float)pos["x"]; y = (float)pos["y"]; headingDeg = (float?)sp["rotation"] ?? 0f;
                    return true;
                }
                string pathName = FirstPath(auto["command"]);
                if (pathName == null) return false;
                string pathFile = Path.Combine(root, "paths", pathName + ".path");
                if (!File.Exists(pathFile)) return false;
                var path = JObject.Parse(File.ReadAllText(pathFile));
                var wp = (path["waypoints"] as JArray)?[0]?["anchor"];
                if (wp == null) return false;
                x = (float)wp["x"]; y = (float)wp["y"];
                headingDeg = (float?)path["idealStartingState"]?["rotation"] ?? (float?)path["previewStartingState"]?["rotation"] ?? 0f;
                return true;
            }
            catch { return false; }
        }

        // 依序找第一個 type = "path" 的指令(sequential / parallel / deadline / race 群組會往裡面找)
        static string FirstPath(JToken cmd)
        {
            if (cmd == null) return null;
            if ((string)cmd["type"] == "path") return (string)cmd["data"]?["pathName"];
            if (cmd["data"]?["commands"] is JArray list)
                foreach (var c in list) { var p = FirstPath(c); if (p != null) return p; }
            return null;
        }
    }
}
