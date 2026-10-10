using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FrcSim
{
    // 真實程式模式的 NetworkTables 4 小客戶端(坦克 / swerve 都會掛):
    //  1. 找機器人程式放在 SmartDashboard 的自動程式選單(SendableChooser / PathPlanner AutoBuilder.buildAutoChooser /
    //     AdvantageKit LoggedDashboardChooser 都是 "/SmartDashboard/<名稱>/.type" = "String Chooser"),
    //     讀 options / default / active,開賽前讓使用者選,寫回 "<名稱>/selected"(和 Shuffleboard / Elastic 做的事一樣)。
    //  2. 發布模擬器的真實位姿,機器人程式可用 Tools/sim/robot-example/FrcSimPose.java 讀來重置里程計:
    //     /FRCSim/StartPose  double[] {x, y, 航向度}(開賽起點)
    //     /FRCSim/RobotPose  double[] {x, y, 航向度}(目前位置,20 Hz)
    //     /FRCSim/Station    string  "blue1".."red3"
    //  也把 SmartDashboard 的字串值記下來(Strings),測試用來讀機器人程式回報的內容。
    public class NtChooser : MonoBehaviour
    {
        public class Chooser { public string Name; public string[] Options = new string[0]; public string Default = "", Active = "", Selected = ""; }

        public SwerveDrive Drive;
        public string Station = "blue2";
        public float StartX, StartY, StartDeg;
        public string Status = "idle";
        public volatile bool Connected;
        public readonly ConcurrentDictionary<string, string> Strings = new ConcurrentDictionary<string, string>();
        public readonly ConcurrentDictionary<string, double[]> Arrays = new ConcurrentDictionary<string, double[]>();   // 例如 Field/Robot = 程式自己估的位姿 {x, y, 度}

        readonly ConcurrentDictionary<string, Chooser> choosers = new ConcurrentDictionary<string, Chooser>();
        readonly ConcurrentDictionary<long, KeyValuePair<string, string>> topicById = new ConcurrentDictionary<long, KeyValuePair<string, string>>();
        readonly ConcurrentQueue<KeyValuePair<string, string>> pendingSelect = new ConcurrentQueue<KeyValuePair<string, string>>();
        readonly Dictionary<string, int> pubs = new Dictionary<string, int>();
        ClientWebSocket ws;
        CancellationTokenSource cts;
        volatile float px, py, pdeg;
        double clockOffsetUs;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long NowUs() => clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;

        // 主執行緒用:目前找到的選單(依名稱排序)
        public List<Chooser> Choosers
        {
            get
            {
                var l = new List<Chooser>();
                foreach (var c in choosers.Values) if (c.Options.Length > 0) l.Add(c);
                l.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return l;
            }
        }

        public static string PrefKey(string chooser) => "autoChoice." + chooser;

        // 選一個選項(主執行緒呼叫):記住、並送給機器人程式
        public void Select(Chooser c, string option)
        {
            // 只送清單裡有的選項:送了不存在的名稱,SendableChooser.getSelected() 會回傳 null,機器人程式可能直接當掉
            if (c == null || string.IsNullOrEmpty(option) || Array.IndexOf(c.Options, option) < 0) return;
            c.Selected = option;
            PlayerPrefs.SetString(PrefKey(c.Name), option); PlayerPrefs.Save();
            pendingSelect.Enqueue(new KeyValuePair<string, string>(c.Name, option));
        }

        public void Begin() { cts = new CancellationTokenSource(); Task.Run(() => Run(cts.Token)); }
        void OnDestroy() { try { cts?.Cancel(); ws?.Abort(); } catch { } }

        void Update()
        {
            if (Drive == null) return;
            var p = Drive.Pose2d; px = p.x; py = p.y; pdeg = Drive.HeadingRad * Mathf.Rad2Deg;
        }

        async Task Run(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    ws?.Dispose();
                    ws = new ClientWebSocket();
                    ws.Options.AddSubProtocol("v4.1.networktables.first.wpi.edu");
                    Status = "connecting";
                    try { await ws.ConnectAsync(new Uri("ws://127.0.0.1:5810/nt/frcsim-chooser"), ct); }
                    catch { await Task.Delay(1500, ct); continue; }
                    pubs.Clear(); topicById.Clear(); Connected = true; Status = "connected";
                    var subs = new JArray(new JObject { ["method"] = "subscribe", ["params"] = new JObject { ["topics"] = new JArray("/SmartDashboard/"), ["subuid"] = 11, ["options"] = new JObject { ["prefix"] = true, ["periodic"] = 0.1 } } });
                    await SendText(subs.ToString(Newtonsoft.Json.Formatting.None), ct);
                    await SendBinary(TimeSync(), ct);
                    var recv = Task.Run(() => ReceiveLoop(ct));
                    int tick = 0;
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        if (tick++ % 100 == 0) await SendBinary(TimeSync(), ct);
                        while (pendingSelect.TryDequeue(out var sel)) await PubString("/SmartDashboard/" + sel.Key + "/selected", sel.Value, ct);
                        await PubDoubles("/FRCSim/RobotPose", new double[] { px, py, pdeg }, ct);
                        if (tick % 10 == 1)
                        {
                            await PubDoubles("/FRCSim/StartPose", new double[] { StartX, StartY, StartDeg }, ct);
                            await PubString("/FRCSim/Station", Station, ct);
                        }
                        await Task.Delay(50, ct);
                    }
                    Connected = false; Status = "disconnected";
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Status = "error: " + e.Message; }
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[1 << 16];
            var ms = new MemoryStream();
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    if (r.MessageType == WebSocketMessageType.Binary) HandleBinary(ms.ToArray());
                    else HandleText(Encoding.UTF8.GetString(ms.ToArray()));
                    ms.SetLength(0);
                }
            }
            catch { }
        }

        void HandleText(string s)
        {
            try
            {
                foreach (var m in JArray.Parse(s))
                    if ((string)m["method"] == "announce")
                        topicById[(long)m["params"]["id"]] = new KeyValuePair<string, string>((string)m["params"]["name"], (string)m["params"]["type"]);
            }
            catch { }
        }

        void HandleBinary(byte[] data)
        {
            int pos = 0;
            try
            {
                while (pos < data.Length)
                {
                    var arr = MsgPack.Read(data, ref pos) as object[];
                    if (arr == null || arr.Length < 4) continue;
                    long id = Convert.ToInt64(arr[0]);
                    if (id == -1)
                    {
                        long rtt = NowUs() - Convert.ToInt64(arr[3]);
                        clockOffsetUs = Convert.ToInt64(arr[1]) + rtt / 2.0 - NowUs();
                        continue;
                    }
                    if (!topicById.TryGetValue(id, out var tp)) continue;
                    OnValue(tp.Key, arr[3]);
                }
            }
            catch { }
        }

        void OnValue(string topic, object v)
        {
            const string Root = "/SmartDashboard/";
            if (!topic.StartsWith(Root)) return;
            if (v is string sv) Strings[topic.Substring(Root.Length)] = sv;
            else if (v is object[] arr && arr.Length > 0 && (arr[0] is double || arr[0] is long))
            {
                var dv = new double[arr.Length];
                for (int i = 0; i < arr.Length; i++) dv[i] = Convert.ToDouble(arr[i]);
                Arrays[topic.Substring(Root.Length)] = dv;
            }
            int slash = topic.LastIndexOf('/');
            if (slash <= Root.Length) return;
            string name = topic.Substring(Root.Length, slash - Root.Length), key = topic.Substring(slash + 1);
            if (key == ".type") { if (v as string == "String Chooser") choosers.GetOrAdd(name, n => new Chooser { Name = n }); return; }
            if (!choosers.TryGetValue(name, out var c))
            {
                // .type 可能比 options 晚到:先建起來,options 有值才會出現在清單
                if (key != "options" && key != "default" && key != "active") return;
                c = choosers.GetOrAdd(name, n => new Chooser { Name = n });
            }
            switch (key)
            {
                case "options":
                    if (v is object[] a) { var o = new string[a.Length]; for (int i = 0; i < a.Length; i++) o[i] = a[i] as string ?? ""; c.Options = o; }
                    break;
                case "default": c.Default = v as string ?? ""; break;
                case "active": c.Active = v as string ?? ""; break;
                case "selected": c.Selected = v as string ?? ""; break;
            }
        }

        async Task PubString(string topic, string value, CancellationToken ct) => await Pub(topic, "string", 4, value, ct);
        async Task PubDoubles(string topic, double[] value, CancellationToken ct) => await Pub(topic, "double[]", 17, value, ct);

        async Task Pub(string topic, string typeName, int type, object value, CancellationToken ct)
        {
            if (!pubs.TryGetValue(topic, out int id))
            {
                id = 100 + pubs.Count;
                pubs[topic] = id;
                await SendText(new JArray(new JObject { ["method"] = "publish", ["params"] = new JObject { ["name"] = topic, ["pubuid"] = id, ["type"] = typeName, ["properties"] = new JObject() } }).ToString(Newtonsoft.Json.Formatting.None), ct);
            }
            var b = new List<byte> { 0x94 };
            Int(b, id); Int(b, (long)(NowUs() + clockOffsetUs)); Int(b, type);
            if (value is string s) Str(b, s);
            else if (value is double[] d) { b.Add(0xdc); b.Add((byte)(d.Length >> 8)); b.Add((byte)d.Length); foreach (var x in d) Dbl(b, x); }
            await SendBinary(b.ToArray(), ct);
        }

        byte[] TimeSync() { var b = new List<byte> { 0x94 }; Int(b, -1); Int(b, 0); Int(b, 2); Int(b, NowUs()); return b.ToArray(); }

        async Task SendText(string s, CancellationToken ct) => await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)), WebSocketMessageType.Text, true, ct);
        async Task SendBinary(byte[] b, CancellationToken ct) => await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Binary, true, ct);

        static void Int(List<byte> b, long v)
        {
            if (v >= 0 && v < 128) { b.Add((byte)v); return; }
            if (v < 0 && v >= -32) { b.Add((byte)(0xe0 | (v + 32))); return; }
            b.Add(0xd3);
            for (int i = 7; i >= 0; i--) b.Add((byte)(v >> (8 * i)));
        }
        static void Dbl(List<byte> b, double v) { b.Add(0xcb); var x = BitConverter.GetBytes(v); Array.Reverse(x); b.AddRange(x); }
        static void Str(List<byte> b, string s)
        {
            var u = Encoding.UTF8.GetBytes(s);
            if (u.Length < 32) b.Add((byte)(0xa0 | u.Length));
            else if (u.Length < 256) { b.Add(0xd9); b.Add((byte)u.Length); }
            else { b.Add(0xda); b.Add((byte)(u.Length >> 8)); b.Add((byte)u.Length); }
            b.AddRange(u);
        }
    }
}
