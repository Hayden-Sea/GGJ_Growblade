using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    public class BoxMechanicTests
    {
        private GameConfig cfg;

        [SetUp] public void SetUp() => cfg = ScriptableObject.CreateInstance<GameConfig>();
        [TearDown] public void TearDown() { if (cfg != null) Object.DestroyImmediate(cfg); }

        private static GameState State(Vector2Int boxCell)
        {
            var s = new GameState
            {
                sessionId = 1, player = new PlayerState { cell = new Vector2Int(6, 5), facing = 0, hp = 4 },
                sword = SwordState.CreateInitial(), enemies = new List<EnemyState>()
            };
            s.enemies.Add(new EnemyState { actorId = 200, kind = EnemyKind.Box, cell = boxCell, hp = 999, intent = EnemyIntent.Wait(false) });
            return s;
        }

        [Test]
        public void ForwardSwordContact_PushesBoxWithoutDamageOrSelfMovement()
        {
            var sim = new BeatSimulator(cfg);
            var pr = sim.BeginPlayerAction(State(new Vector2Int(8, 5)), PlayerAction.Move(Vector2Int.right), BeatSimulatorTests.Board());
            Assert.IsTrue(pr.isValid);
            var box = pr.state.enemies.Find(e => e.kind == EnemyKind.Box);
            Assert.AreEqual(new Vector2Int(9, 5), box.cell);
            Assert.AreEqual(999, box.hp);
            var er = sim.ResumeEnemyPhase(pr.state, BeatSimulatorTests.Board(), pr.presentation);
            Assert.AreEqual(new Vector2Int(9, 5), er.state.enemies.Find(e => e.kind == EnemyKind.Box).cell, "箱子不会自行行动");
        }

        [Test]
        public void RotatingSword_PushesBoxAndRebounds()
        {
            var pr = new BeatSimulator(cfg).BeginPlayerAction(State(new Vector2Int(6, 6)), PlayerAction.Rotate(1), BeatSimulatorTests.Board());
            Assert.IsTrue(pr.isValid);
            Assert.IsTrue(pr.presentation.rebound);
            Assert.AreEqual(StopReason.Enemy, pr.presentation.stopReason);
            Assert.AreEqual(0, pr.state.player.facing);
            Assert.AreEqual(999, pr.state.enemies.Find(e => e.kind == EnemyKind.Box).hp);
        }

        [Test]
        public void BoxBlockedByWall_StaysAndRemainsIndestructible()
        {
            var state = State(new Vector2Int(8, 5));
            var pr = new BeatSimulator(cfg).BeginPlayerAction(state, PlayerAction.Move(Vector2Int.right),
                BeatSimulatorTests.Board(new Vector2Int(9, 5)));
            var box = pr.state.enemies.Find(e => e.kind == EnemyKind.Box);
            Assert.AreEqual(new Vector2Int(8, 5), box.cell);
            Assert.AreEqual(999, box.hp);
            Assert.IsTrue(pr.presentation.hits[0].wallImpact);
            Assert.IsFalse(pr.presentation.hits[0].died);
        }

        [Test]
        public void BoxObjective_RevealsExitOnlyWhenEveryPlateIsOccupied()
        {
            var room = ScriptableObject.CreateInstance<RoomDefinition>();
            room.objective = RoomDefinition.LevelObjective.PushBoxesToPlatesAndExit;
            room.MarkMigrated();
            room.pressurePlateCells.Add(new Vector2Int(9, 5));
            var state = State(new Vector2Int(8, 5));
            Assert.IsFalse(RunController.IsExitObjectiveCleared(room, state));
            state.enemies.Find(e => e.kind == EnemyKind.Box).cell = new Vector2Int(9, 5);
            Assert.IsTrue(RunController.IsExitObjectiveCleared(room, state));
            Object.DestroyImmediate(room);
        }
    }
}
