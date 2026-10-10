using UnityEngine;

namespace FrcSim
{
    // 一場遊戲的生命週期:Begin() 建立球/機器人/計分/HUD,End() 全部清掉回到主畫面。
    public static class GameSession
    {
        public static bool Active;
        public static float StartTime;
        public static SwerveDrive Drive;
        public static RobotMechanisms Mech;
        public static HalSim Hal;
        public static int Station = 1;          // 玩家的起始站位(AutoPlan.Names:0..2 藍 1~3、3..5 紅 1~3)
        public static AutoDrive Auto, Auto2;
        public static NtChooser Chooser;
        public static bool AutoOnly;            // 自動模式:只跑 AUTO(20 秒 + 3 秒計分寬限)就停,顯示 AUTO 成績
        static string lastProjectOverride;    // 內建自動路線(真實程式模式的玩家機器人沒有,自動程式由 Java 程式跑)

        static readonly Color BlueBody = new Color(0.15f, 0.35f, 0.9f), RedBody = new Color(0.9f, 0.2f, 0.2f);

        // 站位:命令列 -station blue1 優先,否則用設定畫面選的(PlayerPrefs "station",預設 Blue 2)
        public static int ChosenStation()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-station");
            if (i >= 0 && i + 1 < a.Length && AutoPlan.Parse(a[i + 1]) >= 0) return AutoPlan.Parse(a[i + 1]);
            return AutoPlan.Clamp(PlayerPrefs.GetInt("station", 1));
        }

        // 舊的自動測試會自己擺車、開車:不能讓 AUTO 階段的內建自動路線搶走控制
        static bool LegacyTestRun()
        {
            var a = System.Environment.GetCommandLineArgs();
            foreach (var f in new[] { "-padtest", "-fieldtest", "-walltest", "-balltest", "-soaktest", "-settletest", "-selftest", "-realtest", "-leotest", "-shottest" })
                if (System.Array.IndexOf(a, f) >= 0) return true;
            return false;
        }

        // 內建自動路線:命令列 -routine neutral 優先,否則用設定畫面選的(PlayerPrefs "autoRoutine",預設依站位)
        public static AutoRoutine ChosenRoutine()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-routine");
            if (i >= 0 && i + 1 < a.Length && AutoPlan.ParseRoutine(a[i + 1]) >= 0) return (AutoRoutine)AutoPlan.ParseRoutine(a[i + 1]);
            return AutoPlan.ClampRoutine(PlayerPrefs.GetInt("autoRoutine", 0));
        }

        static Vector2 Hub(bool red) => red ? new Vector2(SimConstants.FieldLength - 4.42586f, SimConstants.FieldWidth / 2f) : RobotMechanisms.BlueHub;

        static void PlaceAtStation(SwerveDrive d, int station)
        {
            AutoPlan.StartPose(station, out float x, out float y, out float h);
            d.SetPose(new Vector2(x, y), h);
        }

        static GameObject robot, robot2, score, hud;

        public static void Begin(CameraRig rig, bool selfTest = false, string projectOverride = null)
        {
            if (Active) return;
            Active = true;
            lastProjectOverride = projectOverride;
            StartTime = Time.time;
            SimConstants.MaxSpeed = SettingsStore.MaxSpeedChoice;   // 開始前選的最高車速
            SimConstants.MaxAccel = SettingsStore.AccelChoice;      // 開始前選的加速度(慣性)
            SimConstants.MaxAngularAccel = 60f * SettingsStore.AccelChoice / 22f * 1.5f;
            Time.timeScale = 1f;

            FuelManager.Init();
            FuelManager.SpawnStart();
            HumanPlayer.ResetMatch();
            score = new GameObject("Score");
            score.AddComponent<HumanPlayer>();
            score.AddComponent<ScoreManager>();
            score.AddComponent<MatchAudio>();   // 開賽 / TELEOP / 終局 / 結束提示音(M 靜音)

            Station = LegacyTestRun() ? 1 : ChosenStation();
            bool red = AutoPlan.IsRed(Station);
            robot = SimBootstrap.BuildRobot(out var turretVis, out var armVis, "Robot", red ? RedBody : BlueBody);
            Drive = robot.GetComponent<SwerveDrive>();
            if (!LegacyTestRun()) PlaceAtStation(Drive, Station);   // 舊測試沿用原本的起點 (2.0, 場寬中央)
            Mech = robot.AddComponent<RobotMechanisms>();
            Mech.Drive = Drive;
            Mech.RobotCollider = robot.GetComponent<Collider>();
            Mech.TurretVisual = turretVis;
            Mech.ArmVisual = armVis;
            Mech.TargetHub = Hub(red);

            // 第二台機器人(對面聯盟的同號站位,內建行為;AUTO 跑鏡像的內建自動路線,之後方向鍵操控)
            Auto = Auto2 = null; Chooser = null;
            if (PlayerPrefs.GetInt("secondRobot", 1) == 1)
            {
                int st2 = AutoPlan.Opposite(Station);
                bool red2 = AutoPlan.IsRed(st2);
                robot2 = SimBootstrap.BuildRobot(out var t2, out var a2, "Robot2", red2 ? RedBody : BlueBody);
                var d2 = robot2.GetComponent<SwerveDrive>();
                d2.ResetPose();
                if (LegacyTestRun()) d2.SetPose(new Vector2(SimConstants.FieldLength - 2.0f, SimConstants.FieldWidth / 2f), 180f);
                else PlaceAtStation(d2, st2);
                var m2 = robot2.AddComponent<RobotMechanisms>();
                m2.Drive = d2; m2.RobotCollider = robot2.GetComponent<Collider>(); m2.TurretVisual = t2; m2.ArmVisual = a2;
                m2.TargetHub = Hub(red2);
                var in2 = robot2.AddComponent<Robot2Input>(); in2.Drive = d2; in2.Mech = m2;
                if (!LegacyTestRun()) Auto2 = robot2.AddComponent<AutoDrive>();
                if (Auto2 != null) { Auto2.Drive = d2; Auto2.Mech = m2; Auto2.Station = st2; Auto2.Routine = ChosenRoutine(); }
            }

            // 真實機器人程式(設定畫面選的專案;沒選就用內建行為)
            // 預設關閉:真實程式模式還在實驗(閉環時序不穩),內建行為才是穩定版
            string proj = projectOverride ?? (PlayerPrefs.GetInt("useRealCode", 0) == 1 ? PlayerPrefs.GetString("robotProject", "") : "");
            bool tankMode = PlayerPrefs.GetInt("tankMode", 1) == 1;
            if (projectOverride == null && tankMode && !string.IsNullOrEmpty(proj) && !IsTankProfile(proj)) proj = "";   // 坦克模式只跑有坦克設定檔的專案(如 LEO)
            if (string.IsNullOrEmpty(proj) && PlayerPrefs.GetInt("useRealCode", 0) == 1 && !tankMode)
            {
                foreach (var c in new[] { @"C:\Users\frc94\2026_FRC9427_offseasonBot\FRC9427_offseasonBot" })
                    if (System.IO.File.Exists(System.IO.Path.Combine(c, "gradlew.bat"))) { proj = c; break; }
            }
            if (string.IsNullOrEmpty(proj) && PlayerPrefs.GetInt("useRealCode", 0) == 1 && tankMode)
            {
                // 沒選專案時自動找桌面上的 LEO(資料夾名開頭 LEO、內有 gradlew.bat)
                try
                {
                    string desk = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                    foreach (var dd in System.IO.Directory.GetDirectories(desk, "LEO*"))
                        if (System.IO.File.Exists(System.IO.Path.Combine(dd, "gradlew.bat")) && IsTankProfile(dd)) { proj = dd; break; }
                }
                catch { }
            }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-norealcode") >= 0) proj = "";
            if (!string.IsNullOrEmpty(proj) && System.IO.File.Exists(System.IO.Path.Combine(proj, "gradlew.bat")))
            {
                Hal = robot.AddComponent<HalSim>();
                Hal.Station = AutoPlan.Names[Station];   // DriverStation 的聯盟/站位:機器人程式的自動程式依此選路線、決定鏡像
                // 自動程式選單 + 起點位姿(NetworkTables);有比賽計時就先進「開賽前」(Disabled、選自動程式)
                Chooser = robot.AddComponent<NtChooser>();
                Chooser.Drive = Drive; Chooser.Station = AutoPlan.Names[Station];
                AutoPlan.StartPose(Station, out Chooser.StartX, out Chooser.StartY, out Chooser.StartDeg);
                Chooser.Begin();
                if (ScoreManager.ClockOn && !LegacyTestRun())
                {
                    Hal.Enabled = false;
                    robot.AddComponent<PreMatch>().Nt = Chooser;
                }
                Hal.StartRobot(proj);
                var ms = robot.AddComponent<MechSim>();
                ms.Hal = Hal; ms.Drive = Drive; Mech.Sim = ms;
                if (!IsTankProfile(proj))   // 坦克(LEO)沒有視覺,不需要模擬 Limelight,省下 NetworkTables 的 CPU
                {
                    var nt = robot.AddComponent<NtSim>();     // 模擬 Limelight(NetworkTables)讓程式能定位
                    nt.Drive = Drive; nt.Begin();
                }
                ms.MechPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Sim", SimProject.Profile(proj) + ".mech.json");
            }

            // 匯入的機器人模型(沒有就用內建方塊)
            string model = RobotModels.Selected;
            Debug.Log($"[RobotModels] selected='{model}' dir={RobotModels.Dir} count={RobotModels.List().Length}");
            if (!string.IsNullOrEmpty(model))
                RobotModels.Attach(robot.transform, model, RobotModels.YawDeg, ok =>
                {
                    if (!ok) Debug.LogWarning("robot model load failed: " + RobotModels.LastError);
                });

            rig.Orbit = false;
            rig.Target = robot.transform;
            rig.Mode = 1;

            hud = new GameObject("SimUI");
            var ui = hud.AddComponent<SimHud>();
            ui.Drive = Drive; ui.Rig = rig; ui.Mech = Mech;

            var input = robot.AddComponent<RobotInput>();
            input.Drive = Drive;
            input.Mech = Mech;

            // 內建行為模式:AUTO 階段跑內建自動路線(真實程式模式由機器人程式自己跑自動)
            if (Hal == null && !selfTest && !LegacyTestRun())
            {
                Auto = robot.AddComponent<AutoDrive>();
                Auto.Drive = Drive; Auto.Mech = Mech; Auto.Station = Station; Auto.Routine = ChosenRoutine();
            }

            if (selfTest)
            {
                var st = new GameObject("SelfTest").AddComponent<SelfTest>();
                st.Drive = Drive; st.Mech = Mech;
            }
        }

        // 自動模式結束後按 Enter:同樣設定再跑一次
        public static void Restart(CameraRig rig)
        {
            string p = lastProjectOverride;
            End(rig);
            Begin(rig, false, p);
        }

        static bool IsTankProfile(string dir)
        {
            try
            {
                string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Sim", SimProject.Profile(dir) + ".mech.json");
                return System.IO.File.Exists(f) && System.IO.File.ReadAllText(f).Contains("\"tank\"");
            }
            catch { return false; }
        }

        public static void End(CameraRig rig)
        {
            if (!Active) return;
            Active = false;
            Time.timeScale = 1f;
            if (robot != null) Object.Destroy(robot);
            if (robot2 != null) Object.Destroy(robot2);
            if (score != null) Object.Destroy(score);
            if (hud != null) Object.Destroy(hud);
            FuelManager.Clear();
            Hal?.Stop(); Hal = null;
            Auto = Auto2 = null; Chooser = null;
            Drive = null; Mech = null;
            rig.Target = null;
            rig.Orbit = true;
        }
    }
}
