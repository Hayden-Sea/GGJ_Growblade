using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public enum PlayerActionKind { Move, Rotate, Wait }

    [Serializable]
    public struct PlayerAction
    {
        public PlayerActionKind kind;
        public Vector2Int moveDirection;
        public int quarterTurns; // Q = +1（逆时针）, E = -1（顺时针）

        public static PlayerAction Move(Vector2Int dir) => new PlayerAction { kind = PlayerActionKind.Move, moveDirection = dir };
        public static PlayerAction Rotate(int quarterTurns) => new PlayerAction { kind = PlayerActionKind.Rotate, quarterTurns = quarterTurns };
        public static PlayerAction Wait() => new PlayerAction { kind = PlayerActionKind.Wait };
    }

    public enum ActionInvalidReason { None, BodyHitsWall, BladeHitsWall, TargetOccupied, OutsideBoard }

    public enum EnemyKind { Charger, Archer, Core, Box }

    public enum IntentKind { Wait, Move, Melee, Aim, ShootLine, CorePulse, Cooldown }

    [Serializable]
    public struct EnemyIntent
    {
        public IntentKind kind;
        public Vector2Int targetCell;
        public Vector2Int direction;
        public bool interruptible;

        public static EnemyIntent Wait(bool interruptible = true) =>
            new EnemyIntent { kind = IntentKind.Wait, interruptible = interruptible };
    }

    public enum Outcome { Continue, Victory, Defeat }

    public enum EnemyEventKind { Wait, Move, AttackMove, Blocked, ShootLine, CorePulse, Aim }

    [Serializable]
    public struct EnemyEvent
    {
        public int actorId;
        public EnemyEventKind kind;
        public Vector2Int fromCell;
        public Vector2Int toCell;
        public Vector2Int direction;
        public bool playerDamaged;
        public List<Vector2Int> rayCells; // 射线/脉冲表现用
    }

    [Serializable]
    public struct HitEvent
    {
        public int actorId;
        public EnemyKind kind;
        public float u;
        public int segmentIndex;
        public Vector2 contactPoint;
        public int hpBefore;
        public int hpAfter;
        public Vector2Int knockFrom;
        public Vector2Int knockTo; // 未移动时等于 knockFrom
        public bool movedByKnock;
        public bool wallImpact;
        public bool died;
        public bool isCore;
    }

    public enum StopReason { None, Wall, Fruit, Enemy }

    /// <summary>表现事件契约（文档 7.1）：动画只消费这份结果。</summary>
    [Serializable]
    public sealed class ActionPresentation
    {
        public PlayerAction action;
        public bool isValid;
        public ActionInvalidReason invalidReason;

        public Vector2Int playerFromCell;
        public Vector2Int playerToCell;
        public int startFacing;
        public int endFacing;

        public bool rotateBlocked;
        public bool isZeroSweep;
        public float wallU = 1f;
        public int wallSegmentIndex = -1;
        public Vector2Int wallCell;
        public Vector2 wallContactPoint;

        // v1.3 果截断（文档 4.4 / 7.1）
        public StopReason stopReason = StopReason.None;
        public float stopU = 1f;
        public string fruitId;
        public Vector2 fruitContactPoint;
        public bool bodyPickup;
        public bool rebound; // 剑截断回弹（墙或果）
        public float blockedMoveProgress; // 平移撞墙/占位时的出程比例；动作仍耗拍并回弹

        public readonly List<HitEvent> hits = new List<HitEvent>();
        public readonly List<EnemyEvent> enemyEvents = new List<EnemyEvent>();
        public readonly List<EnemyIntentSnapshot> endIntents = new List<EnemyIntentSnapshot>();

        public Outcome outcome = Outcome.Continue;
        public int resultingBeatIndex;
        public int knockbackWallKills;
    }

    [Serializable]
    public struct EnemyIntentSnapshot
    {
        public int actorId;
        public EnemyKind kind;
        public Vector2Int cell;
        public EnemyIntent intent;
    }

    public sealed class BeatSimulation
    {
        public bool isValid;
        public ActionInvalidReason invalidReason;
        public GameState nextState;
        public ActionPresentation presentation;

        public static BeatSimulation Invalid(ActionInvalidReason reason)
        {
            return new BeatSimulation
            {
                isValid = false,
                invalidReason = reason,
                presentation = new ActionPresentation { isValid = false, invalidReason = reason },
            };
        }
    }

    /// <summary>规范化无向边：字典序较小端点放 aHalf，保证唯一表示。
    /// 字段名为兼容旧资产保留；坐标单位现为 0.1 格（文档 5.1）。</summary>
    [Serializable]
    public struct BladeSegment
    {
        public Vector2Int aHalf;
        public Vector2Int bHalf;

        public static BladeSegment Canonical(Vector2Int p, Vector2Int q)
        {
            if (q.x < p.x || (q.x == p.x && q.y < p.y))
                return new BladeSegment { aHalf = q, bHalf = p };
            return new BladeSegment { aHalf = p, bHalf = q };
        }

        public string Key => $"{aHalf.x},{aHalf.y}-{bHalf.x},{bHalf.y}";
    }

    /// <summary>剑 = 连接在角色前方的短节图（文档 5.1）。edges 是唯一武器数据真值。
    /// v1.4：growthCount 非负且与本关已实际使用的成长点一致，无边数上限。</summary>
    [Serializable]
    public sealed class SwordState
    {
        public List<BladeSegment> edges = new List<BladeSegment>();
        public int growthCount;          // 非负；与本关已实际使用的成长点一致

        public static SwordState CreateInitial()
        {
            var s = new SwordState();
            // 初始刃从角色前方 0.5 格开始：0.5 格根段 + 0.3 格尖段，最远端为 1.3 格。
            s.edges.Add(BladeSegment.Canonical(new Vector2Int(5, 0), new Vector2Int(10, 0)));
            s.edges.Add(BladeSegment.Canonical(new Vector2Int(10, 0), new Vector2Int(13, 0)));
            return s;
        }

        public SwordState Clone()
        {
            var s = new SwordState { growthCount = growthCount };
            s.edges.AddRange(edges);
            return s;
        }
    }

    /// <summary>果的当局状态（文档 10.3）。运行时消耗不写回 RoomDefinition。</summary>
    [Serializable]
    public sealed class FruitState
    {
        public string fruitId;
        public Vector2Int cell;
        public int growthValue = 1;
        public bool consumed;

        public int ResolvedGrowthValue => Mathf.Max(1, growthValue);

        public FruitState Clone() => new FruitState { fruitId = fruitId, cell = cell, growthValue = growthValue, consumed = consumed };
    }

    [Serializable]
    public sealed class PlayerState
    {
        public Vector2Int cell;
        public int facing; // 0..3 East/North/West/South
        public int hp;

        public PlayerState Clone() => new PlayerState { cell = cell, facing = facing, hp = hp };
    }

    [Serializable]
    public sealed class EnemyState
    {
        public int actorId;
        public EnemyKind kind;
        public Vector2Int cell;
        public int hp;
        public EnemyIntent intent;
        public bool interruptedThisBeat;

        public EnemyState Clone() => new EnemyState
        {
            actorId = actorId,
            kind = kind,
            cell = cell,
            hp = hp,
            intent = intent,
            interruptedThisBeat = interruptedThisBeat,
        };
    }

    /// <summary>当局状态快照（文档 10.5）。模型与 GameObject 分离，可克隆。</summary>
    public sealed class GameState
    {
        public int sessionId;
        public int roomIndex;
        public int beatIndex;
        public PlayerState player;
        public SwordState sword;
        public List<EnemyState> enemies = new List<EnemyState>();
        public List<FruitState> fruits = new List<FruitState>();
        public int growthCredits;   // 已收集未使用的成长点（暂存点）
        /// <summary>多生长果尚未用完的点数：存在时不可暂存，必须连续成长。</summary>
        public int mandatoryGrowthCredits;
        public int swordVersion;    // 每次确认生长 +1，候选版本绑定
        public uint runSeed;

        public GameState Clone()
        {
            var clone = new GameState
            {
                sessionId = sessionId,
                roomIndex = roomIndex,
                beatIndex = beatIndex,
                player = player.Clone(),
                sword = sword.Clone(),
                growthCredits = growthCredits,
                mandatoryGrowthCredits = mandatoryGrowthCredits,
                swordVersion = swordVersion,
                runSeed = runSeed,
            };
            foreach (var e in enemies)
                clone.enemies.Add(e.Clone());
            foreach (var f in fruits)
                clone.fruits.Add(f.Clone());
            return clone;
        }

        public EnemyState GetCore()
        {
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].kind == EnemyKind.Core)
                    return enemies[i];
            return null;
        }

        public bool HasAliveClearableEnemies()
        {
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].kind != EnemyKind.Box && enemies[i].hp > 0)
                    return true;
            return false;
        }

        public bool AreAllBoxesOn(IList<Vector2Int> plateCells)
        {
            int boxes = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].kind != EnemyKind.Box || enemies[i].hp <= 0) continue;
                boxes++;
                if (plateCells == null || !plateCells.Contains(enemies[i].cell)) return false;
            }
            return boxes > 0 && plateCells != null && boxes == plateCells.Count;
        }

        public static EnemyIntentSnapshot Snapshot(EnemyState e) =>
            new EnemyIntentSnapshot { actorId = e.actorId, kind = e.kind, cell = e.cell, intent = e.intent };
    }
}
