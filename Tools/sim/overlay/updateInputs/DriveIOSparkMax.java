package frc.robot.subsystems.drive;

import com.ctre.phoenix6.controls.VoltageOut;
import com.ctre.phoenix6.hardware.TalonFX;
import frc.robot.Constants.DriveConstants;

/**
 * 模擬器專用替身(由 FRC9427 模擬器在「工作副本」裡覆蓋,不會動到你原本的專案):
 * 這台電腦的 Windows「應用程式控制原則」會擋掉 REVLibDriver.dll,SparkMax 在模擬裡載不起來,
 * 所以底盤四顆馬達改用 Phoenix TalonFX 代替(同 CAN ID),由模擬器的物理 agent 驅動。
 * 這一版對應「updateInputs(DriveIOInputs)」介面的 DriveIO(GitHub 上的 FRC9427-LEO);出力 = duty × 12 V。
 */
public class DriveIOSparkMax implements DriveIO {
  private static final double kNominalVolts = 12.0;
  private final TalonFX m_leftLeader = new TalonFX(DriveConstants.kLeftLeaderCanId);
  private final TalonFX m_leftFollower = new TalonFX(DriveConstants.kLeftFollowerCanId);
  private final TalonFX m_rightLeader = new TalonFX(DriveConstants.kRightLeaderCanId);
  private final TalonFX m_rightFollower = new TalonFX(DriveConstants.kRightFollowerCanId);
  private final VoltageOut m_req = new VoltageOut(0.0);
  private double m_left;
  private double m_right;

  public DriveIOSparkMax() {}

  @Override
  public void updateInputs(DriveIOInputs inputs) {
    inputs.leftOutput = m_left;
    inputs.rightOutput = m_right;
    inputs.leftCurrentAmps = 0.0;
    inputs.rightCurrentAmps = 0.0;
  }

  @Override
  public void setDutyCycle(double left, double right) {
    m_left = left;
    m_right = right;
    double l = left * kNominalVolts;
    double r = right * kNominalVolts;
    m_leftLeader.setControl(m_req.withOutput(l));
    m_leftFollower.setControl(m_req.withOutput(l));
    m_rightLeader.setControl(m_req.withOutput(r));
    m_rightFollower.setControl(m_req.withOutput(r));
  }
}
