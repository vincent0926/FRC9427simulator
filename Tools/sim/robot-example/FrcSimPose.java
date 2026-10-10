package frc.robot.sim;

import edu.wpi.first.math.geometry.Pose2d;
import edu.wpi.first.math.geometry.Rotation2d;
import edu.wpi.first.networktables.DoubleArraySubscriber;
import edu.wpi.first.networktables.NetworkTableInstance;
import edu.wpi.first.networktables.StringSubscriber;
import edu.wpi.first.wpilibj.RobotBase;
import java.util.Optional;

/**
 * 讀 FRC 9427 模擬器(Unity)發布的位姿,只在模擬時有值。
 *
 * <p>模擬器把機器人擺在選好的起始站位(Blue 1~3 / Red 1~3),但機器人程式的里程計是從 (0, 0) 開始,
 * 沒有視覺定位的機器人(例如坦克 LEO)自動路線就會整個偏掉。把這個檔案放進專案(例如 src/main/java/frc/robot/sim/),
 * 在 autonomousInit 重置里程計即可:
 *
 * <pre>
 * public void autonomousInit() {
 *   FrcSimPose.startPose().ifPresent(drive::resetPose);   // 上機時沒有值,不會做任何事
 *   ...
 * }
 * </pre>
 *
 * <p>NetworkTables 主題:/FRCSim/StartPose、/FRCSim/RobotPose(double[] {x 公尺, y 公尺, 航向 度},WPILib 藍方原點座標)、
 * /FRCSim/Station(字串 "blue1".."red3")。
 */
public final class FrcSimPose {
  private static final NetworkTableInstance NT = NetworkTableInstance.getDefault();
  private static final DoubleArraySubscriber START =
      NT.getDoubleArrayTopic("/FRCSim/StartPose").subscribe(new double[0]);
  private static final DoubleArraySubscriber ROBOT =
      NT.getDoubleArrayTopic("/FRCSim/RobotPose").subscribe(new double[0]);
  private static final StringSubscriber STATION =
      NT.getStringTopic("/FRCSim/Station").subscribe("");

  private FrcSimPose() {}

  /** 模擬器的開賽起點;上機或模擬器沒連上時是 empty。 */
  public static Optional<Pose2d> startPose() {
    return toPose(START.get());
  }

  /** 模擬器裡機器人目前的真實位置(除錯、比對里程計用)。 */
  public static Optional<Pose2d> truePose() {
    return toPose(ROBOT.get());
  }

  /** 模擬器選的站位,例如 "red2";沒有時是空字串。DriverStation.getAlliance() / getLocation() 也讀得到同樣的資訊。 */
  public static String station() {
    return RobotBase.isSimulation() ? STATION.get() : "";
  }

  private static Optional<Pose2d> toPose(double[] v) {
    if (!RobotBase.isSimulation() || v == null || v.length < 3) return Optional.empty();
    return Optional.of(new Pose2d(v[0], v[1], Rotation2d.fromDegrees(v[2])));
  }
}
