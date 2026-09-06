using System;
using UnityEngine;

namespace SwordGame
{
    /// <summary>Continuous checks against the current sword; no cell rounding and no combat side effects.</summary>
    public static class MovementCollision
    {
        public static bool EnemyPathHitsSword(EnemyState enemy, Vector2Int target, GameState state, GameConfig cfg)
            => EnemyPathHitsSword(enemy.kind, enemy.cell, target, state, cfg);

        public static bool EnemyPathHitsSword(EnemyKind kind, Vector2Int fromCell, Vector2Int target, GameState state, GameConfig cfg)
        {
            var from = BoardModel.CellCenter(fromCell);
            var to = BoardModel.CellCenter(target);
            var pivot = BoardModel.CellCenter(state.player.cell);
            float radius = cfg.EnemyRadiusFor(kind) + cfg.bladeRadius + cfg.contactTolerance;
            foreach (var edge in SwordGeometry.GetSegments(state.sword))
            {
                var a = SwordGeometry.HalfToWorld(edge.aHalf, pivot, state.player.facing);
                var b = SwordGeometry.HalfToWorld(edge.bHalf, pivot, state.player.facing);
                // Legacy growth can leave an enemy overlapping when knockback is blocked.
                // Permit only a fully separating exit, never movement deeper through the blade.
                var ab = b - a;
                var nearest = a + ab * Mathf.Clamp01(Vector2.Dot(from - a, ab) / Mathf.Max(ab.sqrMagnitude, .000001f));
                bool startsInside = (from - nearest).sqrMagnitude <= radius * radius;
                if (startsInside && Vector2.Dot(from - nearest, to - from) >= 0 &&
                    Geometry2D.PointSegmentDistanceSquared(to, a, b) > radius * radius)
                    continue;
                if (Geometry2D.SegmentSegmentDistanceSquared(from, to, a, b) <= radius * radius)
                    return true;
            }
            return false;
        }

        /// <summary>Furthest safe visual progress for an already rejected move. Does not modify the model.</summary>
        public static float BlockedMoveProgress(GameState state, PlayerAction action, BoardModel board, GameConfig cfg)
        {
            Vector2 from = BoardModel.CellCenter(state.player.cell);
            Vector2 delta = action.moveDirection;
            float stop = 1f;
            var solver = new TrajectorySolver(cfg);
            var wall = solver.FindFirstWallContact(state, board, action);
            if (wall.hasContact) stop = Mathf.Min(stop, wall.u);
            float bodyR = cfg.playerRadius + cfg.contactTolerance;
            foreach (var rect in board.AllWallRects())
                stop = Mathf.Min(stop, FirstContact(u => Geometry2D.SegmentAabbDistanceSquared(from, from + delta * u, rect) <= bodyR * bodyR));
            // Board edges remain solid even when an author omits perimeter wall cells.
            stop = Mathf.Min(stop, FirstContact(u => {
                var p = from + delta * u;
                return p.x - bodyR <= 0 || p.y - bodyR <= 0 || p.x + bodyR >= board.Width || p.y + bodyR >= board.Height;
            }));
            foreach (var enemy in state.enemies)
            {
                if (enemy.hp <= 0) continue;
                var center = BoardModel.CellCenter(enemy.cell);
                float r = cfg.playerRadius + cfg.EnemyRadiusFor(enemy.kind) + cfg.contactTolerance;
                stop = Mathf.Min(stop, FirstContact(u => Geometry2D.PointSegmentDistanceSquared(center, from, from + delta * u) <= r * r));
                if (solver.FindFirstCircleContact(state, action, center, cfg.bladeRadius + cfg.EnemyRadiusFor(enemy.kind), out float uHit, out _, out _))
                    stop = Mathf.Min(stop, uHit);
            }
            return Mathf.Max(0, stop - cfg.contactTolerance);
        }

        private static float FirstContact(Func<float, bool> touches)
        {
            if (touches(0)) return 0;
            if (!touches(1)) return 1;
            float lo = 0, hi = 1;
            for (int i = 0; i < 20; i++)
            {
                float mid = (lo + hi) * .5f;
                if (touches(mid)) hi = mid; else lo = mid;
            }
            return lo;
        }
    }
}
