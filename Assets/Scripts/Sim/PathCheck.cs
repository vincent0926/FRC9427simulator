using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 測試用:讀 PathPlanner 自動程式裡所有路徑(.path 的貝茲曲線),量機器人實際走的點離路線多遠。
    public static class PathCheck
    {
        public class Curve { public string Name; public List<Vector2> Points = new List<Vector2>(); }

        public static List<Curve> Load(string projectDir, string autoName, bool red)
        {
            var list = new List<Curve>();
            try
            {
                string root = Path.Combine(projectDir, "src", "main", "deploy", "pathplanner");
                var auto = JObject.Parse(File.ReadAllText(Path.Combine(root, "autos", autoName + ".auto")));
                var names = new List<string>();
                Collect(auto["command"], names);
                foreach (var n in names)
                {
                    var p = JObject.Parse(File.ReadAllText(Path.Combine(root, "paths", n + ".path")));
                    var wps = (JArray)p["waypoints"];
                    var c = new Curve { Name = n };
                    for (int i = 0; i + 1 < wps.Count; i++)
                    {
                        Vector2 P(JToken t) => t == null || t.Type == JTokenType.Null ? Vector2.zero : new Vector2((float)t["x"], (float)t["y"]);
                        Vector2 p0 = P(wps[i]["anchor"]), p3 = P(wps[i + 1]["anchor"]);
                        Vector2 p1 = wps[i]["nextControl"] is JObject ? P(wps[i]["nextControl"]) : p0;
                        Vector2 p2 = wps[i + 1]["prevControl"] is JObject ? P(wps[i + 1]["prevControl"]) : p3;
                        for (int k = 0; k <= 40; k++)
                        {
                            float t = k / 40f, u = 1 - t;
                            Vector2 q = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
                            if (red) q = new Vector2(AutoPlan.FieldLength - q.x, AutoPlan.FieldWidth - q.y);
                            c.Points.Add(q);
                        }
                    }
                    list.Add(c);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("PathCheck: " + e.Message); }
            return list;
        }

        static void Collect(JToken cmd, List<string> names)
        {
            if (cmd == null) return;
            if ((string)cmd["type"] == "path") { names.Add((string)cmd["data"]?["pathName"]); return; }
            if (cmd["data"]?["commands"] is JArray a) foreach (var c in a) Collect(c, names);
        }

        // 點到所有路線的最短距離,並回報最近的是哪一條
        public static float Distance(List<Curve> curves, Vector2 p, out string which)
        {
            float best = float.MaxValue; which = "-";
            foreach (var c in curves)
                for (int i = 0; i + 1 < c.Points.Count; i++)
                {
                    Vector2 a = c.Points[i], b = c.Points[i + 1], ab = b - a;
                    float t = ab.sqrMagnitude < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float d = Vector2.Distance(p, a + t * ab);
                    if (d < best) { best = d; which = c.Name; }
                }
            return best;
        }
    }
}
