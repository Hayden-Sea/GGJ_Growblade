using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>伤害、击退与冲突顺序（文档 4.7 / 8.2）。</summary>
    public static class CombatRules
    {
        /// <summary>旋转切线量化：45° 平分线固定水平优先（R05）。</summary>
        public static Vector2Int QuantizeTangent(Vector2 relative, bool counterClockwise)
        {
            Vector2 t = counterClockwise
                ? new Vector2(-relative.y, relative.x)
                : new Vector2(relative.y, -relative.x);
            return QuantizeAxis(t);
        }

        public static Vector2Int QuantizeAxis(Vector2 t)
        {
            if (Mathf.Abs(t.x) >= Mathf.Abs(t.y))
                return new Vector2Int(t.x >= 0f ? 1 : -1, 0);
            return new Vector2Int(0, t.y >= 0f ? 1 : -1);
        }

        /// <summary>
        /// 解算一次击退（R06）：空格移入；墙/边界撞墙附加伤害；
        /// 同伴/核心/玩家保留格停留且无附加伤。返回是否移动。
        /// 调用前需已把命中事件 Add 进 presentation（本方法回写最后一条）。
        /// </summary>
        public static bool ResolveKnockback(
            EnemyState target, Vector2Int direction, BoardModel board, GameConfig cfg,
            GameState state, HashSet<Vector2Int> reservedCells, ActionPresentation presentation, HitEvent hit)
        {
            Vector2Int dest = target.cell + direction * cfg.knockbackCells;
            hit.knockTo = target.cell;

            bool blockedByWall = !board.IsFloor(dest);
            bool blockedByActor = false;
            if (!blockedByWall)
            {
                foreach (var e in state.enemies)
                {
                    if (e.hp > 0 && e.cell == dest)
                    {
                        blockedByActor = true;
                        break;
                    }
                }
            }

            if (blockedByWall || blockedByActor || reservedCells.Contains(dest))
            {
                if (blockedByWall)
                {
                    hit.wallImpact = true;
                    if (target.kind != EnemyKind.Box)
                        target.hp -= cfg.wallImpactDamage;
                    hit.hpAfter = target.hp;
                    if (target.hp <= 0)
                        hit.died = true;
                }
                presentation.hits[presentation.hits.Count - 1] = hit;
                return false;
            }

            target.cell = dest;
            hit.knockTo = dest;
            hit.movedByKnock = true;
            presentation.hits[presentation.hits.Count - 1] = hit;
            return true;
        }
    }
}
