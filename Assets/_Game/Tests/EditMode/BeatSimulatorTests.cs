using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>回合一拍规则测试（T/C/B/S 系列，文档 14.2）。所有断言读取 nextState（模拟返回克隆）。</summary>
    public class BeatSimulatorTests
    {
        private GameConfig cfg;
        private BeatSimulator sim;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<GameConfig>();
            sim = new BeatSimulator(cfg);
        }

        [TearDown]
        public void TearDown()
        {
            if (cfg != null)
                Object.DestroyImmediate(cfg);
        }

        // ---- 构造辅助 ----

        /// <summary>13×11 外墙房间，可附加内墙（供本类与 FruitBeatTests 共用）。</summary>
        internal static BoardModel Board(params Vector2Int[] innerWalls)
        {
            var walls = new List<Vector2Int>();
            for (int x = 0; x < 13; x++)
            {
                walls.Add(new Vector2Int(x, 0));
                walls.Add(new Vector2Int(x, 10));
            }
            for (int y = 0; y < 11; y++)
            {
                walls.Add(new Vector2Int(0, y));
                walls.Add(new Vector2Int(12, y));
            }
            walls.AddRange(innerWalls);
            return new BoardModel(13, 11, walls);
        }

        private static Vector2Int Node(int halfX, int halfY) => new Vector2Int(
            halfX < 3 ? halfX * 5 : 13 + (halfX - 3) * 5, halfY * 5);

        private static SwordState Sword(int tip = 3)
        {
            var s = SwordState.CreateInitial();
            for (int n = 3; n < tip; n++)
                s.edges.Add(BladeSegment.Canonical(Node(n, 0), Node(n + 1, 0)));
            return s;
        }

        private static SwordState SwordWith(params int[][] edgeHalves)
        {
            var s = SwordState.CreateInitial();
            foreach (var e in edgeHalves)
                s.edges.Add(BladeSegment.Canonical(Node(e[0], e[1]), Node(e[2], e[3])));
            return s;
        }

        // 仅用于验证与初始长度无关的碰撞边界：复原旧测试所需的 1.5/2.0 格直刃。
        private static SwordState CollisionFixtureSword(int tip = 3)
        {
            var s = SwordState.CreateInitial();
            s.edges[1] = BladeSegment.Canonical(new Vector2Int(10, 0), new Vector2Int(15, 0));
            for (int n = 3; n < tip; n++)
                s.edges.Add(BladeSegment.Canonical(new Vector2Int(n * 5, 0), new Vector2Int((n + 1) * 5, 0)));
            return s;
        }

        /// <summary>完整拍辅助：玩家阶段 + 敌人阶段立即续完（无果触发场景）。board 参数仅为兼容旧调用形状。</summary>
        private BeatSimulation Beat(GameState state, PlayerAction action, BoardModel board)
        {
            var pr = sim.BeginPlayerAction(state, action, board);
            if (!pr.isValid)
                return new BeatSimulation
                {
                    isValid = false,
                    invalidReason = pr.invalidReason,
                    nextState = state,
                    presentation = pr.presentation,
                };
            var er = sim.ResumeEnemyPhase(pr.state, board, pr.presentation);
            return new BeatSimulation { isValid = true, nextState = er.state, presentation = er.presentation };
        }

        private static GameState State(Vector2Int playerCell, int facing, SwordState sword = null)
        {
            return new GameState
            {
                sessionId = 1,
                roomIndex = 0,
                beatIndex = 0,
                player = new PlayerState { cell = playerCell, facing = facing, hp = 4 },
                sword = sword ?? SwordState.CreateInitial(),
                enemies = new List<EnemyState>(),
            };
        }

        private static EnemyState AddEnemy(GameState s, int actorId, EnemyKind kind, Vector2Int cell, int hp = 2)
        {
            var e = new EnemyState { actorId = actorId, kind = kind, cell = cell, hp = hp };
            s.enemies.Add(e);
            return e;
        }

        private static EnemyState NextEnemy(GameState next, int actorId)
        {
            foreach (var e in next.enemies)
                if (e.actorId == actorId)
                    return e;
            return null;
        }

        private static int HitCountOn(ActionPresentation p, int actorId)
        {
            int n = 0;
            foreach (var h in p.hits)
                if (h.actorId == actorId)
                    n++;
            return n;
        }

        private static HitEvent HitOn(ActionPresentation p, int actorId)
        {
            foreach (var h in p.hits)
                if (h.actorId == actorId)
                    return h;
            return default;
        }

        // ---- T 系列：旋转与墙 ----

        [Test]
        public void T01_OpenAreaRotation_Completes90Degrees()
        {
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0);
            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(result.isValid);
            Assert.AreEqual(1, result.nextState.player.facing, "逆时针 East→North");
            Assert.IsFalse(result.presentation.rotateBlocked);
        }

        [Test]
        public void T02_WallBeyond90DegreeEndpoint_NoRebound()
        {
            // 轴心 (2.5,3.5)：旋转全程与墙 (5,2) 最近 1.19、与底墙 1.0，均超出 90° 终点
            var board = Board(new Vector2Int(5, 2));
            var state = State(new Vector2Int(2, 3), 0, Sword(tip: 3));
            var result = Beat(state, PlayerAction.Rotate(-1), board);

            Assert.IsTrue(result.isValid);
            Assert.IsFalse(result.presentation.rotateBlocked, "墙在 90° 终点之外，不应回弹");
            Assert.AreEqual(3, result.nextState.player.facing, "顺时针 East→South");
        }

        [Test]
        public void T03_MidSweepCornerGraze_FindsContact()
        {
            // 轴心 (2.5,2.5)，柱 (4,1) 角 (4,2)：半径 1.581，尖端 1.5 → 顺时针约 16°~22° 处接触
            var board = Board(new Vector2Int(4, 1));
            var state = State(new Vector2Int(2, 2), 0, CollisionFixtureSword());
            var result = Beat(state, PlayerAction.Rotate(-1), board);

            Assert.IsTrue(result.isValid);
            Assert.IsTrue(result.presentation.rotateBlocked, "扫掠中途应擦到柱角");
            var p = result.presentation;
            Assert.GreaterOrEqual(p.wallU, 0.10f, "首次接触进度应在合理区间");
            Assert.LessOrEqual(p.wallU, 0.35f, "首次接触进度应在合理区间");
            Assert.AreEqual(new Vector2Int(4, 1), p.wallCell);
            Assert.AreEqual(0, result.nextState.player.facing, "回弹保持原朝向");
        }

        [Test]
        public void T04_OldTipBudHitsWallBeforeMainTip_ReportsBudSegment()
        {
            // 轴心 (2.5,2.5) 朝东；OldTip 芽尖局部 (1.5,-1.0) 半径 1.803；
            // 外墙 (3,0) 上边 y=1 最近半径 1.871：侧芽约 17°~24° 接触，主刃（1.5+0.1）永不可达
            var board = Board(); // (3,0) 属于外墙 y=0
            var sword = CollisionFixtureSword();
            sword.edges.Add(BladeSegment.Canonical(new Vector2Int(15, 0), new Vector2Int(15, -10)));
            var state = State(new Vector2Int(2, 2), 0, sword);
            var result = Beat(state, PlayerAction.Rotate(-1), board);

            Assert.IsTrue(result.isValid);
            Assert.IsTrue(result.presentation.rotateBlocked, "侧芽应先撞墙");
            Assert.AreEqual(2, result.presentation.wallSegmentIndex, "主刃两节之后侧芽排序索引为 2");
            Assert.GreaterOrEqual(result.presentation.wallU, 0.15f, "侧芽应在扫掠前段接触外墙");
            Assert.LessOrEqual(result.presentation.wallU, 0.65f);
        }

        [Test]
        public void T05_StartNearWallRotatingAway_NotLocked()
        {
            // 位姿级：尖端距墙面 0.101（间隙 0.001 ≤ 容差）但未穿入；逆时针转离必须放行
            var board = Board(new Vector2Int(4, 2));
            var solver = new TrajectorySolver(cfg);
            var sword = Sword(tip: 3);

            var away = solver.FindFirstWallContactPose(
                new Vector2(4f - 1.3f - 0.101f, 2.5f), 0f, Mathf.PI / 2f, sword, board);
            Assert.IsFalse(away.hasContact, "分离运动可放行，不应报接触");
        }

        [Test]
        public void T05b_StartPressingIntoWall_ZeroProgressContact()
        {
            var board = Board(new Vector2Int(4, 2));
            var solver = new TrajectorySolver(cfg);
            var sword = Sword(tip: 3);

            // 尖端距面 0.05 < bladeRadius：已压入，接触进度 0
            var pressed = solver.FindFirstWallContactPose(
                new Vector2(4f - 1.3f - 0.05f, 2.5f), 0f, Mathf.PI / 2f, sword, board);
            Assert.IsTrue(pressed.hasContact);
            Assert.LessOrEqual(pressed.u, cfg.contactTolerance + 1e-6f);
        }

        [Test]
        public void T06_StartPenetratingWall_ZeroSweepRebound_NoDamage()
        {
            // 玩家 (3,2) 朝东 tip 3：尖端 (5.0,2.5) 恰在墙 (5,2) 面上（压入）
            var board = Board(new Vector2Int(5, 2));
            var state = State(new Vector2Int(3, 2), 0, CollisionFixtureSword());
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(4, 2)); // 静止重叠在剑身上
            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(result.isValid, "旋转撞墙仍有效，耗 1 拍");
            Assert.IsTrue(result.presentation.rotateBlocked);
            Assert.IsTrue(result.presentation.isZeroSweep, "压入起点应记为零扫角");
            Assert.AreEqual(1, result.nextState.beatIndex);
            Assert.AreEqual(0, result.nextState.player.facing, "回弹保持原朝向");
            Assert.AreEqual(2, NextEnemy(result.nextState, 0).hp, "零扫角不造成伤害");
            Assert.AreEqual(0, result.presentation.hits.Count);
        }

        [Test]
        public void T07_RotateHitsEnemy_ReboundsAtFirstContact()
        {
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, Sword(tip: 3));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(6, 6));

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(result.isValid);
            Assert.IsTrue(result.presentation.rotateBlocked, "碰敌时旋剑应被截断并回弹");
            Assert.AreEqual(StopReason.Enemy, result.presentation.stopReason);
            Assert.Greater(result.presentation.stopU, 0f);
            Assert.Less(result.presentation.stopU, 1f);
            Assert.AreEqual(0, result.nextState.player.facing, "回弹后保持原朝向");
            Assert.AreEqual(1, HitCountOn(result.presentation, 0), "阻挡怪物仍只受一次伤害");
        }

        // ---- C 系列：命中、去重与击退 ----

        [Test]
        public void C01_EnemyInFrontHit_EnemyBehindWallNotHit()
        {
            // 玩家 (2,5) tip 5 朝东顺时针扫；柱 (2,4) 角 (3,5) 在 u≈0.41 接触；
            // A(3,5) 静止重叠在剑身（u=0 先命中，切线向南击退），B(2,3) 被柱遮蔽
            var board = Board(new Vector2Int(2, 4));
            var state = State(new Vector2Int(2, 5), 0, Sword(tip: 5));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 5));
            AddEnemy(state, 1, EnemyKind.Charger, new Vector2Int(2, 3));

            var result = Beat(state, PlayerAction.Rotate(-1), board);

            Assert.IsTrue(result.presentation.rotateBlocked);
            Assert.AreEqual(1, HitCountOn(result.presentation, 0));
            var nextA = NextEnemy(result.nextState, 0);
            Assert.AreEqual(1, nextA.hp, "墙前敌人命中 1 点");
            Assert.AreEqual(new Vector2Int(3, 4), nextA.cell, "顺时针切线向南击退");
            Assert.AreEqual(2, NextEnemy(result.nextState, 1).hp, "墙后敌人不命中");
        }

        [Test]
        public void C02_ReboundPassesEnemyAgain_TotalDamageStillOne()
        {
            var board = Board(new Vector2Int(2, 4));
            var state = State(new Vector2Int(2, 5), 0, Sword(tip: 5));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 5));

            var result = Beat(state, PlayerAction.Rotate(-1), board);

            Assert.AreEqual(1, HitCountOn(result.presentation, 0), "回弹再次经过敌人不造成第二次伤害");
            Assert.AreEqual(1, NextEnemy(result.nextState, 0).hp);
        }

        [Test]
        public void C03_TranslationPathHitsWall_ReboundsAndConsumesBeat()
        {
            // 玩家 (2,4) tip 6 朝东；墙 (6,4)；剑尖将进入墙；墙前敌人 (5,4)
            var board = Board(new Vector2Int(6, 4));
            var state = State(new Vector2Int(2, 4), 0, Sword(tip: 6));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(5, 4));

            var result = Beat(state, PlayerAction.Move(Vector2Int.right), board);

            Assert.IsTrue(result.isValid);
            Assert.AreEqual(ActionInvalidReason.BladeHitsWall, result.presentation.invalidReason);
            Assert.AreEqual(1, result.nextState.beatIndex, "撞墙回弹仍耗一拍");
            Assert.AreEqual(2, NextEnemy(result.nextState, 0).hp, "墙前敌人也不受伤");
            Assert.AreEqual(new Vector2Int(2, 4), result.nextState.player.cell);
            Assert.AreEqual(0, result.presentation.hits.Count, "无效平移不生成命中事件");
        }

        [Test]
        public void C04_KnockbackIntoWall_AddsWallImpactDamage()
        {
            // 玩家 (2,4) tip 3 逆时针；敌人 (3,4) 静止重叠（u=0 命中）；
            // 逆时针切线向上，敌人被推向 (3,5)——该格为墙：+1 撞墙伤害致死
            var board = Board(new Vector2Int(3, 5));
            var state = State(new Vector2Int(2, 4), 0, Sword(tip: 3));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 4));

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.AreEqual(1, HitCountOn(result.presentation, 0));
            var hit = HitOn(result.presentation, 0);
            Assert.AreEqual(0, hit.hpAfter, "基础 1 伤 + 撞墙 1 伤 = 2，敌人 HP 2 直接死亡");
            Assert.IsTrue(hit.wallImpact);
            Assert.IsTrue(hit.died);
            Assert.IsNull(NextEnemy(result.nextState, 0), "死亡敌人从状态移除");
            Assert.AreEqual(1, result.presentation.knockbackWallKills);
        }

        [Test]
        public void C05_KnockbackIntoAlly_StaysNoWallDamage()
        {
            // A 是首个碰撞目标，剑立即回弹；A 向北的击退格被 B 占据，因此留在原格且无撞墙附伤。
            var board = Board();
            var state = State(new Vector2Int(2, 4), 0, CollisionFixtureSword(tip: 4));
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 4));
            AddEnemy(state, 1, EnemyKind.Charger, new Vector2Int(3, 5));

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(result.isValid);
            Assert.AreEqual(1, HitCountOn(result.presentation, 0));
            Assert.AreEqual(0, HitCountOn(result.presentation, 1), "首个怪物触发回弹后不得穿透命中 B");
            var nextA = NextEnemy(result.nextState, 0);
            var nextB = NextEnemy(result.nextState, 1);
            Assert.AreEqual(new Vector2Int(3, 4), nextA.cell, "被同伴挡住，留在原格");
            Assert.AreEqual(1, nextA.hp, "撞同伴无附加伤害");
            Assert.IsFalse(HitOn(result.presentation, 0).wallImpact);
            Assert.AreEqual(new Vector2Int(3, 5), nextB.cell, "未被命中的 B 保持原格");
        }

        [Test]
        public void C06_TwoEnemiesSameProgress_LowerActorIdBlocksFirst()
        {
            // 两个敌人位于同一首次接触位置；回弹只结算一个阻挡者，并按 actorId 稳定选择。
            var board = Board();
            var state = State(new Vector2Int(2, 4), 0, CollisionFixtureSword(tip: 4));
            AddEnemy(state, 5, EnemyKind.Charger, new Vector2Int(3, 4));
            AddEnemy(state, 2, EnemyKind.Charger, new Vector2Int(3, 4));

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.AreEqual(1, result.presentation.hits.Count, "首次怪物碰撞立即回弹，只结算一个阻挡者");
            Assert.AreEqual(2, result.presentation.hits[0].actorId, "同一接触进度由较小 actorId 稳定取得优先权");
        }

        // ---- B 系列：敌人意图 ----

        [Test]
        public void B01_HitChargerSkipsItsMoveThisBeat()
        {
            var board = Board();
            var state = State(new Vector2Int(2, 4), 0, Sword(tip: 4));
            var charger = AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 4));
            charger.intent = new EnemyIntent { kind = IntentKind.Move, targetCell = new Vector2Int(2, 4), direction = Vector2Int.left, interruptible = true };
            var bystander = AddEnemy(state, 1, EnemyKind.Charger, new Vector2Int(8, 6));
            bystander.intent = new EnemyIntent { kind = IntentKind.Move, targetCell = new Vector2Int(7, 6), direction = Vector2Int.left, interruptible = true };

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.AreEqual(1, HitCountOn(result.presentation, 0));
            Assert.AreEqual(new Vector2Int(3, 5), NextEnemy(result.nextState, 0).cell, "被命中后按逆时针切线向北击退 1 格");
            bool chargerMovedByIntent = false;
            foreach (var e in result.presentation.enemyEvents)
                if (e.actorId == 0 && (e.kind == EnemyEventKind.Move || e.kind == EnemyEventKind.AttackMove))
                    chargerMovedByIntent = true;
            Assert.IsFalse(chargerMovedByIntent, "被命中冲锋怪该拍不执行预存移动（击退除外）");
            Assert.AreEqual(1, result.nextState.beatIndex);
        }

        [Test]
        public void B02_PlayerWait_EnemiesActAndBeatAdvances()
        {
            var board = Board();
            var state = State(new Vector2Int(2, 4), 0);
            // Use a clear route: walking through the sword is now forbidden.
            var charger = AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(4, 3));
            charger.intent = new EnemyIntent { kind = IntentKind.Move, targetCell = new Vector2Int(3, 3), direction = Vector2Int.left, interruptible = true };

            var result = Beat(state, PlayerAction.Wait(), board);

            Assert.IsTrue(result.isValid);
            Assert.AreEqual(1, result.nextState.beatIndex);
            Assert.AreEqual(new Vector2Int(3, 3), NextEnemy(result.nextState, 0).cell);
            Assert.AreEqual(0, result.presentation.hits.Count, "等待时剑不攻击");
        }

        [Test]
        public void B03_CoreNonFatalStillPulses()
        {
            var board = Board(new Vector2Int(4, 3), new Vector2Int(8, 3), new Vector2Int(4, 7), new Vector2Int(8, 7));
            var state = State(new Vector2Int(4, 5), 0, Sword(tip: 4));
            var core = AddEnemy(state, 100, EnemyKind.Core, new Vector2Int(6, 5), hp: 3);
            core.intent = new EnemyIntent { kind = IntentKind.CorePulse, direction = new Vector2Int(1, 0), interruptible = false };

            var result = Beat(state, PlayerAction.Rotate(1), board);

            Assert.AreEqual(1, HitCountOn(result.presentation, 100));
            Assert.AreEqual(2, NextEnemy(result.nextState, 100).hp, "核心受 1 点非致命伤");
            Assert.AreEqual(3, result.nextState.player.hp, "核心命中后仍执行脉冲");
            Assert.AreEqual(Outcome.Continue, result.presentation.outcome);
        }

        [Test]
        public void B04_CoreLethalHit_ImmediateVictoryNoEnemyResponse()
        {
            var board = Board(new Vector2Int(4, 3), new Vector2Int(8, 3), new Vector2Int(4, 7), new Vector2Int(8, 7));
            var state = State(new Vector2Int(4, 5), 0, Sword(4));
            var core = AddEnemy(state, 100, EnemyKind.Core, new Vector2Int(6, 5), hp: 1);
            core.intent = new EnemyIntent { kind = IntentKind.CorePulse, direction = new Vector2Int(1, 0), interruptible = false };
            var guard = AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(9, 2));
            guard.intent = new EnemyIntent { kind = IntentKind.Move, targetCell = new Vector2Int(8, 2), direction = Vector2Int.left, interruptible = true };

            // 分段事务：玩家阶段即可判定核心目标完成；敌人阶段将被取消，不执行
            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(pr.isValid);
            Assert.AreEqual(1, HitCountOn(pr.presentation, 100));
            Assert.IsTrue(BeatSimulator.CoreObjectiveCompleted(pr.state), "核心被摧毁立即标记通关");
            var coreAfter = NextEnemy(pr.state, 100);
            Assert.IsNotNull(coreAfter);
            Assert.AreEqual(0, coreAfter.hp);
            Assert.AreEqual(4, pr.state.player.hp, "取消剩余敌人响应");
            Assert.AreEqual(new Vector2Int(9, 2), NextEnemy(pr.state, 0).cell, "守卫不行动");
            Assert.AreEqual(0, pr.presentation.enemyEvents.Count, "无敌人响应事件");
        }

        // ---- S 系列：确定性 ----

        [Test]
        public void S01_FourRotationsReturnToInitialPose()
        {
            var shapes = new List<SwordState>
            {
                Sword(3),
                Sword(8),
                SwordWith(new[] { 3, 0, 4, 0 }, new[] { 4, 0, 5, 0 }, new[] { 1, 0, 1, -1 }, new[] { 1, -1, 1, -2 }),
                SwordWith(new[] { 3, 0, 4, 0 }, new[] { 1, 0, 1, -1 }, new[] { 2, 0, 2, 1 }, new[] { 2, 1, 2, 2 }, new[] { 3, 0, 3, -1 }),
            };
            var board = Board();

            foreach (var shape in shapes)
            {
                var initial = SwordGeometry.GetSegments(shape);
                var state = State(new Vector2Int(6, 5), 0, shape.Clone());
                for (int i = 0; i < 4; i++)
                {
                    var result = Beat(state, PlayerAction.Rotate(1), board);
                    Assert.IsTrue(result.isValid, $"形状 {SwordGeometry.ShapeKey(shape)} 第 {i} 拍应有效");
                    state = result.nextState;
                }

                Assert.AreEqual(0, state.player.facing % 4, "四次逆时针回位");
                var final = SwordGeometry.GetSegments(state.sword);
                Assert.AreEqual(initial.Count, final.Count);
                for (int i = 0; i < initial.Count; i++)
                {
                    Assert.AreEqual(initial[i].aHalf, final[i].aHalf);
                    Assert.AreEqual(initial[i].bHalf, final[i].bHalf);
                }
            }
        }

        // ---- 补充：无效平移原因 ----

        [Test]
        public void Extra_MoveIntoWallTarget_ReboundsAndConsumesBeat()
        {
            // 目标格 (0,1) 是外墙：平移无效
            var board = Board();
            var state = State(new Vector2Int(1, 1), 2, Sword(tip: 3));
            var result = Beat(state, PlayerAction.Move(Vector2Int.left), board);
            Assert.IsTrue(result.isValid);
            Assert.AreEqual(ActionInvalidReason.BodyHitsWall, result.presentation.invalidReason);
            Assert.AreEqual(1, result.nextState.beatIndex);
        }

        [Test]
        public void Extra_MoveOntoEnemy_InvalidTargetOccupied()
        {
            var board = Board();
            var state = State(new Vector2Int(2, 4), 0);
            AddEnemy(state, 0, EnemyKind.Charger, new Vector2Int(3, 4));
            var result = Beat(state, PlayerAction.Move(Vector2Int.right), board);
            Assert.IsFalse(result.isValid);
            Assert.AreEqual(ActionInvalidReason.TargetOccupied, result.invalidReason);
        }

        [Test]
        public void Extra_WallBlockedMoveConsumesBeat()
        {
            var board = Board(new Vector2Int(6, 4));
            var state = State(new Vector2Int(2, 4), 0, Sword(6));
            var result = Beat(state, PlayerAction.Move(Vector2Int.right), board);
            Assert.IsTrue(result.isValid);
            Assert.AreEqual(1, result.nextState.beatIndex, "撞墙回弹消耗一拍");
        }
    }
}
