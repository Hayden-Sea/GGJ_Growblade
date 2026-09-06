using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    [Serializable]
    public struct SpawnCandidate
    {
        public Vector2Int cell;
        public int facing;
    }

    [Serializable]
    public struct EnemySpawnDef
    {
        public Vector2Int cell;
        public EnemyKind kind;
    }

    /// <summary>房间数据：墙格、出生候选、敌人配置、出口（文档 9.2/10.8）。</summary>
    [CreateAssetMenu(fileName = "ROOM_New", menuName = "SwordGame/Room Definition")]
    public sealed class RoomDefinition : ScriptableObject
    {
        public string roomId = "Room";
        [TextArea] public string goalText = "清理敌人";
        [Tooltip("本关玩家初始生命；0 表示兼容旧关卡并使用 GameConfig 默认值")]
        [Min(0)] public int initialPlayerHp;
        public int width = 13;
        public int height = 11;
        public List<Vector2Int> wallCells = new List<Vector2Int>();

        [Tooltip("按顺序尝试的出生候选，进入房间前用实际剑形验证")]
        public List<SpawnCandidate> playerSpawns = new List<SpawnCandidate>();
        public List<EnemySpawnDef> enemySpawns = new List<EnemySpawnDef>();
        [Tooltip("可被剑击退、不会自行行动的箱子")]
        public List<Vector2Int> boxSpawns = new List<Vector2Int>();
        [Tooltip("箱子全部压住这些机关后显示并点亮出口")]
        public List<Vector2Int> pressurePlateCells = new List<Vector2Int>();

        public bool hasCore;
        public Vector2Int coreCell;

        [Tooltip("普通房的出口格；核心房可为任意格（胜利条件为摧毁核心）")]
        public Vector2Int exitCell;

        [Tooltip("旧奖励已停用（v1.3），仅保留序列化字段用于回溯")]
        public bool hasReward;
        public Vector2Int rewardCell;

        // ---- v1.3 独立关字段（文档 10.6）----

        public enum LevelObjective
        {
            ClearEnemiesAndExit = 0,
            // 仅用于读取旧资产；EffectiveObjective 会把它迁移为 ClearEnemiesAndExit。
            DestroyCore = 1,
            PushBoxesToPlatesAndExit = 2,
            ClearEnemiesAndPlatesAndExit = 3,
            ReachExit = 4,
        }

        [Serializable]
        public struct FruitSpawnDef
        {
            public string fruitId;    // 本资产内唯一且稳定
            public Vector2Int cell;
            [Min(1)] public int growthValue; // 1 = 普通果；2..9 = 必须连续完成的多生长果
        }

        [Tooltip("0 = 旧资产（按 hasCore 解释目标）；2 = 已迁移")]
        [SerializeField] private int schemaVersion;

        public LevelObjective objective = LevelObjective.ReachExit;
        public List<FruitSpawnDef> fruitSpawns = new List<FruitSpawnDef>();

        public int SchemaVersion => schemaVersion;

        public void MarkMigrated() => schemaVersion = 2;

        /// <summary>兼容期唯一读取入口：旧资产按 hasCore 解释目标类型。</summary>
        public LevelObjective EffectiveObjective
        {
            get
            {
                var resolved = schemaVersion >= 2
                    ? objective
                    : (hasCore ? LevelObjective.DestroyCore : LevelObjective.ClearEnemiesAndExit);
                return resolved == LevelObjective.DestroyCore ? LevelObjective.ClearEnemiesAndExit : resolved;
            }
        }

        public bool RequiresEnemyClear => EffectiveObjective == LevelObjective.ClearEnemiesAndExit ||
                                          EffectiveObjective == LevelObjective.ClearEnemiesAndPlatesAndExit;
        public bool RequiresPlates => EffectiveObjective == LevelObjective.PushBoxesToPlatesAndExit ||
                                      EffectiveObjective == LevelObjective.ClearEnemiesAndPlatesAndExit;
        public bool IsExitOpenByDefault => EffectiveObjective == LevelObjective.ReachExit;

        public int ResolveInitialPlayerHp(GameConfig cfg)
        {
            if (initialPlayerHp > 0)
                return initialPlayerHp;
            return Mathf.Max(1, cfg != null ? cfg.playerHp : 4);
        }

        public BoardModel BuildBoard()
        {
            return new BoardModel(width, height, wallCells);
        }
    }

    [Serializable]
    public struct RunStage
    {
        public RoomDefinition room;
    }
}
