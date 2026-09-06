using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public struct HitCandidate
    {
        public int actorId;
        public EnemyKind kind;
        public float u;
        public int segmentIndex;
        public Vector2 contactPoint;
    }

    public struct ContactResult
    {
        public bool hasContact;
        public float u;
        public int segmentIndex;
        public Vector2Int wallCell;
        public Vector2 contactPoint;

        public static readonly ContactResult None = default;
    }

    /// <summary>平移/旋转的首次墙接触与目标接触求解（文档 6.3）。短区间扫描 + 自适应二分保守检测。</summary>
    public sealed class TrajectorySolver
    {
        private readonly GameConfig cfg;
        private const float SeparationProbe = 0.01f;

        // 剑局部线段缓存（米）：按实际边数动态扩容（v1.4 无边数上限，文档 5.6）
        private int segCount;
        private float[] ax = new float[16];
        private float[] ay = new float[16];
        private float[] bx = new float[16];
        private float[] by = new float[16];
        private float[] wx1 = new float[16];
        private float[] wy1 = new float[16];
        private float[] wx2 = new float[16];
        private float[] wy2 = new float[16];

        private void EnsureSegmentCapacity(int n)
        {
            if (ax.Length >= n)
                return;
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(n, ax.Length * 2));
            ax = new float[cap];
            ay = new float[cap];
            bx = new float[cap];
            by = new float[cap];
            wx1 = new float[cap];
            wy1 = new float[cap];
            wx2 = new float[cap];
            wy2 = new float[cap];
        }

        // 扫掠位姿参数
        private Vector2 pivot0;
        private Vector2 pivotDelta;   // 平移总位移
        private float startRad;
        private float deltaRad;       // 旋转总角
        private float maxEndpointRadius;

        public TrajectorySolver(GameConfig config)
        {
            cfg = config;
        }

        private void LoadPose(GameState state, PlayerAction action)
        {
            LoadSegments(state.sword);

            pivot0 = BoardModel.CellCenter(state.player.cell);
            startRad = state.player.facing * Mathf.PI / 2f;
            maxEndpointRadius = SwordGeometry.MaxEndpointRadius(state.sword);

            if (action.kind == PlayerActionKind.Move)
            {
                pivotDelta = new Vector2(action.moveDirection.x, action.moveDirection.y);
                deltaRad = 0f;
            }
            else
            {
                pivotDelta = Vector2.zero;
                deltaRad = action.quarterTurns * Mathf.PI / 2f;
            }
        }

        private void LoadSegments(SwordState sword)
        {
            var segments = SwordGeometry.GetSegments(sword);
            segCount = segments.Count;
            EnsureSegmentCapacity(segCount);
            for (int i = 0; i < segCount; i++)
            {
                var a = SwordGeometry.NodeToLocal(segments[i].aHalf);
                var b = SwordGeometry.NodeToLocal(segments[i].bHalf);
                ax[i] = a.x;
                ay[i] = a.y;
                bx[i] = b.x;
                by[i] = b.y;
            }
        }

        private void EvalSegments(float u)
        {
            Vector2 pivot = pivot0 + pivotDelta * u;
            float ang = startRad + deltaRad * u;
            float c = Mathf.Cos(ang);
            float s = Mathf.Sin(ang);
            for (int i = 0; i < segCount; i++)
            {
                float aLx = ax[i], aLy = ay[i], bLx = bx[i], bLy = by[i];
                wx1[i] = pivot.x + aLx * c - aLy * s;
                wy1[i] = pivot.y + aLx * s + aLy * c;
                wx2[i] = pivot.x + bLx * c - bLy * s;
                wy2[i] = pivot.y + bLx * s + bLy * c;
            }
        }

        private float MotionBound(float a, float b)
        {
            float halfSpan = (b - a) * 0.5f;
            float trans = pivotDelta.magnitude * halfSpan;
            float rot = maxEndpointRadius * Mathf.Abs(deltaRad) * halfSpan;
            return trans + rot;
        }

        /// <summary>候选墙格：整条轨迹包围区域（不能只查终点周围）。</summary>
        private List<Vector2Int> CollectCandidateWalls(BoardModel board, float bladeRadius)
        {
            var cells = new List<Vector2Int>(16);
            float r = bladeRadius + cfg.contactTolerance * 4f;

            // 扫掠包围盒
            float minX, minY, maxX, maxY;
            if (deltaRad == 0f)
            {
                EvalSegments(0f);
                minX = float.MaxValue; minY = float.MaxValue; maxX = float.MinValue; maxY = float.MinValue;
                for (int i = 0; i < segCount; i++)
                {
                    minX = Mathf.Min(minX, Mathf.Min(wx1[i], wx2[i]));
                    maxX = Mathf.Max(maxX, Mathf.Max(wx1[i], wx2[i]));
                    minY = Mathf.Min(minY, Mathf.Min(wy1[i], wy2[i]));
                    maxY = Mathf.Max(maxY, Mathf.Max(wy1[i], wy2[i]));
                }
                EvalSegments(1f);
                for (int i = 0; i < segCount; i++)
                {
                    minX = Mathf.Min(minX, Mathf.Min(wx1[i], wx2[i]));
                    maxX = Mathf.Max(maxX, Mathf.Max(wx1[i], wx2[i]));
                    minY = Mathf.Min(minY, Mathf.Min(wy1[i], wy2[i]));
                    maxY = Mathf.Max(maxY, Mathf.Max(wy1[i], wy2[i]));
                }
            }
            else
            {
                float R = maxEndpointRadius;
                minX = pivot0.x - R; maxX = pivot0.x + R;
                minY = pivot0.y - R; maxY = pivot0.y + R;
            }
            minX -= r; minY -= r; maxX += r; maxY += r;

            int cx0 = Mathf.Max(0, Mathf.FloorToInt(minX) - 1);
            int cx1 = Mathf.Min(board.Width - 1, Mathf.CeilToInt(maxX) + 1);
            int cy0 = Mathf.Max(0, Mathf.FloorToInt(minY) - 1);
            int cy1 = Mathf.Min(board.Height - 1, Mathf.CeilToInt(maxY) + 1);
            for (int y = cy0; y <= cy1; y++)
                for (int x = cx0; x <= cx1; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (board.Walls.Contains(cell))
                        cells.Add(cell);
                }
            return cells;
        }

        // ---- 墙接触（保守二分，含分离放行） ----

        private bool IntervalWallExcluded(float a, float b, Rect rect)
        {
            float m = (a + b) * 0.5f;
            EvalSegments(m);
            float bound = MotionBound(a, b);
            float eps = cfg.numericalEpsilon;
            float excludeR = bound + cfg.bladeRadius + eps;
            float excludeR2 = excludeR * excludeR;
            for (int i = 0; i < segCount; i++)
            {
                float d2 = Geometry2D.SegmentAabbDistanceSquared(
                    new Vector2(wx1[i], wy1[i]), new Vector2(wx2[i], wy2[i]), rect);
                if (d2 <= excludeR2)
                    return false;
            }
            return true;
        }

        private ContactResult FindWallContactInInterval(float a, float b, Rect rect, Vector2Int wallCell)
        {
            if (IntervalWallExcluded(a, b, rect))
                return ContactResult.None;

            if (MotionBound(a, b) <= cfg.contactTolerance)
            {
                // 叶子区间：实际穿入即接触；用固定探测步长判断分离运动（文档 6.4）
                EvalSegments(a);
                float distA = MinSegmentRectDistance(rect);
                if (distA < cfg.bladeRadius - cfg.contactTolerance)
                    return MakeWallResult(a, wallCell, rect);
                float probe = Mathf.Min(1f, a + SeparationProbe);
                EvalSegments(probe);
                float distB = MinSegmentRectDistance(rect);
                if (distB > distA + cfg.numericalEpsilon)
                    return ContactResult.None;
                return MakeWallResult(a, wallCell, rect);
            }

            float mid = (a + b) * 0.5f;
            var left = FindWallContactInInterval(a, mid, rect, wallCell);
            if (left.hasContact)
                return left;
            return FindWallContactInInterval(mid, b, rect, wallCell);
        }

        private float MinSegmentRectDistance(Rect rect)
        {
            float best = float.MaxValue;
            for (int i = 0; i < segCount; i++)
            {
                float d2 = Geometry2D.SegmentAabbDistanceSquared(
                    new Vector2(wx1[i], wy1[i]), new Vector2(wx2[i], wy2[i]), rect);
                if (d2 < best)
                    best = d2;
            }
            return Mathf.Sqrt(best);
        }

        private ContactResult MakeWallResult(float u, Vector2Int wallCell, Rect rect)
        {
            EvalSegments(u);
            int bestSeg = 0;
            float bestD2 = float.MaxValue;
            Vector2 bestPoint = new Vector2(wx1[0], wy1[0]);
            for (int i = 0; i < segCount; i++)
            {
                var a = new Vector2(wx1[i], wy1[i]);
                var b = new Vector2(wx2[i], wy2[i]);
                float d2 = Geometry2D.SegmentAabbDistanceSquared(a, b, rect);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestSeg = i;
                    bestPoint = Geometry2D.ClosestPointOnSegmentToRect(a, b, rect);
                }
            }
            return new ContactResult { hasContact = true, u = u, segmentIndex = bestSeg, wallCell = wallCell, contactPoint = bestPoint };
        }

        /// <summary>旋转/平移动作全程的首次墙接触。无接触返回 hasContact=false。</summary>
        public ContactResult FindFirstWallContact(GameState state, BoardModel board, PlayerAction action)
        {
            LoadPose(state, action);
            return SolveWalls(board);
        }

        /// <summary>显式位姿版本（测试与验证器使用）：pivot/startRad/deltaRad 可脱离格点量化。</summary>
        public ContactResult FindFirstWallContactPose(Vector2 pivot, float startRadValue, float deltaRadValue, SwordState sword, BoardModel board)
        {
            LoadSegments(sword);
            pivot0 = pivot;
            pivotDelta = Vector2.zero;
            startRad = startRadValue;
            deltaRad = deltaRadValue;
            maxEndpointRadius = SwordGeometry.MaxEndpointRadius(sword);
            return SolveWalls(board);
        }

        private ContactResult SolveWalls(BoardModel board)
        {
            if (segCount == 0)
                return ContactResult.None;
            float bladeRadius = cfg.bladeRadius;
            var candidates = CollectCandidateWalls(board, bladeRadius);
            ContactResult best = ContactResult.None;

            foreach (var cell in candidates)
            {
                var rect = board.WallRect(cell);
                var hit = FindWallContactInInterval(0f, 1f, rect, cell);
                if (hit.hasContact && (!best.hasContact || hit.u < best.u))
                    best = hit;
            }
            return best;
        }

        // ---- 敌人接触（胶囊 vs 圆） ----

        private float MinSegmentPointDistance(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < segCount; i++)
            {
                float d2 = Geometry2D.PointSegmentDistanceSquared(p, new Vector2(wx1[i], wy1[i]), new Vector2(wx2[i], wy2[i]));
                if (d2 < best)
                    best = d2;
            }
            return Mathf.Sqrt(best);
        }

        private bool IntervalEnemyExcluded(float a, float b, Vector2 center, float contactRadius)
        {
            float m = (a + b) * 0.5f;
            EvalSegments(m);
            float bound = MotionBound(a, b);
            float excludeR = bound + contactRadius + cfg.numericalEpsilon;
            float excludeR2 = excludeR * excludeR;
            return MinSegmentPointDistance(center) > excludeR;
        }

        private bool FindEnemyContactInInterval(float a, float b, Vector2 center, float contactRadius, out float u, out int segIndex, out Vector2 point)
        {
            u = 0f; segIndex = 0; point = default;
            if (IntervalEnemyExcluded(a, b, center, contactRadius))
                return false;

            if (MotionBound(a, b) <= cfg.contactTolerance)
            {
                EvalSegments(a);
                float distA = MinSegmentPointDistance(center);
                if (distA < contactRadius - cfg.contactTolerance)
                {
                    // 保守取 a 为首次接触进度
                    EvalSegments(a);
                    u = a;
                    float bestD2 = float.MaxValue;
                    for (int i = 0; i < segCount; i++)
                    {
                        var sa = new Vector2(wx1[i], wy1[i]);
                        var sb = new Vector2(wx2[i], wy2[i]);
                        float d2 = Geometry2D.PointSegmentDistanceSquared(center, sa, sb);
                        if (d2 < bestD2)
                        {
                            bestD2 = d2;
                            segIndex = i;
                            float denom = (sb - sa).sqrMagnitude;
                            float t = denom <= Geometry2D.GeomEpsilon
                                ? 0f
                                : Mathf.Clamp(Vector2.Dot(center - sa, sb - sa) / denom, 0f, 1f);
                            point = sa + t * (sb - sa);
                        }
                    }
                    return true;
                }
                float probe = Mathf.Min(1f, a + SeparationProbe);
                EvalSegments(probe);
                float distB = MinSegmentPointDistance(center);
                if (distB > distA + cfg.numericalEpsilon)
                    return false; // 分离运动放行
                // 保守接触
                EvalSegments(a);
                u = a;
                float bestD2b = float.MaxValue;
                for (int i = 0; i < segCount; i++)
                {
                    var sa = new Vector2(wx1[i], wy1[i]);
                    var sb = new Vector2(wx2[i], wy2[i]);
                    float d2 = Geometry2D.PointSegmentDistanceSquared(center, sa, sb);
                    if (d2 < bestD2b)
                    {
                        bestD2b = d2;
                        segIndex = i;
                        float denom = (sb - sa).sqrMagnitude;
                        float t = denom <= Geometry2D.GeomEpsilon
                            ? 0f
                            : Mathf.Clamp(Vector2.Dot(center - sa, sb - sa) / denom, 0f, 1f);
                        point = sa + t * (sb - sa);
                    }
                }
                return true;
            }

            float mid = (a + b) * 0.5f;
            if (FindEnemyContactInInterval(a, mid, center, contactRadius, out u, out segIndex, out point))
                return true;
            return FindEnemyContactInInterval(mid, b, center, contactRadius, out u, out segIndex, out point);
        }

        /// <summary>出程内每名敌人的首次接触（u∈[0,1]）。调用方负责墙截断过滤。</summary>
        public List<HitCandidate> FindEnemyContacts(GameState state, PlayerAction action, List<HitCandidate> results)
        {
            LoadPose(state, action);
            results.Clear();
            if (action.kind == PlayerActionKind.Wait || state.enemies == null)
                return results;

            float outerStepAngle = cfg.outerMotionStep / Mathf.Max(0.01f, maxEndpointRadius);
            int outerSteps;
            if (deltaRad == 0f)
                outerSteps = Mathf.Max(1, Mathf.CeilToInt(pivotDelta.magnitude / cfg.outerMotionStep));
            else
                outerSteps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(deltaRad) / outerStepAngle));

            foreach (var enemy in state.enemies)
            {
                if (enemy.hp <= 0)
                    continue;
                float contactRadius = cfg.bladeRadius + cfg.EnemyRadiusFor(enemy.kind);
                Vector2 center = BoardModel.CellCenter(enemy.cell);

                for (int step = 0; step < outerSteps; step++)
                {
                    float a = step / (float)outerSteps;
                    float b = (step + 1) / (float)outerSteps;
                    if (FindEnemyContactInInterval(a, b, center, contactRadius, out float u, out int seg, out Vector2 pt))
                    {
                        results.Add(new HitCandidate
                        {
                            actorId = enemy.actorId,
                            kind = enemy.kind,
                            u = u,
                            segmentIndex = seg,
                            contactPoint = pt,
                        });
                        break;
                    }
                }
            }
            return results;
        }

        /// <summary>身体平移路径（半径圆）的墙体检查。</summary>
        public bool BodyPathHitsWall(Vector2Int from, Vector2Int to, BoardModel board)
        {
            Vector2 a = BoardModel.CellCenter(from);
            Vector2 b = BoardModel.CellCenter(to);
            float r2 = cfg.playerRadius * cfg.playerRadius;

            int minX = Mathf.Min(from.x, to.x) - 1;
            int maxX = Mathf.Max(from.x, to.x) + 1;
            int minY = Mathf.Min(from.y, to.y) - 1;
            int maxY = Mathf.Max(from.y, to.y) + 1;
            for (int y = Mathf.Max(0, minY); y <= Mathf.Min(board.Height - 1, maxY); y++)
                for (int x = Mathf.Max(0, minX); x <= Mathf.Min(board.Width - 1, maxX); x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!board.Walls.Contains(cell))
                        continue;
                    if (Geometry2D.SegmentAabbDistanceSquared(a, b, board.WallRect(cell)) <= r2)
                        return true;
                }
            return false;
        }

        /// <summary>动作全程对任意圆目标（果等）的首次接触（文档 6.1：胶囊 vs 圆）。</summary>
        public bool FindFirstCircleContact(GameState state, PlayerAction action, Vector2 center, float contactRadius,
            out float u, out int segIndex, out Vector2 point)
        {
            LoadPose(state, action);
            u = 0f;
            segIndex = 0;
            point = default;
            if (action.kind == PlayerActionKind.Wait || segCount == 0)
                return false;

            float outerStepAngle = cfg.outerMotionStep / Mathf.Max(0.01f, maxEndpointRadius);
            int outerSteps;
            if (deltaRad == 0f)
                outerSteps = Mathf.Max(1, Mathf.CeilToInt(pivotDelta.magnitude / cfg.outerMotionStep));
            else
                outerSteps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(deltaRad) / outerStepAngle));

            for (int step = 0; step < outerSteps; step++)
            {
                float a = step / (float)outerSteps;
                float b = (step + 1) / (float)outerSteps;
                if (FindEnemyContactInInterval(a, b, center, contactRadius, out u, out segIndex, out point))
                    return true;
            }
            return false;
        }
    }
}
