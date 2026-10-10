using System;
using System.IO;
using System.Linq;
using UnityEngine;
using GLTFast;

namespace FrcSim
{
    // 匯入機器人 3D 模型(.glb):放在 exe 旁邊的 Robots 資料夾,設定裡切換。
    // Onshape/CAD 匯出的模型座標:Y 向上、單位公尺。ModelYawDeg 用來把車頭對到模擬器的 +X。
    public static class RobotModels
    {
        public static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Robots");

        public static string[] List()
        {
            try
            {
                if (!Directory.Exists(Dir)) return new string[0];
                return Directory.GetFiles(Dir, "*.glb").Select(Path.GetFileName).OrderBy(x => x).ToArray();
            }
            catch { return new string[0]; }
        }

        // "" = 內建方塊。第一次沒設定過就用資料夾裡的第一個模型。
        public static string Selected
        {
            get
            {
                if (PlayerPrefs.HasKey("robotModel"))
                {
                    string s = PlayerPrefs.GetString("robotModel");
                    return s == "" || List().Contains(s) ? s : "";
                }
                var l = List();
                return l.Length > 0 ? l[0] : "";
            }
            set { PlayerPrefs.SetString("robotModel", value); PlayerPrefs.Save(); }
        }

        public static float YawDeg
        {
            // kepler.glb 的 intake 在模型 +z 側,車頭(+x)要轉 90° 才對;使用者沒調過時預設用這個
            get => PlayerPrefs.GetFloat("modelYaw", Selected == "kepler.glb" ? 90f : 0f);
            set { PlayerPrefs.SetFloat("modelYaw", value); PlayerPrefs.Save(); }
        }

        public static string LastError = "";

        // 載好、合併好的模型留一份樣板(停用、不會被場景切換刪掉):之後每場比賽直接複製,不用再讀 45 MB 的 glb
        // (原本每次開賽都重新載入,開賽瞬間會卡 2~3 秒)
        static GameObject template; static string templateKey;
        public static bool Loading { get; private set; }
        public static float LastAttachMs;   // 測試用:上一次掛模型花的時間

        static string Key(string file, float yawDeg) => file + "|" + yawDeg.ToString("0.0");

        static void HideBuiltin(Transform robot)
        {
            foreach (var n in new[] { "Body", "IntakeArm", "Turret" })
            {
                var t = robot.Find(n);
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        // 在主選單時先在背景載好目前選的模型,開賽時就不用等
        public static void Preload()
        {
            string file = Selected;
            if (string.IsNullOrEmpty(file) || Loading || (template != null && templateKey == Key(file, YawDeg))) return;
            var dummy = new GameObject("ModelPreload");
            dummy.transform.position = new Vector3(0f, -50f, 0f);
            Attach(dummy.transform, file, YawDeg, ok => UnityEngine.Object.Destroy(dummy));
        }

        // 載入並掛到機器人底下,貼地置中。成功後隱藏內建方塊外觀。
        public static async void Attach(Transform robot, string file, float yawDeg, Action<bool> done)
        {
            bool ok = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string key = Key(file, yawDeg);
            // 預載還沒好:等它(最多 30 秒),再用樣板
            while (Loading && template == null && sw.ElapsedMilliseconds < 30000) await System.Threading.Tasks.Task.Yield();
            if (template != null && templateKey == key)
            {
                if (robot == null) { done?.Invoke(false); return; }
                var copy = UnityEngine.Object.Instantiate(template, robot, false);
                copy.name = "ModelPivot";
                copy.SetActive(true);
                HideBuiltin(robot);
                LastAttachMs = sw.ElapsedMilliseconds;
                Debug.Log($"[RobotModels] attached cached model in {LastAttachMs} ms");
                done?.Invoke(true);
                return;
            }
            Loading = true;
            try
            {
                var path = Path.Combine(Dir, file);
                Debug.Log($"[RobotModels] loading {path} exists={File.Exists(path)}");
                var imp = new GltfImport(null, null, null, new GLTFast.Logging.ConsoleLogger());
                bool loaded = await imp.Load(new Uri(path).AbsoluteUri);
                Debug.Log($"[RobotModels] Load returned {loaded}");
                if (loaded)
                {
                    // 外觀掛在「慣性傾斜」軸心下:加速時車頭微微上揚、煞車時點頭(以地面接觸點為軸),看得出底盤慣性
                    var pivot = new GameObject("ModelPivot");
                    pivot.transform.SetParent(robot, false);
                    pivot.transform.localPosition = new Vector3(0f, -(SimConstants.BumperHeight / 2f + 0.03f), 0f);
                    pivot.AddComponent<BodyLean>();
                    var root = new GameObject("ModelRoot");
                    root.transform.SetParent(pivot.transform, false);
                    bool inst = await imp.InstantiateMainSceneAsync(root.transform);
                    Debug.Log($"[RobotModels] Instantiate returned {inst}");
                    if (inst)
                    {
                        Fix(root, robot, yawDeg);
                        ok = true;
                        // 留樣板:合併後的網格在 ModelPivot 底下(機器人座標),和機器人位置/朝向無關
                        if (template != null) UnityEngine.Object.Destroy(template);
                        template = UnityEngine.Object.Instantiate(pivot);
                        template.name = "ModelTemplate";
                        template.transform.localPosition = pivot.transform.localPosition;   // 複製到機器人底下時用的是 local 值
                        template.transform.localRotation = Quaternion.identity;
                        template.transform.localScale = Vector3.one;
                        // 零件全部合併了:樣板裡不用留原本數百個(已停用的)零件,複製會快很多
                        var tr = template.transform.Find("ModelRoot");
                        if (tr != null && tr.GetComponentsInChildren<Renderer>(false).Length == 0) UnityEngine.Object.Destroy(tr.gameObject);
                        template.SetActive(false);
                        UnityEngine.Object.DontDestroyOnLoad(template);
                        templateKey = key;
                    }
                    else UnityEngine.Object.Destroy(root);
                }
                else LastError = "load failed";
            }
            catch (Exception e) { LastError = e.Message; Debug.LogError("RobotModels: " + e); }
            finally { Loading = false; }
            LastAttachMs = sw.ElapsedMilliseconds;
            Debug.Log($"[RobotModels] loaded model in {LastAttachMs} ms");
            done?.Invoke(ok);
        }

        // 同一個顏色共用一個材質(原本每個零件各建一個,573 個零件 = 573+ 個材質,完全無法合批,是卡頓主因之一)
        static readonly System.Collections.Generic.Dictionary<int, Material> matCache = new System.Collections.Generic.Dictionary<int, Material>();
        static Material SharedMat(Color c)
        {
            int key = (Mathf.RoundToInt(c.r * 63f) << 12) | (Mathf.RoundToInt(c.g * 63f) << 6) | Mathf.RoundToInt(c.b * 63f);
            if (!matCache.TryGetValue(key, out var m) || m == null) { m = FieldBuilder.MakeMat(c); matCache[key] = m; }
            return m;
        }

        public static int LastDrawParts;   // 合併後的網格數(測試 / 除錯用)

        // 把模型依材質合併成少數幾個大網格:數百個零件 → 每種顏色一個(超過 65k 頂點用 32 位元索引)。
        // 合併後掛在 parent(ModelPivot)底下,慣性傾斜照常;讀不到頂點資料的網格(非 readable)保留原樣。
        static void Merge(GameObject root, Transform parent)
        {
            var groups = new System.Collections.Generic.Dictionary<Material, System.Collections.Generic.List<CombineInstance>>();
            var merged = new System.Collections.Generic.List<GameObject>();
            Matrix4x4 toParent = parent.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                var mesh = mf.sharedMesh;
                if (mr == null || mesh == null || !mesh.isReadable || !mr.enabled || !mf.gameObject.activeInHierarchy) continue;
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (!groups.TryGetValue(mats[i], out var list)) { list = new System.Collections.Generic.List<CombineInstance>(); groups[mats[i]] = list; }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = toParent * mf.transform.localToWorldMatrix });
                }
                merged.Add(mf.gameObject);
            }
            if (merged.Count == 0) { LastDrawParts = root.GetComponentsInChildren<Renderer>(true).Length; return; }
            int parts = 0;
            foreach (var kv in groups)
            {
                var m = new Mesh { name = "Merged_" + kv.Key.color, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), true, true);
                m.RecalculateBounds();
                var go = new GameObject(m.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                parts++;
            }
            foreach (var g in merged) g.SetActive(false);   // 原零件關掉(不刪,讀不到的網格仍顯示)
            LastDrawParts = parts + root.GetComponentsInChildren<Renderer>(false).Length;
            Debug.Log($"[RobotModels] merged {merged.Count} parts into {parts} meshes (draw parts now {LastDrawParts})");
        }

        static void Fix(GameObject root, Transform robot, float yawDeg)
        {
            // 材質換成內建 Standard(保留 glTF 的基本色),避免 shader 被打包流程裁掉;同色共用一個材質
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    Color c = new Color(0.6f, 0.62f, 0.66f);
                    if (m != null)
                    {
                        if (m.HasProperty("baseColorFactor")) c = m.GetColor("baseColorFactor");
                        else if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                        else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
                    }
                    c.a = 1f;
                    mats[i] = SharedMat(c);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            Debug.Log($"[RobotModels] Fix: renderers={root.GetComponentsInChildren<Renderer>(true).Length} meshFilters={root.GetComponentsInChildren<MeshFilter>(true).Length} children={root.transform.childCount} active={root.activeInHierarchy}");
            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            // 貼地置中:底部對齊車底,XZ 置中於機器人中心
            Bounds b = default; bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            Debug.Log($"[RobotModels] bounds first={first} center={b.center} size={b.size}");
            if (first) return;
            {
                var rs = root.GetComponentsInChildren<Renderer>(true);
                var mins = new System.Collections.Generic.List<float>();
                foreach (var r in rs) mins.Add(r.bounds.min.y);
                mins.Sort();
                var sbd = new System.Text.StringBuilder("[RobotModels] lowest renderers minY: ");
                for (int i = 0; i < Mathf.Min(12, mins.Count); i++) sbd.Append(mins[i].ToString("0.000")).Append(' ');
                sbd.Append("| p10=" + mins[mins.Count / 10].ToString("0.000") + " median=" + mins[mins.Count / 2].ToString("0.000") + " robotY=" + robot.position.y.ToString("0.000"));
                foreach (var r in rs) if (r.bounds.min.y <= mins[Mathf.Min(2, mins.Count - 1)]) sbd.Append(" lowName=" + r.name + " size=" + r.bounds.size.ToString("0.00"));
                Debug.Log(sbd.ToString());
            }
            // 底部取「主體」最低點:少數零件(<8%)若單獨掉在下方一大段(間隔 >5cm),不算進底部,否則整台機器人會懸空
            float bottom = b.min.y;
            {
                var ys = new System.Collections.Generic.List<float>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) ys.Add(r.bounds.min.y);
                ys.Sort();
                int start = 0;
                while (true)
                {
                    int end = start;
                    while (end + 1 < ys.Count && ys[end + 1] - ys[end] <= 0.05f) end++;
                    int size = end - start + 1;
                    if (end + 1 < ys.Count && size < ys.Count * 0.08f) { start = end + 1; continue; }
                    break;
                }
                bottom = ys[start];
            }
            Vector3 target = new Vector3(robot.position.x, robot.position.y - SimConstants.BumperHeight / 2f, robot.position.z);
            root.transform.position += new Vector3(target.x - b.center.x, target.y - bottom, target.z - b.center.z);

            // 擺好位置後再合併(合併時把位置一起烤進網格)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-nomerge") >= 0) { LastDrawParts = root.GetComponentsInChildren<Renderer>(false).Length; Debug.Log("[RobotModels] -nomerge: keeping " + LastDrawParts + " parts"); }
            else try { Merge(root, root.transform.parent); } catch (Exception e) { Debug.LogWarning("[RobotModels] merge failed, keeping parts: " + e.Message); }

            // 隱藏內建方塊外觀
            HideBuiltin(robot);
        }
    }
}
