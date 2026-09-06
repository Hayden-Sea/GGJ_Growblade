using UnityEngine;

namespace SwordGame
{
    /// <summary>纯几何距离函数（文档 6.2），与场景组件分离。</summary>
    public static class Geometry2D
    {
        public const float GeomEpsilon = 1e-9f;

        public static float PointSegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom <= GeomEpsilon)
                return (p - a).sqrMagnitude;
            float t = Mathf.Clamp(Vector2.Dot(p - a, ab) / denom, 0f, 1f);
            Vector2 q = a + t * ab;
            return (p - q).sqrMagnitude;
        }

        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>含端点、平行与共线退化情况的两线段相交判定。</summary>
        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            Vector2 r = p2 - p1;
            Vector2 s = p4 - p3;
            Vector2 qp = p3 - p1;
            float rxs = Cross(r, s);
            float qpxr = Cross(qp, r);
            float rr = r.sqrMagnitude;
            float ss = s.sqrMagnitude;

            if (Mathf.Abs(rxs) < GeomEpsilon)
            {
                if (Mathf.Abs(qpxr) >= GeomEpsilon)
                    return false; // 平行不共线
                if (rr <= GeomEpsilon)
                    return PointSegmentDistanceSquared(p1, p3, p4) <= GeomEpsilon;
                if (ss <= GeomEpsilon)
                    return PointSegmentDistanceSquared(p3, p1, p2) <= GeomEpsilon;
                float t0 = Vector2.Dot(qp, r) / rr;
                float t1 = t0 + Vector2.Dot(s, r) / rr;
                if (t0 > t1) { (t0, t1) = (t1, t0); }
                return Mathf.Max(t0, 0f) <= Mathf.Min(t1, 1f) + GeomEpsilon;
            }

            if (rr <= GeomEpsilon)
                return PointSegmentDistanceSquared(p1, p3, p4) <= GeomEpsilon;
            if (ss <= GeomEpsilon)
                return PointSegmentDistanceSquared(p3, p1, p2) <= GeomEpsilon;

            float t = Cross(qp, s) / rxs;
            float u = qpxr / rxs;
            return t >= -GeomEpsilon && t <= 1f + GeomEpsilon &&
                   u >= -GeomEpsilon && u <= 1f + GeomEpsilon;
        }

        public static float SegmentSegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (SegmentsIntersect(a, b, c, d))
                return 0f;
            float m1 = PointSegmentDistanceSquared(a, c, d);
            float m2 = PointSegmentDistanceSquared(b, c, d);
            float m3 = PointSegmentDistanceSquared(c, a, b);
            float m4 = PointSegmentDistanceSquared(d, a, b);
            return Mathf.Min(Mathf.Min(m1, m2), Mathf.Min(m3, m4));
        }

        /// <summary>线段到 AABB 的距离平方；端点在盒内或与盒边相交时为 0。保留胶囊圆角，不做扩大方角近似。</summary>
        public static float SegmentAabbDistanceSquared(Vector2 a, Vector2 b, Rect box)
        {
            if (box.Contains(a) || box.Contains(b))
                return 0f;
            Vector2 bl = new Vector2(box.xMin, box.yMin);
            Vector2 br = new Vector2(box.xMax, box.yMin);
            Vector2 tr = new Vector2(box.xMax, box.yMax);
            Vector2 tl = new Vector2(box.xMin, box.yMax);
            float d1 = SegmentSegmentDistanceSquared(a, b, bl, br);
            float d2 = SegmentSegmentDistanceSquared(a, b, br, tr);
            float d3 = SegmentSegmentDistanceSquared(a, b, tr, tl);
            float d4 = SegmentSegmentDistanceSquared(a, b, tl, bl);
            return Mathf.Min(Mathf.Min(d1, d2), Mathf.Min(d3, d4));
        }

        /// <summary>点到 AABB 的最近点（用于估算接触点）。</summary>
        public static Vector2 ClosestPointOnRect(Vector2 p, Rect box)
        {
            return new Vector2(Mathf.Clamp(p.x, box.xMin, box.xMax), Mathf.Clamp(p.y, box.yMin, box.yMax));
        }

        /// <summary>线段到 AABB 的最近点：取线段上到盒距离最小的点（视觉火花用）。</summary>
        public static Vector2 ClosestPointOnSegmentToRect(Vector2 a, Vector2 b, Rect box, int samples = 8)
        {
            Vector2 best = a;
            float bestD = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector2 p = Vector2.Lerp(a, b, t);
                Vector2 q = ClosestPointOnRect(p, box);
                float d = (p - q).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
