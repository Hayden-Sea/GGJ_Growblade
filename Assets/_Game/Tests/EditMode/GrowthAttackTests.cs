using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>v1.4 伸长攻击与无上限回归（文档 14.3 A 系列 / 14.4 U 系列）。</summary>
    public class GrowthAttackTests
    {
        private GameConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (cfg != null)
                Object.DestroyImmediate(cfg);
        }

        // ---- 构造辅助 ----

        private static GameState State(Vector2Int playerCell, int facing, SwordState sword, int credits = 1)
        {
            return new GameState
            {
                sessionId = 1,
                roomIndex = 0,
                beatIndex = 3,
                player = new PlayerState { cell = playerCell, facing = facing, hp = 4 },
                sword = sword,
                enemies = new List<EnemyState>(),
                growthCredits = credits,
            };
        }

        private static Vector2Int Node(Vector2Int half) => new Vector2Int(
            half.x < 3 ? half.x * 5 : 13 + (half.x - 3) * 5, half.y * 5);

        private static GrowthCandidate Cand(Vector2Int anchor, Vector2Int newNode)
        {
            var nodeAnchor = Node(anchor);
            var nodeNew = Node(newNode);
            var edge = BladeSegment.Canonical(nodeAnchor, nodeNew);
            return new GrowthCandidate { edge = edge, anchor = nodeAnchor, newNode = nodeNew, edgeKey = edge.Key };
        }

        private static EnemyState AddCharger(GameState state, int actorId, Vector2Int cell, int hp = 2)
        {
            var e = new EnemyState { actorId = actorId, kind = EnemyKind.Charger, cell = cell, hp = hp };
            state.enemies.Add(e);
            return e;
        }

        private static EnemyState AddCore(GameState state, Vector2Int cell, int hp)
        {
            var e = new EnemyState { actorId = 100, kind = EnemyKind.Core, cell = cell, hp = hp };
            state.enemies.Add(e);
            return e;
        }

        // ---- A 系列：伸长攻击 ----

        [Test]
        public void A01_EnemyOnCandidatePath_IsLegalAndHit()
        {
            // 玩家 (6,5) 朝东：候选 (3,0)→(4,0) 世界 [8.0,8.5]×y5.5；敌人 (8,5) 圆心恰在端点
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            AddCharger(state, 0, new Vector2Int(8, 5));
            var coreCell = (Vector2Int?)null;

            // v1.4：敌人不再使候选非法，改为命中目标
            var (ok, reason) = GrowthPlacement.Filter(state.sword, Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)).edge,
                new Vector2Int(6, 5), 0, board, state, coreCell, cfg);
            Assert.IsTrue(ok, "敌人所在候选应合法：" + reason);

            var result = RunController.SimulateConfirm(state, board, coreCell, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid, result.invalidReason);
            Assert.AreEqual(1, result.hits.Count);
            var hit = result.hits[0];
            Assert.AreEqual(0, hit.actorId);
            Assert.AreEqual(2, hit.hpBefore, "敌人 hp2 → 1");
            Assert.AreEqual(1, hit.hpAfter, "敌人 hp2 → 1");
            Assert.AreEqual(1, result.nextState.enemies.Find(e => e.actorId == 0).hp);
            Assert.IsTrue(result.nextState.enemies.Find(e => e.actorId == 0).interruptedThisBeat,
                "成长命中打断本拍意图");
            // 初始尖端由 1.3 伸至 1.8；进入接触半径时约为 g=0.6。
            Assert.Greater(hit.g, 0.5f);
            Assert.Less(hit.g, 0.7f);
        }

        [Test]
        public void A02_Knockback_FollowsWorldDirectionOfAnchorToNewNode()
        {
            var board = BeatSimulatorTests.Board();
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1) };
            for (int facing = 0; facing < 4; facing++)
            {
                var state = State(new Vector2Int(6, 5), facing, SwordState.CreateInitial());
                var enemyCell = new Vector2Int(6, 5) + dirs[facing] * 2;
                AddCharger(state, 0, enemyCell);

                var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                    Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

                Assert.IsTrue(result.valid, $"facing={facing}: {result.invalidReason}");
                Assert.AreEqual(1, result.hits.Count, $"facing={facing}");
                var hit = result.hits[0];
                Assert.IsTrue(hit.movedByKnock, $"facing={facing} 应沿生长方向击退");
                Assert.AreEqual(enemyCell + dirs[facing], hit.knockTo, $"facing={facing} 世界击退方向");
            }
        }

        [Test]
        public void A03_KnockbackIntoWall_AddsDamageAndStays()
        {
            var board = BeatSimulatorTests.Board(new Vector2Int(9, 5));
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            AddCharger(state, 0, new Vector2Int(8, 5));

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid);
            var hit = result.hits[0];
            Assert.IsTrue(hit.wallImpact, "击退撞墙追加伤害");
            Assert.IsFalse(hit.movedByKnock);
            Assert.AreEqual(new Vector2Int(8, 5), hit.knockTo, "撞墙停留原格");
            Assert.AreEqual(0, hit.hpAfter, "1(剑伤) + 1(撞墙) = hp2 → 0");
            Assert.IsTrue(hit.died);
        }

        [Test]
        public void A03b_KnockbackIntoCompanion_StaysWithoutWallDamage()
        {
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            AddCharger(state, 0, new Vector2Int(8, 5));
            AddCharger(state, 1, new Vector2Int(9, 5));

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid);
            Assert.AreEqual(1, result.hits.Count, "同伴不在新边路径上不受击");
            var hit = result.hits[0];
            Assert.IsFalse(hit.movedByKnock, "击退被同伴阻挡");
            Assert.IsFalse(hit.wallImpact, "被同伴阻挡不触发撞墙伤害");
            Assert.AreEqual(1, hit.hpAfter);
        }

        [Test]
        public void A05_EnemyTouchingOldBladeAtGrowthStart_IsExcluded()
        {
            // 核心 (7,5) 圆心 (7.5,5.5) 与旧剑边 [7.5,8.0] 接触；新边 [8.0,8.5] 半径 0.5 内
            // 若无排除规则会在 g≈0 二次受击；v1.4 规定成长开始已碰旧剑的目标不由本根攻击
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            var core = AddCore(state, new Vector2Int(7, 5), 3);

            var events = GrowthSimulator.Simulate(state.Clone(), Node(new Vector2Int(3, 0)), Node(new Vector2Int(4, 0)),
                0, new Vector2Int(6, 5), board, cfg, 1);

            Assert.AreEqual(0, events.Count, "起始已碰旧剑的核心不受本次伸长攻击");
            Assert.AreEqual(3, core.hp);
        }

        [Test]
        public void A06_ConsecutiveGrowths_EachHasIndependentHitSet()
        {
            // 敌人 hp4：第 1 根命中并击退到 (9,5)；第 2 根够不到；第 3 根再次命中（独立去重）
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial(), credits: 3);
            var enemy = AddCharger(state, 0, new Vector2Int(8, 5), hp: 4);

            var r1 = RunController.SimulateConfirm(state, board, null, cfg, 1, Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));
            Assert.IsTrue(r1.valid);
            Assert.AreEqual(1, r1.hits.Count, "第 1 根命中");
            var after1 = r1.nextState;

            var r2 = RunController.SimulateConfirm(after1, board, null, cfg, 2, Cand(new Vector2Int(4, 0), new Vector2Int(5, 0)));
            Assert.IsTrue(r2.valid);
            Assert.AreEqual(0, r2.hits.Count, "敌人已被击退出第 2 根范围");
            var after2 = r2.nextState;

            var r3 = RunController.SimulateConfirm(after2, board, null, cfg, 3, Cand(new Vector2Int(5, 0), new Vector2Int(6, 0)));
            Assert.IsTrue(r3.valid);
            Assert.AreEqual(1, r3.hits.Count, "第 3 根独立命中（不因第 1 根去重而吞掉）");
            Assert.AreEqual(2, r3.nextState.enemies.Find(e => e.actorId == 0).hp, "hp4 → 3 → 2");

            var sword = after2.sword;
            Assert.AreEqual(4, sword.edges.Count, "两根成长后总边数 2+2");
            Assert.AreEqual(1, after2.growthCredits, "3 点用掉 2 点");
        }

        [Test]
        public void A07_IllegalPositions_AreRejectedWithoutSpending()
        {
            // 墙 / 未收集果 / 点数不足
            var walledBoard = BeatSimulatorTests.Board(new Vector2Int(8, 5));
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            var r = RunController.SimulateConfirm(state, walledBoard, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));
            Assert.IsFalse(r.valid, "候选穿墙非法");
            Assert.AreEqual(1, state.growthCredits, "非法确认不扣点");
            Assert.AreEqual(2, state.sword.edges.Count, "非法确认不加边");

            var fruitState = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            fruitState.fruits.Add(new FruitState { fruitId = "f0", cell = new Vector2Int(8, 5), consumed = false });
            var r2 = RunController.SimulateConfirm(fruitState, BeatSimulatorTests.Board(), null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));
            Assert.IsFalse(r2.valid, "候选碰未收集果非法");

            var brokeState = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial(), credits: 0);
            var r3 = RunController.SimulateConfirm(brokeState, BeatSimulatorTests.Board(), null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));
            Assert.IsFalse(r3.valid, "无成长点不能确认");
        }

        [Test]
        public void A09_GrowthHitsCore_NoKnockbackNoInterrupt_KillFlagOnDestroy()
        {
            // 初始尖端 1.3 格；核心位于前方第 2 格，首根 0.5 格成长可命中且起点未接触。
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            var core = AddCore(state, new Vector2Int(8, 5), hp: 1);

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid);
            Assert.AreEqual(1, result.hits.Count);
            var hit = result.hits[0];
            Assert.IsTrue(hit.isCore);
            Assert.AreEqual(0, hit.hpAfter, "核心 hp1 → 0");
            Assert.IsFalse(hit.movedByKnock, "核心不击退");
            Assert.IsFalse(core.interruptedThisBeat, "核心不打断");
            Assert.IsTrue(result.coreKilledByThisGrowth, "本根击毁核心应置通关标志");
        }

        [Test]
        public void A09b_CoreSurvivesGrowth_KeepsIntentNoFlag()
        {
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            AddCore(state, new Vector2Int(8, 5), hp: 3);

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid);
            Assert.IsFalse(result.coreKilledByThisGrowth);
            Assert.AreEqual(2, result.nextState.GetCore().hp, "核心受 1 伤存活");
            Assert.IsFalse(result.nextState.GetCore().interruptedThisBeat, "核心不被打断");
        }

        [Test]
        public void A10_GrowthInterruptCancelsEnemyIntent_OthersStillAct()
        {
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial());
            AddCharger(state, 0, new Vector2Int(8, 5));
            var other = AddCharger(state, 1, new Vector2Int(7, 3));
            EnemyPlanner.PlanNextIntents(state, board, cfg);
            Assert.AreEqual(IntentKind.Move, state.enemies.Find(e => e.actorId == 0).intent.kind,
                "当前短剑挡住直线路径时，应规划绕行而不是停住");

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));
            Assert.IsTrue(result.valid);

            // 被打断敌人取消本拍意图；其余敌人各按原意图响应一次（文档 4.7）
            var sim = new BeatSimulator(cfg);
            var er = sim.ResumeEnemyPhase(result.nextState, board, new ActionPresentation());
            var e0 = er.state.enemies.Find(e => e.actorId == 0);
            var e1 = er.state.enemies.Find(e => e.actorId == 1);
            Assert.IsNotNull(e0);
            Assert.AreEqual(new Vector2Int(9, 5), e0.cell, "被打断敌人保持击退后位置，不再执行意图");
            Assert.AreEqual(new Vector2Int(6, 3), e1.cell, "未打断敌人执行原意图移动");
            Assert.AreEqual(0, er.presentation.enemyEvents.FindAll(ev => ev.actorId == 0).Count, "被打断敌人无事件");
            Assert.AreEqual(1, er.presentation.enemyEvents.FindAll(ev => ev.actorId == 1).Count);
        }

        // ---- 账本（文档 4.3）----

        [Test]
        public void Ledger_ConfirmAtomicChange_EdgesCountCreditsVersion()
        {
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, SwordState.CreateInitial(), credits: 2);
            state.fruits.Add(new FruitState { fruitId = "f0", cell = new Vector2Int(3, 8), consumed = true });
            state.fruits.Add(new FruitState { fruitId = "f1", cell = new Vector2Int(3, 9), consumed = true });
            int versionBefore = state.swordVersion;

            var result = RunController.SimulateConfirm(state, board, null, cfg, 1,
                Cand(new Vector2Int(3, 0), new Vector2Int(4, 0)));

            Assert.IsTrue(result.valid);
            var next = result.nextState;
            Assert.AreEqual(3, next.sword.edges.Count, "edges = 2 + growthCount");
            Assert.AreEqual(1, next.sword.growthCount);
            Assert.AreEqual(1, next.growthCredits);
            Assert.AreEqual(versionBefore + 1, next.swordVersion);
            // 已消费果数 = growthCount + growthCredits：2 = 1 + 1
            int consumed = 0;
            foreach (var f in state.fruits)
                if (f.consumed) consumed++;
            Assert.AreEqual(next.sword.growthCount + next.growthCredits, consumed);
        }

        // ---- U 系列：无上限（文档 14.4）----

        private static SwordState GrowChain(int n)
        {
            var s = SwordState.CreateInitial();
            for (int i = 0; i < n; i++)
                s = GrowthOptions.WithEdge(s, Cand(new Vector2Int(3 + i, 0), new Vector2Int(4 + i, 0)).edge);
            return s;
        }

        [Test]
        public void U01_SixAndTwelveGrowths_StillProduceCandidates()
        {
            foreach (int n in new[] { 6, 12 })
            {
                var s = GrowChain(n);
                Assert.AreEqual(2 + n, s.edges.Count, $"总边数 = 2 + {n}");
                Assert.AreEqual(n, s.growthCount);
                Assert.AreEqual(1.3f + 0.5f * n, SwordGeometry.MaxReachRadius(s), 1e-4f, "直长触及 = 1.3 + 0.5n");
                Assert.Greater(GrowthOptions.GetGraphCandidates(s, cfg).Count, 0, $"第 {n} 次成长后仍可继续生长");
            }
        }

        [Test]
        public void U02_TwentyFourGrowths_CloneShapeKeyReachAndSolverStayCorrect()
        {
            // 12 直 + 6 上 + 6 下的分叉图（24 仅代表性测试样本，非上限）
            var s = GrowChain(12);
            for (int i = 0; i < 6; i++)
                s = GrowthOptions.WithEdge(s, Cand(new Vector2Int(15, i), new Vector2Int(15, i + 1)).edge);
            for (int i = 0; i < 6; i++)
                s = GrowthOptions.WithEdge(s, Cand(new Vector2Int(12, -i), new Vector2Int(12, -i - 1)).edge);

            Assert.AreEqual(26, s.edges.Count);
            var clone = s.Clone();
            Assert.AreEqual(SwordGeometry.ShapeKey(s), SwordGeometry.ShapeKey(clone), "克隆与形状键稳定");
            Assert.AreEqual(24, s.growthCount);
            // 按真实图计算触及：上枝端点 (7.3,3.0)，不再用 4.0 裁剪（文档 5.6）
            Assert.AreEqual(Mathf.Sqrt(7.3f * 7.3f + 3f * 3f), SwordGeometry.MaxReachRadius(s), 1e-4f);
            Assert.Greater(SwordGeometry.MaxEndpointRadius(s), 4.0f, "触及超过旧 4.0 限制仍正确计算");

            // 26 边剑走一遍求解器：动态容量不越界、确定性不抛异常
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(6, 5), 0, s);
            var solver = new TrajectorySolver(cfg);
            var wall = solver.FindFirstWallContact(state, board, PlayerAction.Rotate(1));
            Assert.IsTrue(wall.hasContact, "大剑旋转应碰到边界墙");
            Assert.GreaterOrEqual(wall.u, 0f);
            Assert.LessOrEqual(wall.u, 1f);
            var hits = solver.FindEnemyContacts(state, PlayerAction.Rotate(1), new List<HitCandidate>());
            Assert.AreEqual(0, hits.Count, "空房间无敌人命中");
            Assert.IsFalse(solver.BodyPathHitsWall(new Vector2Int(6, 5), new Vector2Int(5, 5), board));
        }

        [Test]
        public void U02b_LongSwordRotate_FindsWallWithStableProgress()
        {
            // 玩家 (2,5) 直线 12 节长剑朝东（触及 7.5）：顺时针扫向南墙 y=0（距轴心 5.5），
            // 首次接触角 asin(5.5/7.5) ≈ 47.2° → u ≈ 0.527，由最远端段（索引 13）率先接触
            var board = BeatSimulatorTests.Board();
            var state = State(new Vector2Int(2, 5), 0, GrowChain(12));
            var solver = new TrajectorySolver(cfg);
            var wall = solver.FindFirstWallContact(state, board, PlayerAction.Rotate(-1));
            Assert.IsTrue(wall.hasContact, "长剑旋转应碰到边界墙");
            Assert.Greater(wall.u, 0.3f);
            Assert.Less(wall.u, 0.65f);
            Assert.AreEqual(13, wall.segmentIndex, "首次接触在最远端段");
        }
    }
}
