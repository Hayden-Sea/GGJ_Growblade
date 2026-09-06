using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>关卡目标与“房间已清理”状态的边界测试。</summary>
    public class RoomObjectiveTests
    {
        private RoomDefinition MakeRoom(RoomDefinition.LevelObjective objective)
        {
            var room = ScriptableObject.CreateInstance<RoomDefinition>();
            room.objective = objective;
            room.MarkMigrated();
            return room;
        }

        private static GameState StateWithoutNormalEnemies()
        {
            return new GameState
            {
                player = new PlayerState { hp = 4 },
                sword = SwordState.CreateInitial(),
            };
        }

        [Test]
        public void LegacyDestroyCore_IsMergedIntoClearEnemiesAndRequiresCoreAndGuards()
        {
            var room = MakeRoom(RoomDefinition.LevelObjective.DestroyCore);
            var state = StateWithoutNormalEnemies();
            state.enemies.Add(new EnemyState { actorId = 100, kind = EnemyKind.Core, cell = new Vector2Int(6, 5), hp = 3 });

            Assert.AreEqual(RoomDefinition.LevelObjective.ClearEnemiesAndExit, room.EffectiveObjective);
            Assert.IsFalse(RunController.IsExitObjectiveCleared(room, state));
            state.enemies.Add(new EnemyState { actorId = 1, kind = EnemyKind.Charger, cell = new Vector2Int(5, 5), hp = 1 });
            state.enemies.RemoveAll(e => e.kind == EnemyKind.Core);
            Assert.IsFalse(RunController.IsExitObjectiveCleared(room, state));
            state.enemies.Clear();
            Assert.IsTrue(RunController.IsExitObjectiveCleared(room, state));

            Object.DestroyImmediate(room);
        }

        [Test]
        public void CombinedObjective_RequiresEnemiesAndPlatesTogether()
        {
            var room = MakeRoom(RoomDefinition.LevelObjective.ClearEnemiesAndPlatesAndExit);
            room.pressurePlateCells.Add(new Vector2Int(4, 4));
            var state = StateWithoutNormalEnemies();
            state.enemies.Add(new EnemyState { actorId = 1, kind = EnemyKind.Charger, cell = new Vector2Int(3, 3), hp = 1 });
            state.enemies.Add(new EnemyState { actorId = 200, kind = EnemyKind.Box, cell = new Vector2Int(4, 4), hp = 999 });
            Assert.IsFalse(RunController.IsExitObjectiveCleared(room, state), "机关已开但仍有敌人");
            state.enemies.RemoveAll(e => e.kind == EnemyKind.Charger);
            Assert.IsTrue(RunController.IsExitObjectiveCleared(room, state));
            state.enemies.Find(e => e.kind == EnemyKind.Box).cell = new Vector2Int(5, 4);
            Assert.IsFalse(RunController.IsExitObjectiveCleared(room, state), "敌人已清但机关未开");
            Object.DestroyImmediate(room);
        }

        [Test]
        public void ClearEnemiesAndExit_ClearingGuards_MarksRoomCleared()
        {
            var room = MakeRoom(RoomDefinition.LevelObjective.ClearEnemiesAndExit);

            Assert.IsTrue(RunController.IsExitObjectiveCleared(room, StateWithoutNormalEnemies()));

            Object.DestroyImmediate(room);
        }

        [Test]
        public void ReachExit_IsOpenImmediatelyWithoutEnemiesOrPlates()
        {
            var room = MakeRoom(RoomDefinition.LevelObjective.ReachExit);
            var state = StateWithoutNormalEnemies();
            state.enemies.Add(new EnemyState { actorId = 1, kind = EnemyKind.Charger, cell = new Vector2Int(3, 3), hp = 1 });

            Assert.IsTrue(room.IsExitOpenByDefault);
            Assert.IsTrue(RunController.IsExitObjectiveCleared(room, state), "直接离开不依赖敌人或机关状态");

            Object.DestroyImmediate(room);
        }

        [Test]
        public void RoomInitialPlayerHp_UsesRoomValueAndLegacyFallback()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfig>();
            cfg.playerHp = 4;
            var room = ScriptableObject.CreateInstance<RoomDefinition>();

            Assert.AreEqual(4, room.ResolveInitialPlayerHp(cfg), "旧房间应继续使用全局默认生命");
            room.initialPlayerHp = 6;
            Assert.AreEqual(6, room.ResolveInitialPlayerHp(cfg), "作者设置后应使用本关独立生命");

            Object.DestroyImmediate(room);
            Object.DestroyImmediate(cfg);
        }
    }
}
