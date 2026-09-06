using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>果、回弹与分段拍事务测试（文档 14.3 F 系列）。</summary>
    public class FruitBeatTests
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

        private static BoardModel Board(params Vector2Int[] innerWalls) => BeatSimulatorTests.Board(innerWalls);

        private static Vector2Int Node(int halfX, int halfY) => new Vector2Int(
            halfX < 3 ? halfX * 5 : 13 + (halfX - 3) * 5, halfY * 5);

        private static SwordState Sword(int tip = 3)
        {
            var s = SwordState.CreateInitial();
            for (int n = 3; n < tip; n++)
                s.edges.Add(BladeSegment.Canonical(Node(n, 0), Node(n + 1, 0)));
            return s;
        }

        private static SwordState CollisionFixtureSword(int tip)
        {
            var s = SwordState.CreateInitial();
            s.edges[1] = BladeSegment.Canonical(new Vector2Int(10, 0), new Vector2Int(15, 0));
            for (int n = 3; n < tip; n++)
                s.edges.Add(BladeSegment.Canonical(new Vector2Int(n * 5, 0), new Vector2Int((n + 1) * 5, 0)));
            return s;
        }

        private static GameState State(Vector2Int playerCell, int facing, int tip, params Vector2Int[] fruitCells)
        {
            var state = new GameState
            {
                sessionId = 1,
                roomIndex = 0,
                beatIndex = 0,
                player = new PlayerState { cell = playerCell, facing = facing, hp = 4 },
                sword = Sword(tip),
                enemies = new List<EnemyState>(),
            };
            int i = 0;
            foreach (var c in fruitCells)
                state.fruits.Add(new FruitState { fruitId = "f" + i++, cell = c });
            return state;
        }

        [Test]
        public void F01_BodyPickup_OpensGrowth_ConsumesOnce()
        {
            // 剑朝北不接触果；身体向东合法移动到果格 → 身体拾取（若剑先碰果则应为 SwordHit 截断）
            var board = Board();
            var state = State(new Vector2Int(6, 5), 1, 3, new Vector2Int(7, 5));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Move(Vector2Int.right), board);

            Assert.IsTrue(pr.isValid);
            Assert.IsTrue(pr.hasGrowthRequest, "身体到达果格应触发成长");
            Assert.AreEqual(GrowthSource.BodyPickup, pr.growthRequest.source);
            Assert.AreEqual(1, pr.state.growthCredits);
            Assert.IsTrue(pr.state.fruits[0].consumed);
            Assert.AreEqual(new Vector2Int(7, 5), pr.state.player.cell, "身体拾果后应停在目标果格");
            Assert.IsTrue(pr.presentation.bodyPickup);
            Assert.AreEqual(StopReason.None, pr.presentation.stopReason, "身体拾果不应作为剑碰果截断");
            Assert.IsFalse(pr.presentation.rebound, "身体拾果不应回弹");

            // 敌人等确认 / 暂存后行动：恢复阶段才执行
            var er = sim.ResumeEnemyPhase(pr.state, board, pr.presentation);
            Assert.AreEqual(1, er.state.beatIndex);
        }

        [Test]
        public void F02_RotateHitsFruit_TruncatesAndRebounds()
        {
            // 轴心 (6.5,5.5) 朝东，果 (6,6) 在正北：逆时针扫至 u≈0.74 首次接触
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, 3, new Vector2Int(6, 6));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(pr.isValid);
            Assert.AreEqual(StopReason.Fruit, pr.presentation.stopReason);
            Assert.Greater(pr.presentation.stopU, 0.5f);
            Assert.Less(pr.presentation.stopU, 0.9f);
            Assert.IsTrue(pr.presentation.rebound);
            Assert.AreEqual(0, pr.state.player.facing, "旋转砍果回到原朝向");
            Assert.AreEqual(new Vector2Int(6, 5), pr.state.player.cell);
            Assert.AreEqual(1, pr.state.growthCredits);
            Assert.IsTrue(pr.state.fruits[0].consumed);
            Assert.IsTrue(pr.hasGrowthRequest);
            Assert.AreEqual(GrowthSource.SwordHit, pr.growthRequest.source);
        }

        [Test]
        public void F03_MoveSwordHitsFruit_ReturnsToStartCell()
        {
            // 玩家 (6,5) tip4 朝东：果 (9,5) 在出程 u≈0.6 处被剑砍到
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, 4, new Vector2Int(9, 5));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Move(Vector2Int.right), board);

            Assert.IsTrue(pr.isValid);
            Assert.AreEqual(StopReason.Fruit, pr.presentation.stopReason);
            Assert.Greater(pr.presentation.stopU, 0.3f);
            Assert.Less(pr.presentation.stopU, 0.9f);
            Assert.AreEqual(new Vector2Int(6, 5), pr.state.player.cell, "平移砍果整体回到出发格");
            Assert.AreEqual(1, pr.state.growthCredits);
            Assert.IsTrue(pr.state.fruits[0].consumed);
            Assert.IsFalse(pr.presentation.bodyPickup);
            Assert.IsTrue(pr.hasGrowthRequest);
        }

        [Test]
        public void F04a_FruitBeforeWall_FruitWins()
        {
            // 玩家 (4,5) tip4 逆时针：果 (6,6) u≈0.34 先于柱 (5,3) 的墙接触 u≈0.49
            var board = Board(new Vector2Int(5, 3));
            var state = State(new Vector2Int(4, 5), 0, 4, new Vector2Int(6, 6));
            state.sword = CollisionFixtureSword(4);

            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(1), board);

            Assert.AreEqual(StopReason.Fruit, pr.presentation.stopReason, "果先于墙应砍果回弹");
            Assert.AreEqual(1, pr.state.growthCredits);
            Assert.IsTrue(pr.state.fruits[0].consumed);
        }

        [Test]
        public void F04b_WallBeforeFruit_WallWinsFruitKept()
        {
            // 柱 (5,3) 在 u≈0.49 截断；果 (3,4) 在扫掠路径之外靠后位置：墙优先，果保留
            var board = Board(new Vector2Int(5, 3));
            var state = State(new Vector2Int(4, 5), 0, 4, new Vector2Int(3, 4));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(-1), board);

            Assert.AreEqual(StopReason.Wall, pr.presentation.stopReason, "墙先接触应截断且不取果");
            Assert.AreEqual(0, pr.state.growthCredits);
            Assert.IsFalse(pr.state.fruits[0].consumed, "误差内墙优先：果保留");
            Assert.AreEqual(0, pr.state.player.facing, "墙回弹朝向不变");
        }

        [Test]
        public void F06_EnemyFruitEnemy_OnlyEnemyBeforeFruitHit()
        {
            // 玩家 (6,5) tip6 向东：敌人 (8,5) u≈0.1 受击；果 (10,5) u≈0.6 截断；敌人 (11,5) 不受击
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, 6, new Vector2Int(10, 5));
            var e1 = new EnemyState { actorId = 0, kind = EnemyKind.Charger, cell = new Vector2Int(8, 5), hp = 2 };
            var e2 = new EnemyState { actorId = 1, kind = EnemyKind.Charger, cell = new Vector2Int(11, 5), hp = 2 };
            state.enemies.Add(e1);
            state.enemies.Add(e2);

            var pr = sim.BeginPlayerAction(state, PlayerAction.Move(Vector2Int.right), board);

            Assert.IsTrue(pr.isValid);
            Assert.AreEqual(StopReason.Fruit, pr.presentation.stopReason);
            Assert.AreEqual(1, HitOn(pr.presentation, 0).hpBefore >= 0 ? 1 : 0, "第一名敌人受击");
            Assert.AreEqual(1, HitOn(pr.presentation, 0).hpAfter);
            Assert.AreEqual(2, NextEnemy(pr.state, 1).hp, "果后的第二名敌人不受击");
            Assert.AreEqual(1, pr.state.growthCredits);
            Assert.IsTrue(pr.hasGrowthRequest);
        }

        [Test]
        public void F07_FullPathHitsWall_FruitNotConsumed()
        {
            // 完整平移路径撞墙仍整次无效：即使墙前有果（文档 4.2）
            var board = Board(new Vector2Int(11, 5));
            var state = State(new Vector2Int(9, 5), 0, 3, new Vector2Int(10, 5));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Move(Vector2Int.right), board);

            Assert.IsTrue(pr.isValid, "完整平移撞墙应回弹并提交一拍");
            Assert.IsFalse(pr.hasGrowthRequest, "撞墙回弹不取果");
            Assert.AreEqual(0, pr.state.growthCredits);
            Assert.IsFalse(pr.state.fruits[0].consumed);
            Assert.AreEqual(1, pr.state.beatIndex, "消耗 1 拍");
        }

        [Test]
        public void F08_AfterFruitRebound_WaitingConsumesNothing()
        {
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, 3, new Vector2Int(6, 6));

            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(1), board);
            Assert.IsTrue(pr.hasGrowthRequest);

            // 砍果回弹后等待：静止剑无伤害、无自动吃果、无新成长请求
            var er = sim.ResumeEnemyPhase(pr.state, board, pr.presentation);
            Assert.AreEqual(0, er.presentation.hits.Count);
            Assert.AreEqual(1, er.state.growthCredits, "不重复加点");
            Assert.AreEqual(1, er.state.fruits.Count(f => f.consumed), "无二次取果");
        }

        [Test]
        public void F11_CoreBeforeFruit_ReboundsAtCoreAndKeepsFruit()
        {
            // 逆时针：核心 (5,5) 在 u≈0.27 被打碎（hp1），果 (5,6) 在 u≈0.62 被砍到：先回弹+成长，再通关
            var board = Board();
            var state = State(new Vector2Int(4, 4), 0, 4, new Vector2Int(5, 6));
            var core = new EnemyState { actorId = 100, kind = EnemyKind.Core, cell = new Vector2Int(5, 5), hp = 1 };
            state.enemies.Add(core);

            var pr = sim.BeginPlayerAction(state, PlayerAction.Rotate(1), board);

            Assert.IsTrue(pr.isValid);
            Assert.AreEqual(StopReason.Enemy, pr.presentation.stopReason, "剑先碰核心时应按怪物碰撞规则立即回弹");
            Assert.IsFalse(pr.state.fruits.First(f => f.fruitId == "f0").consumed, "核心后的果不应穿透拾取");
            Assert.AreEqual(0, NextEnemy(pr.state, 100).hp, "核心同拍被击毁");
            Assert.IsFalse(pr.hasGrowthRequest, "未砍到果时不打开成长");
            Assert.IsTrue(BeatSimulator.CoreObjectiveCompleted(pr.state), "核心已被击毁");
        }

        [Test]
        public void F12_GrowthCandidateOverlappingFruit_IsRejected()
        {
            var board = Board();
            var state = State(new Vector2Int(6, 5), 0, 4, new Vector2Int(8, 5));
            var coreCell = (Vector2Int?)null;

            // 伸长候选 (3,0)-(4,0) → 世界 (8.0,5.5)-(9.0,5.5)：与果 (8,5) 圆重叠 → 拒绝
            var edge = BladeSegment.Canonical(Node(3, 0), Node(4, 0));
            var (ok, reason) = GrowthPlacement.Filter(state.sword, edge, new Vector2Int(6, 5), 0, board, state, coreCell, cfg);
            Assert.IsFalse(ok, "新增短节不得与未收集果重叠：" + reason);

            // 无果对照：同一候选可放置
            var empty = State(new Vector2Int(6, 5), 0, 4);
            var (ok2, reason2) = GrowthPlacement.Filter(empty.sword, edge, new Vector2Int(6, 5), 0, board, empty, coreCell, cfg);
            Assert.IsTrue(ok2, "无果时可放置：" + reason2);
        }

        private static EnemyState NextEnemy(GameState state, int actorId)
        {
            foreach (var e in state.enemies)
                if (e.actorId == actorId)
                    return e;
            return null;
        }

        private static HitEvent HitOn(ActionPresentation p, int actorId)
        {
            foreach (var h in p.hits)
                if (h.actorId == actorId)
                    return h;
            return default;
        }
    }
}
