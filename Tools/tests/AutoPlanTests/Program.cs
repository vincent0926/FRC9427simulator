using System;
using System.Collections.Generic;
using FrcSim;

// AutoPlan / AutoRunner 單元測試(不需要 Unity)。結束碼 0 = 全部通過。
static class Program
{
    static int fails, passes;
    static void Check(bool c, string msg)
    {
        if (c) passes++; else fails++;
        Console.WriteLine((c ? "  ok   " : "  FAIL ") + msg);
    }
    static bool Near(float a, float b, float tol = 1e-3f) => Math.Abs(a - b) <= tol;

    static int Main()
    {
        Console.WriteLine("Station names / parse / opposite");
        for (int s = 0; s < AutoPlan.Count; s++)
        {
            Check(AutoPlan.Parse(AutoPlan.Names[s]) == s, $"Parse({AutoPlan.Names[s]}) == {s}");
            Check(AutoPlan.Parse(AutoPlan.Labels[s]) == s, $"Parse(\"{AutoPlan.Labels[s]}\") == {s}");
            Check(AutoPlan.Opposite(AutoPlan.Opposite(s)) == s, $"Opposite twice returns {AutoPlan.Names[s]}");
            Check(AutoPlan.IsRed(s) != AutoPlan.IsRed(AutoPlan.Opposite(s)), $"Opposite of {AutoPlan.Names[s]} is other alliance");
            Check(AutoPlan.Number(s) == AutoPlan.Number(AutoPlan.Opposite(s)), $"Opposite of {AutoPlan.Names[s]} keeps station number");
        }
        Check(AutoPlan.Parse("purple9") == -1, "unknown station name -> -1");
        Check(AutoPlan.Clamp(-1) == 1 && AutoPlan.Clamp(99) == 1, "out-of-range station falls back to Blue 2");

        Console.WriteLine("Start poses");
        var starts = new List<(float x, float y)>();
        for (int s = 0; s < AutoPlan.Count; s++)
        {
            AutoPlan.StartPose(s, out float x, out float y, out float h);
            starts.Add((x, y));
            bool red = AutoPlan.IsRed(s);
            float near = red ? AutoPlan.FieldLength - x : x;   // 離自己聯盟牆的距離
            Check(near + AutoPlan.RobotHalf <= AutoPlan.AllianceZoneDepth && near - AutoPlan.RobotHalf > 0f, $"{AutoPlan.Names[s]} start fully behind its starting line (x={x:0.00})");
            Check(y - AutoPlan.RobotHalf > 0f && y + AutoPlan.RobotHalf < AutoPlan.FieldWidth, $"{AutoPlan.Names[s]} start inside field width (y={y:0.00})");
            Check(Near(h, red ? 180f : 0f), $"{AutoPlan.Names[s]} faces the field (heading {h:0})");
            AutoPlan.StartPose(AutoPlan.Opposite(s), out float ox, out float oy, out float oh);
            Check(Near(ox, AutoPlan.FieldLength - x) && Near(oy, AutoPlan.FieldWidth - y) && Near(AutoPlan.WrapDeg(oh - h - 180f), 0f), $"{AutoPlan.Names[s]} is point-mirror of {AutoPlan.Names[AutoPlan.Opposite(s)]}");
        }
        for (int i = 0; i < starts.Count; i++)
            for (int j = i + 1; j < starts.Count; j++)
            {
                float dx = starts[i].x - starts[j].x, dy = starts[i].y - starts[j].y;
                Check(Math.Sqrt(dx * dx + dy * dy) > 2 * AutoPlan.RobotHalf + 0.1, $"{AutoPlan.Names[i]} and {AutoPlan.Names[j]} starts do not overlap");
            }
        AutoPlan.StartPose(0, out _, out float b1y, out _);
        AutoPlan.StartPose(2, out _, out float b3y, out _);
        Check(b1y > b3y, "Blue 1 is on the high-y side (driver left), Blue 3 on the low-y side");

        Console.WriteLine("Routines (mirroring, geometry)");
        for (int s = 0; s < AutoPlan.Count; s++)
        {
            var r = AutoPlan.Routine(s);
            var o = AutoPlan.Routine(AutoPlan.Opposite(s));
            Check(r.Count == o.Count, $"{AutoPlan.Names[s]} routine has same steps as mirror");
            bool mirrored = true;
            for (int i = 0; i < r.Count && i < o.Count; i++)
                mirrored &= r[i].Kind == o[i].Kind && Near(r[i].X, AutoPlan.FieldLength - o[i].X) && Near(r[i].Y, AutoPlan.FieldWidth - o[i].Y) && Near(AutoPlan.WrapDeg(r[i].HeadingDeg - o[i].HeadingDeg - 180f), 0f);
            Check(mirrored, $"{AutoPlan.Names[s]} routine is point-mirror of {AutoPlan.Names[AutoPlan.Opposite(s)]}");
            Check(r.Exists(st => st.Kind == AutoStepKind.Shoot), $"{AutoPlan.Names[s]} routine shoots");
            AutoPlan.HubCenter(AutoPlan.IsRed(s), out float hx, out float hy);
            foreach (var st in r)
                if (st.Kind == AutoStepKind.Shoot)
                {
                    float d = (float)Math.Sqrt((st.X - hx) * (st.X - hx) + (st.Y - hy) * (st.Y - hy));
                    Check(d >= 2.0f && d <= 4.0f, $"{AutoPlan.Names[s]} {st.Name} is {d:0.00} m from own HUB (shooter table 2~4 m)");
                }
        }
        Check(AutoPlan.Routine(0) != AutoPlan.Routine(0), "Routine() returns a fresh list each call (mirroring must not mutate shared steps)");
        Check(Near(AutoPlan.Routine(3)[0].X, AutoPlan.FieldLength - AutoPlan.Routine(0)[0].X), "calling Routine(red) twice still mirrors once");

        Console.WriteLine("Simulated AUTO (point-mass robot, 12 m/s^2, 8 shots/s, intake 4 balls/s in DEPOT / neutral pile)");
        for (int r = 0; r < AutoPlan.RoutineCount; r++)
            for (int s = 0; s < AutoPlan.Count; s++)
            {
                var routine = (AutoRoutine)r;
                var eff = AutoPlan.Resolve(s, routine);
                string tag = $"{AutoPlan.Names[s]}/{AutoPlan.RoutineNames[r]}";
                var res = Simulate(s, routine);
                Check(res.done, $"{tag} finishes within 20 s (t={res.t:0.0} s)");
                if (eff == AutoRoutine.None) { Check(res.shots == 0 && res.moved < 0.01f, $"{tag} does nothing"); continue; }
                Check(res.shots >= 8, $"{tag} fires preload ({res.shots} shots)");
                if (eff == AutoRoutine.Depot || eff == AutoRoutine.Neutral) Check(res.collected > 0 && res.shots > 8, $"{tag} collects and shoots again (collected {res.collected}, shots {res.shots})");
                Check(!res.hitObstacle, $"{tag} path never enters a HUB / TRENCH post / BUMP / TOWER post / field wall {res.why}");
                if (eff == AutoRoutine.Neutral) Check(res.maxWallDist + AutoPlan.RobotHalf < AutoPlan.FieldLength / 2f, $"{tag} never crosses the center line (front at {res.maxWallDist + AutoPlan.RobotHalf:0.00} m)");
                else Check(res.stayedHome, $"{tag} stays in its own alliance zone");
            }
        Check(AutoPlan.Resolve(0, AutoRoutine.ByStation) == AutoRoutine.Depot && AutoPlan.Resolve(4, AutoRoutine.ByStation) == AutoRoutine.PreloadOnly, "ByStation = DEPOT for station 1, preload only otherwise");
        Check(AutoPlan.ParseRoutine("neutral") == (int)AutoRoutine.Neutral && AutoPlan.ParseRoutine("x") == -1 && AutoPlan.ClampRoutine(99) == AutoRoutine.ByStation, "routine parse / clamp");
        for (int r = 0; r < AutoPlan.RoutineCount; r++)
            for (int s = 0; s < 3; s++)
            {
                var b = AutoPlan.Routine(s, (AutoRoutine)r); var o = AutoPlan.Routine(s + 3, (AutoRoutine)r);
                bool m = b.Count == o.Count;
                for (int i = 0; m && i < b.Count; i++) m = Near(b[i].X, AutoPlan.FieldLength - o[i].X) && Near(b[i].Y, AutoPlan.FieldWidth - o[i].Y);
                Check(m, $"{AutoPlan.RoutineNames[r]} routine for {AutoPlan.Names[s]} mirrors {AutoPlan.Names[s + 3]}");
            }

        Console.WriteLine("Runner edge cases");
        {
            var rr = new AutoRunner(1);
            var c = rr.Update(0f, 0f, 0f, 8, 0.02f);
            Check(Math.Sqrt(c.Vx * c.Vx + c.Vy * c.Vy) <= rr.MaxSpeed + 1e-3, "commanded speed is limited");
            Check(Math.Abs(c.Omega) <= rr.MaxOmega + 1e-3, "commanded omega is limited");
            // 一直卡住(位置不動):每步都會逾時,最後仍會結束
            var stuck = new AutoRunner(0);
            float t = 0; while (!stuck.Done && t < 60f) { stuck.Update(8f, 4f, 0f, 8, 0.02f); t += 0.02f; }
            Check(stuck.Done, $"blocked robot still ends the routine by timeouts (t={t:0.0} s)");
            var done = stuck.Update(8f, 4f, 0f, 8, 0.02f);
            Check(done.Done && done.Vx == 0f && done.Vy == 0f && !done.Shoot && !done.Intake, "finished runner commands nothing");
            // 航向繞回:目標 180°,目前 -179° → 只差 1°,不能繞一大圈
            var wr = new AutoRunner(3);
            AutoPlan.StartPose(3, out float rx, out float ry, out _);
            var w = wr.Update(rx, ry, -179f * (float)Math.PI / 180f, 8, 0.02f);
            Check(w.Omega < 0f && Math.Abs(w.Omega) < 0.2f, $"heading error wraps around 180 deg (omega={w.Omega:0.000})");
        }

        Console.WriteLine($"\n{passes} passed, {fails} failed");
        Console.WriteLine(fails == 0 ? "PASS" : "FAIL");
        return fails == 0 ? 0 : 1;
    }

    struct SimResult { public bool done, hitObstacle, stayedHome; public int shots, collected; public float t, moved, maxWallDist; public string why; }

    // 簡化的底盤:速度朝指令靠近(加速度上限),在射擊步驟且幾乎停住時每 0.125 s 射一顆,intake 在 DEPOT 內每 0.25 s 吃一顆
    static SimResult Simulate(int station, AutoRoutine routine = AutoRoutine.ByStation)
    {
        var r = new AutoRunner(station, routine);
        AutoPlan.StartPose(station, out float x, out float y, out float hd);
        float h = hd * (float)Math.PI / 180f, vx = 0, vy = 0, w = 0, t = 0, shotTimer = 0, intakeTimer = 0;
        int held = 8, depotBalls = 24, neutralBalls = 200;
        float x0 = x, y0 = y;
        var res = new SimResult { stayedHome = true, why = "" };
        bool red = AutoPlan.IsRed(station);
        AutoPlan.HubCenter(red, out float hx, out float hy);
        const float dt = 0.02f, accel = 12f, alpha = 30f;
        while (t < 20f)
        {
            var c = r.Update(x, y, h, held, dt);
            if (c.Done) { res.done = true; break; }
            float dvx = c.Vx - vx, dvy = c.Vy - vy, dv = (float)Math.Sqrt(dvx * dvx + dvy * dvy), maxDv = accel * dt;
            if (dv > maxDv) { dvx *= maxDv / dv; dvy *= maxDv / dv; }
            vx += dvx; vy += dvy;
            w += Math.Max(-alpha * dt, Math.Min(alpha * dt, c.Omega - w));
            x += vx * dt; y += vy * dt; h = AutoPlan.WrapRad(h + w * dt);
            float speed = (float)Math.Sqrt(vx * vx + vy * vy);
            if (c.Shoot && held > 0 && speed < 0.3f) { shotTimer += dt; while (shotTimer >= 0.125f && held > 0) { shotTimer -= 0.125f; held--; res.shots++; } }
            float wallDist = red ? AutoPlan.FieldLength - x : x;
            bool inDepot = Math.Abs(y - (red ? AutoPlan.FieldWidth - AutoPlan.DepotY : AutoPlan.DepotY)) < 0.5f && wallDist < 1.2f;
            if (c.Intake && inDepot && depotBalls > 0) { intakeTimer += dt; while (intakeTimer >= 0.25f && depotBalls > 0) { intakeTimer -= 0.25f; depotBalls--; held++; res.collected++; } }
            // 中立區球堆約 x 7.4~9.1、y 3.1~5.0;intake 在車頭前 0.4~0.9 m
            float fx = x + (float)Math.Cos(h) * 0.65f, fy = y + (float)Math.Sin(h) * 0.65f;
            bool inPile = fx > 7.35f && fx < 9.2f && fy > 3.05f && fy < 5.05f;
            if (c.Intake && inPile && neutralBalls > 0) { intakeTimer += dt; while (intakeTimer >= 0.25f && neutralBalls > 0) { intakeTimer -= 0.25f; neutralBalls--; held++; res.collected++; } }
            // 障礙物:HUB 方塊、TOWER 兩根立柱(離牆 1.02~1.11 m、y 3.30~3.34 / 4.16~4.19)、場邊牆
            float hr = AutoPlan.HubSize / 2f + AutoPlan.RobotHalf;
            if (Math.Abs(x - hx) < hr && Math.Abs(y - hy) < hr) { res.hitObstacle = true; res.why = $"(HUB at t={t:0.0})"; }
            foreach (float py in new[] { 3.32f, 4.175f })
            {
                float pyy = red ? AutoPlan.FieldWidth - py : py;
                if (Math.Abs(wallDist - 1.065f) < 0.05f + AutoPlan.RobotHalf && Math.Abs(y - pyy) < 0.03f + AutoPlan.RobotHalf) { res.hitObstacle = true; res.why = $"(TOWER post at t={t:0.0} x={x:0.00} y={y:0.00} step={c.Step})"; }
            }
            if (wallDist < AutoPlan.RobotHalf - 0.01f || y < AutoPlan.RobotHalf - 0.01f || y > AutoPlan.FieldWidth - AutoPlan.RobotHalf + 0.01f) { res.hitObstacle = true; res.why = $"(wall at t={t:0.0})"; }
            if (wallDist > AutoPlan.AllianceZoneDepth) res.stayedHome = false;
            res.maxWallDist = Math.Max(res.maxWallDist, wallDist);
            // TRENCH 立柱(場邊 1.279~1.584 m)、BUMP(場邊 1.584~3.438 m)都在 HUB 中心 ±0.6 m 的 x 範圍;BUMP 可以開過去但路線不該走
            float hubNear = AutoPlan.AllianceZoneDepth + AutoPlan.HubSize / 2f, xr = AutoPlan.HubSize / 2f + AutoPlan.RobotHalf;
            float side = Math.Min(y, AutoPlan.FieldWidth - y);
            if (Math.Abs(wallDist - hubNear) < xr && side + AutoPlan.RobotHalf > 1.279f && side - AutoPlan.RobotHalf < 3.438f && !(side > 2.4f && Math.Abs(y - hy) < hr)) { res.hitObstacle = true; res.why = $"(TRENCH post / BUMP at t={t:0.0}, y={y:0.00})"; }
            t += dt;
        }
        res.t = t;
        res.moved = (float)Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0));
        return res;
    }
}
