using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>房内原位放置过滤（文档 v1.4 4.6）：图合法候选还要在实际 cell+facing 上满足
    /// 不穿墙 / 越界、不穿过玩家、不接触未收集果。
    /// v1.4：存活敌人 / 核心不再使候选非法（改为成长攻击的合法命中目标）。</summary>
    public static class GrowthPlacement
    {
        public static (bool ok, string reason) Filter(
            SwordState sword, BladeSegment newEdge, Vector2Int cell, int facing,
            BoardModel board, GameState state, Vector2Int? coreCell, GameConfig cfg)
        {
            float tol = cfg.contactTolerance;
            Vector2 pivot = BoardModel.CellCenter(cell);
            Vector2 a = SwordGeometry.HalfToWorld(newEdge.aHalf, pivot, facing);
            Vector2 b = SwordGeometry.HalfToWorld(newEdge.bHalf, pivot, facing);

            // 墙与地图边界
            foreach (var rect in board.AllWallRects())
            {
                float limit = cfg.bladeRadius + tol;
                if (Geometry2D.SegmentAabbDistanceSquared(a, b, rect) <= limit * limit)
                    return (false, "墙体阻挡");
            }

            // 玩家身体（新增部分不得穿过角色）
            {
                float limit = cfg.bladeRadius + cfg.playerRadius + tol;
                if (Geometry2D.PointSegmentDistanceSquared(pivot, a, b) <= limit * limit)
                    return (false, "穿过角色");
            }

            // 未收集果：已收集果不阻挡
            foreach (var f in state.fruits)
            {
                if (f.consumed)
                    continue;
                float limit = cfg.bladeRadius + cfg.fruitRadius + tol;
                if (Geometry2D.PointSegmentDistanceSquared(BoardModel.CellCenter(f.cell), a, b) <= limit * limit)
                    return (false, "与果重叠");
            }

            return (true, null);
        }
    }
}
