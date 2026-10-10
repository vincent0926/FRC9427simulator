using System;
using System.Collections.Generic;

namespace FrcSim
{
    // 自動階段(AUTO 20 秒)的起始位置與內建自動路線。純 C#、不依賴 UnityEngine,可以在 Tools/tests 用 dotnet 單元測試。
    //  · 起始位置:Blue1/2/3、Red1/2/3(駕駛站位置)。藍方 1 號在場地 y 大的一側(藍方駕駛看出去的左手邊),
    //    紅方是藍方繞場地中心旋轉 180°(場地旋轉對稱),所以 Red n 與 Blue n 互為鏡像。
    //  · 真實程式模式:站位送進 DriverStation(機器人程式用 DriverStation.getAlliance()/getLocation() 讀到),自動程式由機器人程式自己跑。
    //  · 內建行為模式 / 第二台機器人:跑這裡的內建路線,路線可以和站位分開選(AutoRoutine):
    //      ByStation 依站位(1 號 = DEPOT,其他 = 只射預載)、PreloadOnly、Depot、Neutral(穿過 TRENCH 去中立區吃球,在中立區這側射)、None(不動)。
    public enum AutoStepKind { Drive, Shoot, Collect }
    public enum AutoRoutine { ByStation, PreloadOnly, Depot, Neutral, None }

    public class AutoStep
    {
        public AutoStepKind Kind;
        public float X, Y, HeadingDeg;   // 場地座標(WPILib:藍方原點,逆時針為正)
        public float Timeout;            // 秒
        public string Name;
        public AutoStep(AutoStepKind k, float x, float y, float h, float timeout, string name) { Kind = k; X = x; Y = y; HeadingDeg = h; Timeout = timeout; Name = name; }
    }

    public struct AutoCmd
    {
        public float Vx, Vy, Omega;      // 場地座標期望速度(m/s、rad/s)
        public bool Intake, Shoot, Done;
        public string Step;
    }

    public static class AutoPlan
    {
        public const float FieldLength = 16.54f, FieldWidth = 8.07f;
        public const float AllianceZoneDepth = 4.0284f, HubSize = 1.194f, RobotHalf = 0.40f;
        public const float StartX = AllianceZoneDepth - RobotHalf - 0.05f;   // 保險桿在起始線(聯盟區邊界)後 5 cm
        public const float ShootX = 2.10f;                                    // 射擊點:離 HUB 約 2.2~2.7 m(查表 2~4 m 有效)
        public const float DepotY = 5.965f;
        public static readonly string[] Names = { "blue1", "blue2", "blue3", "red1", "red2", "red3" };
        public static readonly string[] Labels = { "Blue 1", "Blue 2", "Blue 3", "Red 1", "Red 2", "Red 3" };
        public const int RoutineCount = 5;
        public static readonly string[] RoutineNames = { "bystation", "preload", "depot", "neutral", "none" };
        public static readonly string[] RoutineLabelsZh = { "依站位", "只射預載", "預載 + DEPOT", "預載 + 中立區(TRENCH)", "不動" };
        public static readonly string[] RoutineLabelsEn = { "By station", "Preload only", "Preload + DEPOT", "Preload + neutral (TRENCH)", "Do nothing" };
        public static int ParseRoutine(string name) => Array.IndexOf(RoutineNames, (name ?? "").Trim().ToLowerInvariant());
        public static AutoRoutine ClampRoutine(int r) => r < 0 || r >= RoutineCount ? AutoRoutine.ByStation : (AutoRoutine)r;
        // 中立區路線:TRENCH 開口在場邊 0~1.279 m(車寬 0.8 → 車心 y 0.64),HUB 中心 ±0.6 m 的 x 範圍內;中立區的球約 x 7.4~9.1、y 3.1~5.0
        public const float TrenchY = 0.64f, NeutralShootX = 6.60f, NeutralShootY = 2.40f;
        static readonly float[] BlueStartY = { 6.60f, FieldWidth / 2f, FieldWidth - 6.60f };
        static readonly float[] BlueShootY = { 5.60f, FieldWidth / 2f, FieldWidth - 5.60f };

        public const int Count = 6;
        public static bool IsRed(int station) => Clamp(station) >= 3;
        public static int Number(int station) => Clamp(station) % 3 + 1;
        public static int Opposite(int station) => (Clamp(station) + 3) % 6;   // 對面聯盟的同號站位(鏡像)
        public static int Clamp(int station) => station < 0 || station >= Count ? 1 : station;
        public static int Parse(string name)
        {
            int i = Array.IndexOf(Names, (name ?? "").Trim().ToLowerInvariant().Replace(" ", ""));
            return i;   // 找不到 = -1
        }

        public static float WrapDeg(float d) { d %= 360f; if (d > 180f) d -= 360f; if (d <= -180f) d += 360f; return d; }
        public static float WrapRad(float a) { while (a > Math.PI) a -= 2f * (float)Math.PI; while (a < -Math.PI) a += 2f * (float)Math.PI; return a; }

        // 場地中心點對稱(紅 ↔ 藍)
        public static void Mirror(ref float x, ref float y, ref float headingDeg)
        {
            x = FieldLength - x; y = FieldWidth - y; headingDeg = WrapDeg(headingDeg + 180f);
        }

        public static void HubCenter(bool red, out float x, out float y)
        {
            x = AllianceZoneDepth + HubSize / 2f; y = FieldWidth / 2f;
            if (red) x = FieldLength - x;
        }

        public static void StartPose(int station, out float x, out float y, out float headingDeg)
        {
            station = Clamp(station);
            x = StartX; y = BlueStartY[station % 3]; headingDeg = 0f;   // 車頭(intake)朝場內
            if (IsRed(station)) Mirror(ref x, ref y, ref headingDeg);
        }

        public static AutoRoutine Resolve(int station, AutoRoutine routine)
        {
            if (routine != AutoRoutine.ByStation) return routine;
            return Clamp(station) % 3 == 0 ? AutoRoutine.Depot : AutoRoutine.PreloadOnly;
        }

        public static List<AutoStep> Routine(int station) => Routine(station, AutoRoutine.ByStation);

        public static List<AutoStep> Routine(int station, AutoRoutine routine)
        {
            station = Clamp(station);
            int n = station % 3;
            float sy = BlueShootY[n];
            routine = Resolve(station, routine);
            var s = new List<AutoStep>();
            if (routine == AutoRoutine.None) return s;
            s.Add(new AutoStep(AutoStepKind.Drive, ShootX, sy, 0f, 4f, "to-shoot"));
            s.Add(new AutoStep(AutoStepKind.Shoot, ShootX, sy, 0f, 4f, "shoot-preload"));
            if (routine == AutoRoutine.Neutral)
            {
                // 走離站位最近的 TRENCH(1 號 = y 大那側,2、3 號 = y 小那側);藍方 1 號的路線是 3 號對 y 鏡像
                bool high = n == 0;
                float Y(float y) => high ? FieldWidth - y : y;
                s.Add(new AutoStep(AutoStepKind.Drive, 3.20f, Y(TrenchY), 0f, 4f, "to-trench"));
                s.Add(new AutoStep(AutoStepKind.Drive, 6.00f, Y(TrenchY), 0f, 4f, "through-trench"));
                s.Add(new AutoStep(AutoStepKind.Drive, NeutralShootX, Y(NeutralShootY), 0f, 3f, "to-neutral"));
                s.Add(new AutoStep(AutoStepKind.Collect, 7.70f, Y(3.50f), 0f, 3f, "collect-neutral"));
                s.Add(new AutoStep(AutoStepKind.Drive, NeutralShootX, Y(NeutralShootY), 0f, 3f, "back-to-shoot"));
                s.Add(new AutoStep(AutoStepKind.Shoot, NeutralShootX, Y(NeutralShootY), 0f, 4f, "shoot-neutral"));
            }
            if (routine == AutoRoutine.Depot)
            {
                // 1 號靠 DEPOT:車頭朝聯盟牆,放下 intake 慢慢開進 DEPOT 吃球,再回射擊點
                if (n != 0) s.Add(new AutoStep(AutoStepKind.Drive, ShootX, DepotY, 180f, 4f, "to-depot-lane"));   // 2、3 號先沿 x=2.1 走到 DEPOT 那排,避開 TOWER 立柱(x≈1.1)
                s.Add(new AutoStep(AutoStepKind.Drive, 1.40f, DepotY, 180f, 4f, "to-depot"));
                s.Add(new AutoStep(AutoStepKind.Collect, 0.62f, DepotY, 180f, 3f, "collect-depot"));
                if (n != 0) s.Add(new AutoStep(AutoStepKind.Drive, ShootX, DepotY, 0f, 4f, "return-lane"));
                s.Add(new AutoStep(AutoStepKind.Drive, ShootX, sy, 0f, 4f, "return"));
                s.Add(new AutoStep(AutoStepKind.Shoot, ShootX, sy, 0f, 4f, "shoot-depot"));
            }
            if (IsRed(station))
                foreach (var st in s) Mirror(ref st.X, ref st.Y, ref st.HeadingDeg);
            return s;
        }
    }

    // 依序執行路線:P 控制開到每個路徑點(速度、角速度限幅),射擊步驟射到沒球或逾時,Collect 步驟放下 intake 開進去
    public class AutoRunner
    {
        public readonly List<AutoStep> Steps;
        public int Index { get; private set; }
        public float StepTime { get; private set; }
        public bool Done => Index >= Steps.Count;
        public float MaxSpeed = 2.5f, MaxOmega = 4f;
        const float Kp = 2.5f, KpTheta = 4f, PosTol = 0.10f, AngTol = 0.08f;

        public AutoRunner(int station) { Steps = AutoPlan.Routine(station); }
        public AutoRunner(int station, AutoRoutine routine) { Steps = AutoPlan.Routine(station, routine); }
        public AutoStep Current => Done ? null : Steps[Index];

        public AutoCmd Update(float x, float y, float headingRad, int held, float dt)
        {
            var cmd = new AutoCmd();
            if (Done) { cmd.Done = true; cmd.Step = "done"; return cmd; }
            var st = Steps[Index];
            StepTime += dt;
            cmd.Step = st.Name;

            float ex = st.X - x, ey = st.Y - y;
            float dist = (float)Math.Sqrt(ex * ex + ey * ey);
            float speed = Math.Min(MaxSpeed, Kp * dist);
            if (st.Kind == AutoStepKind.Collect) speed = Math.Min(speed, 1.0f);   // 吃球時放慢
            if (dist > 1e-4f) { cmd.Vx = ex / dist * speed; cmd.Vy = ey / dist * speed; }
            float eh = AutoPlan.WrapRad(st.HeadingDeg * (float)Math.PI / 180f - headingRad);
            cmd.Omega = Math.Max(-MaxOmega, Math.Min(MaxOmega, KpTheta * eh));
            bool arrived = dist < PosTol && Math.Abs(eh) < AngTol;

            switch (st.Kind)
            {
                case AutoStepKind.Drive:
                    if (arrived || StepTime > st.Timeout) Next();
                    break;
                case AutoStepKind.Shoot:
                    cmd.Shoot = true;
                    if ((held <= 0 && StepTime > 0.3f) || StepTime > st.Timeout) Next();
                    break;
                case AutoStepKind.Collect:
                    cmd.Intake = true;
                    if ((arrived && StepTime > 1.0f) || StepTime > st.Timeout) Next();
                    break;
            }
            cmd.Done = Done;
            return cmd;
        }

        void Next() { Index++; StepTime = 0f; }
    }
}
