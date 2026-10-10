using UnityEngine;

namespace FrcSim
{
    // 內建行為的自動階段:比賽時鐘在 AUTO(前 20 秒)時,照 AutoPlan 的路線自己開、自己射;手把/鍵盤暫時無效(按 N 跳過 AUTO)。
    // 真實程式模式的玩家機器人不掛這個(自動程式由機器人 Java 程式自己跑)。
    public class AutoDrive : MonoBehaviour
    {
        public SwerveDrive Drive;
        public RobotMechanisms Mech;
        public int Station = 1;
        public AutoRoutine Routine = AutoRoutine.ByStation;
        public AutoRunner Runner;
        public bool Running { get; private set; }
        public string StepName => Runner == null ? "" : (Runner.Done ? "done" : Runner.Current.Name);
        bool finished, savedFieldCentric;

        public static bool AutoPhase => ScoreManager.ClockOn && ScoreManager.Phase == "AUTO" && !ScoreManager.Ended && !ScoreManager.WaitingForCode;

        void Start() { Runner = new AutoRunner(Station, Routine); }

        void Update()
        {
            if (Drive == null || Runner == null) return;
            bool want = AutoPhase && !finished && !MenuSystem.Blocking;
            if (want && !Running) { Running = true; savedFieldCentric = Drive.FieldCentric; }
            if (!want && Running) Stop();
            if (!Running) return;

            Drive.FieldCentric = true;
            var c = Runner.Update(Drive.Pose2d.x, Drive.Pose2d.y, Drive.HeadingRad, Mech != null ? Mech.Held : 0, Time.deltaTime);
            Drive.Drive(c.Vx / SimConstants.MaxSpeed, c.Vy / SimConstants.MaxSpeed, c.Omega / SimConstants.MaxAngularSpeed);
            if (Mech != null) { Mech.IntakeDown = c.Intake; Mech.Shooting = c.Shoot; }
            if (c.Done) { finished = true; Stop(); }
        }

        void Stop()
        {
            Running = false;
            Drive.Drive(0f, 0f, 0f);
            Drive.FieldCentric = savedFieldCentric;
            if (Mech != null) { Mech.Shooting = false; Mech.IntakeDown = false; }
        }
    }
}
