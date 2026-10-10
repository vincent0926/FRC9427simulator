# 架構說明(給想看懂或接手的人)

## 兩種執行模式
1. **內建行為**:Unity 內自己算底盤、機構、射擊(`SwerveDrive` / `RobotMechanisms` / `ShooterCalc`)。啟動快、穩定。
2. **真實機器人程式**:直接執行隊上的 WPILib Java 專案(`gradlew simulateJava`),Unity 當「虛擬硬體」。

## 真實程式模式的資料流
```
Java 機器人程式(WPILib simulateJava)
   │  HALSim WebSocket (ws://127.0.0.1:3300)           ← 搖桿、DS 狀態、馬達電壓
   │  本機 TCP 3399(SimAgent 推轉子位置/速度)         ← 低延遲的馬達回授
   ▼
Unity (HalSim.cs / MechSim.cs / NtSim.cs)
   · HalSim:WebSocket 收發,送虛擬搖桿(1~2 支)
   · SimAgent.java(-javaagent):在機器人 JVM 內用 Phoenix 6 SimState 同進程積分馬達物理,避開網路延遲
   · MechSim:把馬達轉速換成底盤速度(swerve:最小平方法;坦克:左右側平均)並推動 Unity 的車
   · RobotMechanisms:由飛輪/滾輪/手臂馬達狀態決定吸球、射擊(依 Sim\<專案>.mech.json 設定)
```
- `Tools/sim/*.mech.json`:每個機器人專案一份設定(馬達慣量/摩擦/限位、底盤型式、機構對應)。
- `Tools/sim/overlay/`:模擬專用的替身原始檔。例如 SparkMax 在部分 Windows 電腦會被「應用程式控制原則」擋掉 `REVLibDriver.dll`,所以在**工作副本**把 `DriveIOSparkMax` 換成 TalonFX 版本(原專案不會被改)。
- 專案在 OneDrive 或路徑含中文時,GradleRIO 不能 build → 自動複製到 `C:\FRC9427SimWork\<名稱>`。

## 場地
- 視覺:`Sim/field/Field2026.glb`(FIRST 官方 2026 REBUILT 場地 CAD,經 AdvantageScope 轉出)。載入後把程式蓋的方塊外觀隱藏。
- 物理:`FieldBuilder.cs` 的碰撞體,依官方圖面與場地模型逐項對照:HUB、TRENCH、BUMP(車身高度與傾斜由 `SwerveDrive` 依 BUMP 高度強制設定,所以機器人不和地板碰撞)、TOWER(兩根立柱)、DEPOT(貼地,無碰撞)。OUTPOST 在聯盟牆後面,場內無碰撞。
- FUEL 起始擺法直接取自場地模型內的預擺球位置。
- 傾印工具:`-dumpfield`(依名稱彙總模型零件外框,用來對照碰撞體)。

## 自動階段(AUTO)與起始站位
- 設定畫面「起始站位」選 Blue 1/2/3 或 Red 1/2/3(或命令列 `-station blue1`)。藍方 1 號在場地 y 大的一側(藍方駕駛的左手邊);紅方 n 號是藍方 n 號繞場地中心旋轉 180° 的鏡像。
- 起點:保險桿在起始線(聯盟區邊界)後 5 cm,車頭朝場內。第二台機器人放在對面聯盟的同號站位。
- **真實程式模式**:站位送進 HALSim 的 DriverStation(`>station`),機器人程式用 `DriverStation.getAlliance()` / `getLocation()` 讀得到;比賽時鐘 AUTO 20 秒期間 DS 是 autonomous,跑的是機器人程式自己的自動程式。程式啟動中(還沒連上)時鐘暫停,連上後才從 AUTO 開始。
- **自由練習**:玩家的車從開賽就由玩家操控(不跑內建自動);**第二台機器人**:跑 `AutoPlan.cs` 的內建路線(`AutoDrive.cs` 執行):倒退到射擊點 → 射完 8 顆預載;1 號站位再去 DEPOT 吃球、回來再射。AUTO 期間手把無效,按 N 跳過。
- 內建路線可在設定畫面「內建自動路線」另外選(或 `-routine bystation|preload|depot|neutral|none`):依站位(1 號 DEPOT、其他只射預載)/ 只射預載 / 預載 + DEPOT / 預載 + 中立區(穿過 TRENCH 吃球、在中立區這側射)/ 不動。
- **開賽前(真實程式模式)**:程式連上後先 Disabled,`NtChooser.cs` 從 NetworkTables 找 SmartDashboard 的自動程式選單(SendableChooser / PathPlanner / AdvantageKit 的 LoggedDashboardChooser 都是 "String Chooser"),畫面上用 [ / ] 或十字鍵換選項、Enter 或 A 開賽(有選單 20 秒、沒有 4 秒後自動開賽)。只會送清單內的選項(送不存在的名稱,`getSelected()` 會回傳 null)。上次選的會記住。
- **起點位姿**:模擬器在 NetworkTables 發布 `/FRCSim/StartPose`、`/FRCSim/RobotPose`(double[] x, y, 航向度)、`/FRCSim/Station`。機器人程式放 `Tools/sim/robot-example/FrcSimPose.java`,在 autonomousInit 呼叫 `FrcSimPose.startPose().ifPresent(drive::resetPose)`,沒有視覺的機器人自動路線也會從正確的位置開始(上機時沒有值,不影響)。
- 替身檔 `Tools/sim/overlay/DriveIOSparkMax.java` 有兩種介面版本:專案的 DriveIO 是 `updateInputs(...)` 那種(GitHub 上的 LEO)會自動改用 `overlay/updateInputs/` 的版本。
- `AutoPlan.cs` 不依賴 UnityEngine,單元測試:`dotnet run --project Tools/tests/AutoPlanTests`(需要 .NET 8 SDK)。

## 自動模式(遊戲模式選單)
- 主選單 → 開始 → 「自動模式(跑你自己寫的自動)」:選機器人程式專案、站位 → 開賽前選你程式的自動程式 → 只跑 AUTO 20 秒(+3 秒計分寬限)→ 顯示 AUTO 得分;Enter / A 再跑一次、Esc 回主選單。
- 直接用選的專案執行(不受「坦克模式只跑坦克專案」限制)。

## 真實程式的 swerve:跟隨程式位姿
- 有用 PathPlanner 時(PathPlannerLib 會發布 `/PathPlanner/targetPose`):車子照 PathPlanner 正在跟的目標點走,也就是路線設計的位置與朝向;陀螺儀與視覺都回報車子的真實位姿,程式自己的位姿會收斂到同一點。`-realautotest ... -automode` 會讀 .path 貝茲曲線,量車子離路線多遠(9427newnewpro Blue3:平均 0.01 m、最大 0.11 m)。
- 機器人程式在 SmartDashboard 放 Field2d(例如 `SmartDashboard.putData("Field", field)`)時,模擬器的車會照「程式自己估的位姿」(`Field/Robot`)開:程式的里程計來自它自己的馬達與運動學,所以 PathPlanner 路線會照預覽跑;車子仍是剛體(推球、被牆擋)。設定檔 `chassis.followCodePose: false` 可關閉,改用馬達速度推算(實驗中)。
- 開賽前會等 simagent 開始推馬達資料(機器人程式啟動後約 8 秒)才開始 AUTO,否則照時間跑的路線會在車子還不能動時就跑掉。
- Phoenix 6 的 Pigeon2 不吃 HALSim 輸入:航向經 simagent 寫進 Pigeon2 SimState(Unity → agent 的 TCP 行 `yaw <id> <度> <度/秒>`)。

- 機器人程式的「前方」(PathPlanner 0°)是 shooter 那面時,在設定檔 `chassis.intakeSide` 寫 `"back"`:吸球範圍、橘色 intake 手臂放到車尾,匯入的模型多轉 180°(9427newnewpro、FRC9427swerve 已設定)。

## 內建射擊
- 4 m 以內用機器人程式的查表(ShooterCalc);超過 4 m(過中線的遠射)固定 hood 20°,用彈道解(重力 + 0.375 線性阻力、60 Hz,和球的物理一致)算出剛好落進 HUB 中心的球速。`-shottest`:自己聯盟區到過中線 6 m,每點 8 顆全進。

## 自動測試(皆加 `-batchmode -nographics`)
| 參數 | 內容 |
|---|---|
| `-autotest [-station blue1] [-routine neutral]` | 內建自動階段:起始站位、兩台機器人 AUTO 射完預載並得分、AUTO 結束後交還手動(結果 autotest.txt) |
| `-autotest -fakecode <資料夾>` | 真實程式啟動失敗(資料夾內放一個馬上結束的 gradlew.bat):比賽時鐘不會卡在 AUTO、第二台機器人照常跑自動 |
| `-realautotest <專案> -station red2 -choose "Drive Forward" -portoffset 50` | 真實程式的自動階段:找到自動程式選單、開賽前 Disabled、程式讀到選的自動程式 / 站位 / 起點(專案需有 "Auto Chooser" 並回報 SimTest/*;範例見報告) |
| `-freetest [-station blue2]` | 自由練習:沒按鍵車子不動、AUTO 期間手把能開 |
| `-realautotest <專案> -teleop ...` | 真實程式 TELEOP:推搖桿車子會動、放開會停 |
| `-shottest` | 內建射擊命中率:x 2.2~10 m 各點射 8 顆(結果 shottest.txt) |
| `-realautotest <專案> -automode ...` | 自動模式:AUTO 結束後停、Disabled,再跑一次程式能重新連上 |
| `-padtest -norealcode` | 假手把輸入走一遍:坦克開車、轉向、Intake、發射、車身傾斜 |
| `-fieldtest` | 隧道、立柱、BUMP 通過與抬升 |
| `-walltest` | 貼牆、進入 TOWER |
| `-balltest` | 衝進球堆不被卡住 |
| `-soaktest 秒數` | 長時間隨機操作,記錄記憶體/球數/幀時間 |
| `-leotest <專案>` | LEO(坦克 + 兩支手把)真實程式 |
| `-realtest <專案> -noclock` | swerve 真實程式(射擊、吃球) |
| `-portoffset N` | 連線埠偏移,可與已開著的模擬器同時測試 |

## 發佈與更新
- `Tools/publish-release.ps1`:打包完整 zip + **差異包** `delta-vX.zip`(只含改過的檔案)+ manifest,建立 GitHub Release。
- 程式啟動時查 GitHub 最新 Release;主畫面按 U → 下載差異包 → SHA-256 校驗 → 關閉 → `apply.ps1` 覆蓋檔案 → 重新開啟(`UpdateCheck.cs`)。

## 效能
- 幀率上限 30~144(預設 60,選單 30);物理 60Hz。
- 畫質:超取樣 100/125/150/200%(第二台相機貼圖,UI 在其後)、陰影開關、MSAA;連續 FPS 過低會自動降級;第一次啟動依 GPU 選預設(內顯 → 低)。
- `Tools/Set-High-Performance-GPU.bat`:筆電雙顯卡時讓 Windows 用高效能顯卡。
