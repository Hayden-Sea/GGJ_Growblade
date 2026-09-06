using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>弩手三拍节奏：预备、射击、冷却，避免每拍连续发射。</summary>
    public class ArcherCadenceTests
    {
        private GameConfig cfg;

        [SetUp]
        public void SetUp() => cfg = ScriptableObject.CreateInstance<GameConfig>();

        [TearDown]
        public void TearDown()
        {
            if (cfg != null)
                Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Archer_UsesAimShootCooldownCadence()
        {
            var state = new GameState
            {
                player = new PlayerState { cell = new Vector2Int(8, 5), facing = 0, hp = 4 },
                sword = SwordState.CreateInitial(),
                enemies = new List<EnemyState>
                {
                    new EnemyState { actorId = 7, kind = EnemyKind.Archer, cell = new Vector2Int(2, 5), hp = 2 },
                },
            };
            var archer = state.enemies[0];
            var board = BeatSimulatorTests.Board();

            EnemyPlanner.PlanNextIntents(state, board, cfg);
            Assert.AreEqual(IntentKind.Aim, archer.intent.kind, "第一拍预备");
            var aim = new ActionPresentation();
            EnemyPlanner.ExecuteIntents(state, board, cfg, aim);
            Assert.AreEqual(EnemyEventKind.Aim, aim.enemyEvents[0].kind);
            Assert.AreEqual(4, state.player.hp);

            EnemyPlanner.PlanNextIntents(state, board, cfg);
            Assert.AreEqual(IntentKind.ShootLine, archer.intent.kind, "第二拍射击");
            var shot = new ActionPresentation();
            EnemyPlanner.ExecuteIntents(state, board, cfg, shot);
            Assert.AreEqual(EnemyEventKind.ShootLine, shot.enemyEvents[0].kind);
            Assert.AreEqual(3, state.player.hp, "射击只造成一次伤害");

            EnemyPlanner.PlanNextIntents(state, board, cfg);
            Assert.AreEqual(IntentKind.Cooldown, archer.intent.kind, "射击后必须冷却一拍");
            var cooldown = new ActionPresentation();
            EnemyPlanner.ExecuteIntents(state, board, cfg, cooldown);
            Assert.AreEqual(EnemyEventKind.Wait, cooldown.enemyEvents[0].kind);
            Assert.AreEqual(3, state.player.hp, "冷却拍不产生射线伤害");

            EnemyPlanner.PlanNextIntents(state, board, cfg);
            Assert.AreEqual(IntentKind.Aim, archer.intent.kind, "冷却后重新预备");
        }
    }
}
