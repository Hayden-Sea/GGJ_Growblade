using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public struct GrowthCandidate
    {
        public BladeSegment edge;      // 规范化新边
        public Vector2Int anchor;      // 原始锚点（已有结点，决定伸长方向）
        public Vector2Int newNode;     // 新端点
        public string edgeKey;
    }

    /// <summary>火柴式生长（文档 v1.4 5.2-5.3）：图候选生成与形状枚举。
    /// 图规则只考虑剑图与玩家避让，不设次数 / 边数 / 果数上限；
    /// 房间约束（墙 / 玩家 / 未收集果）由 GrowthPlacement 单独过滤，敌人 / 核心不阻止候选。</summary>
    public static class GrowthOptions
    {
        public static SwordState CreateInitial() => SwordState.CreateInitial();

        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(SwordGeometry.GrowthNodeUnits, 0), new Vector2Int(-SwordGeometry.GrowthNodeUnits, 0),
            new Vector2Int(0, SwordGeometry.GrowthNodeUnits), new Vector2Int(0, -SwordGeometry.GrowthNodeUnits),
        };

        /// <summary>全部图规则合法候选（无次数 / 边数上限；玩家避让保留）。</summary>
        public static List<GrowthCandidate> GetGraphCandidates(SwordState sword, GameConfig cfg)
        {
            var result = new List<GrowthCandidate>();
            var nodes = SwordGeometry.GetNodes(sword);
            var existing = new HashSet<string>();
            foreach (var e in sword.edges)
                existing.Add(e.Key);

            var sortedNodes = new List<Vector2Int>(nodes);
            sortedNodes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            foreach (var anchor in sortedNodes)
            {
                foreach (var dir in Directions)
                {
                    var newNode = anchor + dir;
                    if (nodes.Contains(newNode))
                        continue;
                    var edge = BladeSegment.Canonical(anchor, newNode);
                    if (existing.Contains(edge.Key))
                        continue;
                    // 初始第二节为 0.3 格；不能让新的 0.5 格边跨过这个已有结点或旧边。
                    if (PassesThroughExistingNode(edge, nodes))
                        continue;

                    // 玩家避让：新边胶囊到原点的距离必须严格大于 playerRadius + bladeRadius
                    var a = SwordGeometry.NodeToLocal(edge.aHalf);
                    var b = SwordGeometry.NodeToLocal(edge.bHalf);
                    float d2 = Geometry2D.PointSegmentDistanceSquared(Vector2.zero, a, b);
                    float minR = cfg.playerRadius + cfg.bladeRadius;
                    if (d2 <= minR * minR + cfg.numericalEpsilon)
                        continue;

                    result.Add(new GrowthCandidate
                    {
                        edge = edge,
                        anchor = anchor,
                        newNode = newNode,
                        edgeKey = edge.Key,
                    });
                }
            }
            return result;
        }

        private static bool PassesThroughExistingNode(BladeSegment edge, HashSet<Vector2Int> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == edge.aHalf || node == edge.bHalf)
                    continue;
                var cross = (node.x - edge.aHalf.x) * (edge.bHalf.y - edge.aHalf.y)
                    - (node.y - edge.aHalf.y) * (edge.bHalf.x - edge.aHalf.x);
                if (cross != 0)
                    continue;
                int dot = (node.x - edge.aHalf.x) * (node.x - edge.bHalf.x)
                    + (node.y - edge.aHalf.y) * (node.y - edge.bHalf.y);
                if (dot < 0)
                    return true;
            }
            return false;
        }

        public static SwordState WithEdge(SwordState sword, BladeSegment edge)
        {
            var next = sword.Clone();
            next.edges.Add(BladeSegment.Canonical(edge.aHalf, edge.bHalf));
            next.growthCount++;
            return next;
        }

        /// <summary>按成长深度统计唯一形状数（显式深度参数仅用于回归测试，不限制运行时）。</summary>
        public static List<int> CountUniqueShapesByDepth(GameConfig cfg, int maxDepth)
        {
            var byDepth = new List<int>();
            var seen = new HashSet<string>();
            var current = new List<SwordState> { CreateInitial() };
            seen.Add(SwordGeometry.ShapeKey(current[0]));
            byDepth.Add(current.Count);

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                var next = new List<SwordState>();
                foreach (var s in current)
                    foreach (var c in GetGraphCandidates(s, cfg))
                    {
                        var grown = WithEdge(s, c.edge);
                        var key = SwordGeometry.ShapeKey(grown);
                        if (seen.Add(key))
                            next.Add(grown);
                    }
                byDepth.Add(next.Count);
                current = next;
            }
            return byDepth;
        }

        /// <summary>全部唯一形状状态（编辑器静态检查用；注意验证口径，见文档 19.4）。</summary>
        public static List<SwordState> EnumerateShapeStates(GameConfig cfg, int maxDepth)
        {
            var result = new List<SwordState>();
            var seen = new HashSet<string>();
            var queue = new Queue<SwordState>();
            var initial = CreateInitial();
            queue.Enqueue(initial);
            seen.Add(SwordGeometry.ShapeKey(initial));
            result.Add(initial);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur.growthCount >= maxDepth)
                    continue;
                foreach (var c in GetGraphCandidates(cur, cfg))
                {
                    var grown = WithEdge(cur, c.edge);
                    var key = SwordGeometry.ShapeKey(grown);
                    if (seen.Add(key))
                    {
                        result.Add(grown);
                        queue.Enqueue(grown);
                    }
                }
            }
            return result;
        }
    }
}
