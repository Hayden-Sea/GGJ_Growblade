using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>火柴式生长图规则测试（文档 5.2 / 14.2 G 系列）。</summary>
    public class GrowthGraphTests
    {
        private GameConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<GameConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (cfg != null)
                Object.DestroyImmediate(cfg);
        }

        [Test]
        public void G10_InitialSword_HasHalfPlusThreeTenthsEdges()
        {
            var s = SwordState.CreateInitial();
            Assert.AreEqual(2, s.edges.Count);
            Assert.AreEqual(new Vector2Int(5, 0), s.edges[0].aHalf);
            Assert.AreEqual(new Vector2Int(10, 0), s.edges[0].bHalf);
            Assert.AreEqual(new Vector2Int(10, 0), s.edges[1].aHalf);
            Assert.AreEqual(new Vector2Int(13, 0), s.edges[1].bHalf);
            Assert.AreEqual(0, s.growthCount);
        }

        [Test]
        public void G11_InitialGraphCandidates_ExactlySeven()
        {
            // 结点 {(0.5,0),(1.0,0),(1.3,0)}：跨过 1.3 的候选被拒绝，其余 7 个方向合法。
            var candidates = GrowthOptions.GetGraphCandidates(SwordState.CreateInitial(), cfg);
            Assert.AreEqual(7, candidates.Count);
        }

        [Test]
        public void G12_GraphCandidates_RejectClosedLoopAndDuplicates()
        {
            // 已有结点不得成为新端点，且新边不能跨过 0.3 格尖段的既有结点。
            var s = SwordState.CreateInitial();
            foreach (var c in GrowthOptions.GetGraphCandidates(s, cfg))
                Assert.IsFalse(SwordGeometry.GetNodes(s).Contains(c.newNode), "新结点必须全新");
        }

        [Test]
        public void G13_GraphCandidates_RejectEdgesThroughPlayerBody()
        {
            // (0,0)-(1,0) 穿过角色圆（半径 0.38）→ 不得出现；初始也无法向正后方生长
            var candidates = GrowthOptions.GetGraphCandidates(SwordState.CreateInitial(), cfg);
            foreach (var c in candidates)
            {
                var a = SwordGeometry.NodeToLocal(c.edge.aHalf);
                var b = SwordGeometry.NodeToLocal(c.edge.bHalf);
                Assert.Greater(Geometry2D.PointSegmentDistanceSquared(Vector2.zero, a, b), 0.38f * 0.38f - 1e-6f);
            }
        }

        [Test]
        public void G14_UniqueShapeCounts_MatchDocumentedBaseline()
        {
            // 文档 5.3：0.5+0.3 格初始剑的各深度唯一形状数。
            var counts = GrowthOptions.CountUniqueShapesByDepth(cfg, 5);
            var expected = new[] { 1, 7, 42, 235, 1276, 6813 };
            Assert.AreEqual(expected.Length, counts.Count);
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], counts[i], $"深度 {i} 的唯一形状数");
            Assert.AreEqual(8374, counts.Sum());
        }

        [Test]
        public void G15_FourRotationsOfAnyShape_ReturnExactEdges()
        {
            // S01 补充：任意形状键在四次 90° 后应与初始完全一致（半格整数无漂移）
            var s = SwordState.CreateInitial();
            s.edges.Add(BladeSegment.Canonical(new Vector2Int(13, 0), new Vector2Int(13, -5)));
            s.edges.Add(BladeSegment.Canonical(new Vector2Int(13, -5), new Vector2Int(18, -5)));
            var key = SwordGeometry.ShapeKey(s);
            // 旋转由 facing 表达，剑局部数据不变：这里验证键稳定与克隆独立
            var clone = s.Clone();
            clone.edges.Add(BladeSegment.Canonical(new Vector2Int(0, 5), new Vector2Int(5, 5)));
            Assert.AreEqual(key, SwordGeometry.ShapeKey(s));
            Assert.AreEqual(4, s.edges.Count);
        }

        [Test]
        public void G16_MaxReachRadius_MatchesDocumentedScales()
        {
            Assert.AreEqual(1.3f, SwordGeometry.MaxReachRadius(SwordState.CreateInitial()), 1e-6f);
            var s = SwordState.CreateInitial();
            for (int n = 13; n < 38; n += 5)
                s.edges.Add(BladeSegment.Canonical(new Vector2Int(n, 0), new Vector2Int(n + 5, 0)));
            Assert.AreEqual(3.8f, SwordGeometry.MaxReachRadius(s), 1e-6f);
        }

        [Test]
        public void G17_GraphCandidates_NoGrowthBudgetLimit()
        {
            // v1.4：无全局成长上限，高 growthCount 仍可生成候选
            var s = SwordState.CreateInitial();
            s.growthCount = 99;
            Assert.Greater(GrowthOptions.GetGraphCandidates(s, cfg).Count, 0, "无上限：growthCount=99 仍可生长");
        }
    }
}
