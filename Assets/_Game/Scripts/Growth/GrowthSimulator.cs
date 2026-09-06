using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public struct GrowthHitEvent
    {
        public int actorId;
        public EnemyKind kind;
        public float g;
        public Vector2 contactPoint;
        public Vector2Int knockFrom;
        public Vector2Int knockTo;   // 未移动时等于 knockFrom
        public bool movedByKnock;
        public bool wallImpact;
        public bool died;
        public bool isCore;
        public int hpBefore;
        public int hpAfter;
        public int growthActionId;
    }

    /// <summary>一次左键确认的独立生长事务结果（文档 10.3）：
    /// 提交只能发生一次，呈现层消费 hits 后由 TurnDirector 调 ApplyGrowth 原子生效。</summary>
    public sealed class GrowthTransactionResult
    {
        public bool valid;
        public string invalidReason;
        public int sessionId;
        public int beatId;
        public int growthActionId;
        public int sourceSwordVersion;
        public Vector2Int anchorHalf;   // 原始锚点（决定伸长与击退方向，不用规范化端点反推）
        public Vector2Int newNodeHalf;  // 原始新端点
        public string edgeKey;
        public GameState nextState;
        public List<GrowthHitEvent> hits = new List<GrowthHitEvent>();
        public bool coreKilledByThisGrowth;

        public static GrowthTransactionResult Invalid(string reason) =>
            new GrowthTransactionResult { valid = false, invalidReason = reason };
    }

    /// <summary>伸长攻击模拟（文档 v1.4 6.8）：新短节从 anchor 向 newNode 伸长，
    /// 胶囊 [A, A+(B-A)g] 单调增长；起始已碰旧剑的目标不参与本次命中。
    /// 击退方向 = 新边锚点→新端点的世界四向方向；核心受伤不击退不打断。
    /// 预览与正式执行共用本求解器。</summary>
    public static class GrowthSimulator
    {
        public static List<GrowthHitEvent> Simulate(
            GameState working, Vector2Int anchorHalf, Vector2Int newNodeHalf,
            int facing, Vector2Int playerCell, BoardModel board, GameConfig cfg, int growthActionId)
        {
            var events = new List<GrowthHitEvent>();
            var pivot = BoardModel.CellCenter(playerCell);

            Vector2 A = SwordGeometry.HalfToWorld(anchorHalf, pivot, facing);
            Vector2 B = SwordGeometry.HalfToWorld(newNodeHalf, pivot, facing);

            // 世界四向击退方向：锚点 → 新端点（文档 4.7）
            var worldDir = CombatRules.QuantizeAxis(B - A);

            // 成长开始时已与旧剑任一边接触的目标不由本根攻击（防止进度 0 重复伤害）
            var oldEdges = SwordGeometry.GetEdges(working.sword);
            var excluded = new HashSet<int>();
            foreach (var e in working.enemies)
            {
                if (e.hp <= 0) continue;
                var center = BoardModel.CellCenter(e.cell);
                float contactR = cfg.bladeRadius + cfg.EnemyRadiusFor(e.kind) + cfg.contactTolerance;
                foreach (var edge in oldEdges)
                {
                    var ea = SwordGeometry.HalfToWorld(edge.aHalf, pivot, facing);
                    var eb = SwordGeometry.HalfToWorld(edge.bHalf, pivot, facing);
                    if (Geometry2D.PointSegmentDistanceSquared(center, ea, eb) <= contactR * contactR)
                    {
                        excluded.Add(e.actorId);
                        break;
                    }
                }
            }

            // 候选目标：完整新边能接触的存活敌人 / 核心；二分首次接触 g
            var hits = new List<(EnemyState enemy, float g, Vector2 contactPt)>();
            foreach (var e in working.enemies)
            {
                if (e.hp <= 0 || excluded.Contains(e.actorId))
                    continue;
                var center = BoardModel.CellCenter(e.cell);
                float contactR = cfg.bladeRadius + cfg.EnemyRadiusFor(e.kind);
                if (Geometry2D.PointSegmentDistanceSquared(center, A, B) > contactR * contactR)
                    continue;

                float lo = 0f, hi = 1f;
                for (int iter = 0; iter < 20; iter++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float dd = Vector2.Distance(Vector2.Lerp(A, B, mid), center);
                    if (dd <= contactR)
                        hi = mid;
                    else
                        lo = mid;
                }
                hits.Add((e, hi, Vector2.Lerp(A, B, hi)));
            }

            // 稳定排序：首次接触进度 → actorId（文档 6.8）
            hits.Sort((a, b) =>
            {
                int byG = a.g.CompareTo(b.g);
                return byG != 0 ? byG : a.enemy.actorId.CompareTo(b.enemy.actorId);
            });

            // 击退占位：玩家格不可进入；同伴占位逐事件用最新工作状态判定
            var reserved = new HashSet<Vector2Int> { working.player.cell };

            foreach (var (enemy, g, contactPt) in hits)
            {
                if (enemy.hp <= 0)
                    continue;

                var evt = new GrowthHitEvent
                {
                    actorId = enemy.actorId,
                    kind = enemy.kind,
                    g = g,
                    contactPoint = contactPt,
                    isCore = enemy.kind == EnemyKind.Core,
                    knockFrom = enemy.cell,
                    knockTo = enemy.cell,
                    hpBefore = enemy.hp,
                    growthActionId = growthActionId,
                };

                if (enemy.kind != EnemyKind.Box)
                    enemy.hp -= cfg.bladeDamage;
                if (enemy.kind != EnemyKind.Core && enemy.kind != EnemyKind.Box)
                    enemy.interruptedThisBeat = true;

                // 击退（核心不击退；已死亡不击退）：撞墙附伤，撞同伴 / 玩家 / 核心停留
                if (enemy.kind != EnemyKind.Core && enemy.hp > 0)
                {
                    var dest = enemy.cell + worldDir * cfg.knockbackCells;
                    bool blockedByWall = !board.IsFloor(dest);
                    bool blockedByActor = false;
                    if (!blockedByWall)
                    {
                        foreach (var other in working.enemies)
                        {
                            if (other.hp > 0 && other.cell == dest)
                            {
                                blockedByActor = true;
                                break;
                            }
                        }
                    }
                    if (blockedByWall)
                    {
                        evt.wallImpact = true;
                        if (enemy.kind != EnemyKind.Box)
                            enemy.hp -= cfg.wallImpactDamage;
                    }
                    else if (!blockedByActor && !reserved.Contains(dest))
                    {
                        enemy.cell = dest;
                        evt.knockTo = dest;
                        evt.movedByKnock = true;
                    }
                }

                evt.hpAfter = enemy.hp;
                evt.died = enemy.hp <= 0;
                events.Add(evt);
            }
            return events;
        }
    }
}
