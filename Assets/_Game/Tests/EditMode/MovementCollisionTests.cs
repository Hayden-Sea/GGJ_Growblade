using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    public class MovementCollisionTests
    {
        private GameConfig cfg;
        [SetUp] public void Setup() { cfg = ScriptableObject.CreateInstance<GameConfig>(); }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(cfg); }
        private GameState State() => new GameState {
            player = new PlayerState { cell = new Vector2Int(4, 4), facing = 0, hp = 4 },
            sword = SwordState.CreateInitial(), sessionId = 1
        };
        private EnemyState Enemy(GameState state, Vector2Int from, Vector2Int to)
        {
            var enemy = new EnemyState { actorId = 1, hp = 3, kind = EnemyKind.Charger, cell = from,
                intent = new EnemyIntent { kind = IntentKind.Move, targetCell = to, direction = to - from } };
            state.enemies.Add(enemy); return enemy;
        }
        [Test] public void PlanningAvoidsSwordAndChoosesOtherAxis()
        {
            var s = State(); var e = Enemy(s, new Vector2Int(5,3), new Vector2Int(5,4));
            e.actorId = 2; // vertical preferred, but sword occupies that route
            EnemyPlanner.PlanNextIntents(s, BeatSimulatorTests.Board(), cfg);
            Assert.AreEqual(new Vector2Int(4,3), e.intent.targetCell);
        }
        [Test] public void ExecutionRechecksStaleIntentAgainstCurrentSword()
        {
            var s = State(); var e = Enemy(s, new Vector2Int(5,3), new Vector2Int(5,4));
            var p = new ActionPresentation();
            EnemyPlanner.ExecuteIntents(s, BeatSimulatorTests.Board(), cfg, p);
            Assert.AreEqual(new Vector2Int(5,3), e.cell);
            Assert.AreEqual(EnemyEventKind.Blocked, p.enemyEvents[0].kind);
            Assert.AreEqual(3,e.hp);
        }
        [Test] public void ChargerTakesShortestDetourAroundFrontOfSword()
        {
            var s = State(); var e = Enemy(s, new Vector2Int(6,4), new Vector2Int(5,4));
            e.actorId = 2;
            var board = BeatSimulatorTests.Board();
            var visited = new System.Collections.Generic.List<Vector2Int> { e.cell };
            for (int turn = 0; turn < 4; turn++)
            {
                EnemyPlanner.PlanNextIntents(s, board, cfg);
                Assert.AreEqual(IntentKind.Move, e.intent.kind, "可绕行时不应等待");
                Assert.IsFalse(MovementCollision.EnemyPathHitsSword(e, e.intent.targetCell, s, cfg));
                var p = new ActionPresentation();
                EnemyPlanner.ExecuteIntents(s, board, cfg, p);
                visited.Add(e.cell);
            }
            Assert.AreEqual(s.player.cell, e.intent.targetCell, "第四步应从剑侧面攻击玩家");
            Assert.AreEqual(new Vector2Int(4,5), e.cell, "攻击不占据玩家格");
            Assert.AreEqual(3, s.player.hp);
            Assert.AreEqual(new Vector2Int(6,5), visited[1], "偶数敌人对等最短路固定从上侧绕行");
        }

        [Test] public void ChargerWaitsOnlyWhenNoRouteExists()
        {
            var s = State(); var e = Enemy(s, new Vector2Int(8,8), new Vector2Int(8,7));
            var board = BeatSimulatorTests.Board(new Vector2Int(8,7), new Vector2Int(7,8), new Vector2Int(9,8), new Vector2Int(8,9));
            EnemyPlanner.PlanNextIntents(s, board, cfg);
            Assert.AreEqual(IntentKind.Wait, e.intent.kind);
        }
        [Test] public void SweepCannotCrossHalfGridBranchEvenWhenBothEndsAreClear()
        {
            var s = State();
            s.sword.edges.Add(BladeSegment.Canonical(new Vector2Int(13,0),new Vector2Int(13,5)));
            s.sword.edges.Add(BladeSegment.Canonical(new Vector2Int(13,5),new Vector2Int(13,10)));
            var e=Enemy(s,new Vector2Int(5,5),new Vector2Int(6,5));
            Assert.IsTrue(MovementCollision.EnemyPathHitsSword(e,e.intent.targetCell,s,cfg));
        }
        [Test] public void FacingTransformsSwordCollision()
        {
            var s=State(); s.player.facing=1;
            var e=Enemy(s,new Vector2Int(5,5),new Vector2Int(4,5));
            Assert.IsTrue(MovementCollision.EnemyPathHitsSword(e,e.intent.targetCell,s,cfg));
        }
        [Test] public void ClearRearAttackStillHurtsPlayer()
        {
            var s=State(); Enemy(s,new Vector2Int(3,4),s.player.cell);
            var p=new ActionPresentation();
            EnemyPlanner.ExecuteIntents(s,BeatSimulatorTests.Board(),cfg,p);
            Assert.AreEqual(4-cfg.enemyDamage,s.player.hp);
            Assert.AreEqual(EnemyEventKind.AttackMove,p.enemyEvents[0].kind);
        }
        [Test] public void BlockedBodyMoveStopsAtWallSurfaceAndCommitsReboundBeat()
        {
            var s=State(); var board=BeatSimulatorTests.Board(new Vector2Int(4,3));
            var action=PlayerAction.Move(Vector2Int.down);
            var result=new BeatSimulator(cfg).BeginPlayerAction(s,action,board);
            Assert.IsTrue(result.isValid);
            float u=MovementCollision.BlockedMoveProgress(s,action,board,cfg);
            Assert.That(u,Is.InRange(.19f,.23f));
            Assert.AreEqual(new Vector2Int(4,4),s.player.cell);
            Assert.AreEqual(0,s.beatIndex, "输入快照不变");
            Assert.AreEqual(1,result.state.beatIndex, "返回状态提交一拍");
            Assert.IsTrue(result.presentation.rebound);
            Assert.AreEqual(0,result.presentation.hits.Count);
        }
        [Test] public void BladeWallStopsEarlierThanBody()
        {
            var s=State(); var board=BeatSimulatorTests.Board(new Vector2Int(6,4));
            var action=PlayerAction.Move(Vector2Int.right);
            // 1.3 格初始尖端与墙仍有少量空隙，前探后应由剑先于身体撞墙。
            Assert.That(MovementCollision.BlockedMoveProgress(s,action,board,cfg),Is.InRange(.08f,.12f));
        }
        [Test] public void ShortenedInitialSword_CanMoveDownBesideOffsetWall()
        {
            // 对应人工截图：墙在角色右下方，墙左边缘距轴心 1.5 格。
            // 1.4 中心线 + 0.1 半径会贴墙；1.3 中心线保留余量，应允许完整向下移动。
            var s=State();
            var board=BeatSimulatorTests.Board(new Vector2Int(6,3));
            var result=new BeatSimulator(cfg).BeginPlayerAction(s,PlayerAction.Move(Vector2Int.down),board);
            Assert.IsTrue(result.isValid);
            Assert.AreEqual(new Vector2Int(4,3),result.state.player.cell);
        }
        [Test] public void OccupiedTargetStopsAtActorSurface()
        {
            var s=State(); Enemy(s,new Vector2Int(3,4),new Vector2Int(3,4));
            float u=MovementCollision.BlockedMoveProgress(s,PlayerAction.Move(Vector2Int.left),BeatSimulatorTests.Board(),cfg);
            Assert.That(u,Is.InRange(.39f,.43f));
        }
    }
}
