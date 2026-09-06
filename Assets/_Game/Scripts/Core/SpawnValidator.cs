using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SwordGame
{
    /// <summary>出生候选验证（文档 5.5）：身体/剑不碰墙，身体不与敌人同格，静止剑不与敌人或核心重叠。</summary>
    public static class SpawnValidator
    {
        public static bool TryPickSpawn(
            BoardModel board, IList<SpawnCandidate> candidates, SwordState sword, IList<EnemySpawnDef> enemies,
            Vector2Int? coreCell, GameConfig cfg, out SpawnCandidate spawn, out string failReason)
        {
            var segments = SwordGeometry.GetSegments(sword);
            float tol = cfg.contactTolerance;

            for (int i = 0; i < enemies.Count; i++)
            for (int j = i + 1; j < enemies.Count; j++)
                if (enemies[i].cell == enemies[j].cell)
                {
                    spawn = default;
                    failReason = $"敌人初始重叠：{enemies[i].cell}";
                    return false;
                }

            string firstReason = null;
            foreach (var cand in candidates)
            {
                string reason;
                if (CandidateValid(board, segments, cand, enemies, coreCell, cfg, tol, out reason))
                {
                    spawn = cand;
                    failReason = null;
                    return true;
                }
                if (firstReason == null)
                    firstReason = reason;
            }

            spawn = default;
            failReason = firstReason ?? "全部候选失败";
            return false;
        }

        private static bool CandidateValid(
            BoardModel board, List<BladeSegment> segments, SpawnCandidate cand, IList<EnemySpawnDef> enemies,
            Vector2Int? coreCell, GameConfig cfg, float tol, out string failReason)
        {
            if (!board.IsFloor(cand.cell))
            {
                failReason = $"身体格 {cand.cell} 非地板";
                return false;
            }

            Vector2 pivot = BoardModel.CellCenter(cand.cell);

            // 剑不碰墙
            foreach (var seg in segments)
            {
                Vector2 a = SwordGeometry.HalfToWorld(seg.aHalf, pivot, cand.facing);
                Vector2 b = SwordGeometry.HalfToWorld(seg.bHalf, pivot, cand.facing);
                foreach (var rect in board.AllWallRects())
                {
                    float d2 = Geometry2D.SegmentAabbDistanceSquared(a, b, rect);
                    if (d2 <= (cfg.bladeRadius + tol) * (cfg.bladeRadius + tol))
                    {
                        failReason = $"剑段碰墙（候选 {cand.cell}/{cand.facing}）";
                        return false;
                    }
                }
                // 剑不与敌人/核心初始重叠
                foreach (var e in enemies)
                {
                    float rSum = cfg.bladeRadius + cfg.EnemyRadiusFor(e.kind) + tol;
                    if (Geometry2D.PointSegmentDistanceSquared(BoardModel.CellCenter(e.cell), a, b) <= rSum * rSum)
                    {
                        failReason = $"剑与敌人 {e.kind} 初始重叠（{e.cell}）";
                        return false;
                    }
                }
                if (coreCell.HasValue)
                {
                    float rCore = cfg.bladeRadius + cfg.coreRadius + tol;
                    if (Geometry2D.PointSegmentDistanceSquared(BoardModel.CellCenter(coreCell.Value), a, b) <= rCore * rCore)
                    {
                        failReason = "剑与核心初始重叠";
                        return false;
                    }
                }
            }

            // 身体不与敌人/核心同格
            foreach (var e in enemies)
                if (e.cell == cand.cell)
                {
                    failReason = $"身体与敌人同格 {e.cell}";
                    return false;
                }
            if (coreCell.HasValue && coreCell.Value == cand.cell)
            {
                failReason = "身体与核心同格";
                return false;
            }

            failReason = null;
            return true;
        }

        public static string Describe(SwordState sword)
        {
            return $"edges={sword.edges.Count} key={SwordGeometry.ShapeKey(sword)}";
        }
    }
}
