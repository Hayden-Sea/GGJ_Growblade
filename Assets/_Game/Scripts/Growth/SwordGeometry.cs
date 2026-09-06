using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>短节图几何（文档 5.1 / 3.3）：edges 是唯一武器数据真值。
    /// 字段沿用 aHalf/bHalf 命名以兼容旧资产，实际为 0.1 格整数坐标。</summary>
    public static class SwordGeometry
    {
        public const int GrowthNodeUnits = 5;
        public const float NodeUnit = 0.1f;

        public static Vector2 NodeToLocal(Vector2Int node) => new Vector2(node.x * NodeUnit, node.y * NodeUnit);
        /// <summary>规范化排序后的边列表（遍历与渲染的稳定顺序）。</summary>
        public static List<BladeSegment> GetEdges(SwordState s)
        {
            var list = new List<BladeSegment>(s.edges);
            list.Sort((a, b) =>
            {
                int c = a.aHalf.x.CompareTo(b.aHalf.x);
                if (c != 0) return c;
                c = a.aHalf.y.CompareTo(b.aHalf.y);
                if (c != 0) return c;
                c = a.bHalf.x.CompareTo(b.bHalf.x);
                if (c != 0) return c;
                return a.bHalf.y.CompareTo(b.bHalf.y);
            });
            return list;
        }

        /// <summary>兼容别名：求解器与表现层按线段集合消费。</summary>
        public static List<BladeSegment> GetSegments(SwordState s) => GetEdges(s);

        /// <summary>全部结点（去重）。</summary>
        public static HashSet<Vector2Int> GetNodes(SwordState s)
        {
            var nodes = new HashSet<Vector2Int>();
            foreach (var e in s.edges)
            {
                nodes.Add(e.aHalf);
                nodes.Add(e.bHalf);
            }
            return nodes;
        }

        /// <summary>形状键：全部规范化边排序后连接，用于去重与校验（文档 5.1）。</summary>
        public static string ShapeKey(SwordState s)
        {
            var edges = GetEdges(s);
            var parts = new string[edges.Count];
            for (int i = 0; i < edges.Count; i++)
                parts[i] = edges[i].Key;
            return string.Join(";", parts);
        }

        /// <summary>最大触及：轴心到全部结点的最大距离（文档 3.2 maxReachRadius），不含剑半宽。</summary>
        public static float MaxReachRadius(SwordState s)
        {
            float max = 0f;
            foreach (var n in GetNodes(s))
                max = Mathf.Max(max, NodeToLocal(n).magnitude);
            return max;
        }

        /// <summary>求解器运动界使用的最大端点半径（与 MaxReachRadius 同义）。</summary>
        public static float MaxEndpointRadius(SwordState s) => MaxReachRadius(s);

        public static Vector2 HalfToWorld(Vector2Int half, Vector2 pivot, int facing)
        {
            Vector2 local = NodeToLocal(half);
            float rad = facing * Mathf.PI / 2f;
            float c = Mathf.Cos(rad);
            float sn = Mathf.Sin(rad);
            return pivot + new Vector2(local.x * c - local.y * sn, local.x * sn + local.y * c);
        }
    }
}
