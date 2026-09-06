using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    /// <summary>Geometry2D 纯函数测试（G01-G04，文档 14.2）。</summary>
    public class Geometry2DTests
    {
        [Test]
        public void G01_PointBeyondEndpoint_NearestPointIsEndpoint()
        {
            var a = new Vector2(0f, 0f);
            var b = new Vector2(1f, 0f);

            float dRight = Geometry2D.PointSegmentDistanceSquared(new Vector2(2f, 0f), a, b);
            float dLeft = Geometry2D.PointSegmentDistanceSquared(new Vector2(-1f, 0f), a, b);
            float dMid = Geometry2D.PointSegmentDistanceSquared(new Vector2(0.5f, 1f), a, b);

            Assert.AreEqual(1f, dRight, 1e-6f);
            Assert.AreEqual(1f, dLeft, 1e-6f);
            Assert.AreEqual(1f, dMid, 1e-6f);
        }

        [Test]
        public void G02_ParallelAndCollinear()
        {
            var a1 = new Vector2(0f, 0f);
            var b1 = new Vector2(1f, 0f);
            var a2 = new Vector2(0f, 1f);
            var b2 = new Vector2(1f, 1f);

            // 平行分离：距离 1
            Assert.AreEqual(1f, Geometry2D.SegmentSegmentDistanceSquared(a1, b1, a2, b2), 1e-6f);

            // 共线重叠：0
            Assert.AreEqual(0f, Geometry2D.SegmentSegmentDistanceSquared(
                new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(1f, 0f), new Vector2(3f, 0f)), 1e-6f);

            // 共线分离：距离 1
            Assert.AreEqual(1f, Geometry2D.SegmentSegmentDistanceSquared(
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(3f, 0f)), 1e-6f);
        }

        [Test]
        public void G03_CornerGraze_UsesTrueDistanceNotExpandedBox()
        {
            var box = new Rect(2f, 2f, 1f, 1f);
            var a = new Vector2(1.0f, 1.0f);
            var b = new Vector2(1.9f, 1.9f); // 距角 (2,2) 0.1414，胶囊半径 0.10 时不接触

            float d2 = Geometry2D.SegmentAabbDistanceSquared(a, b, box);
            float d = Mathf.Sqrt(d2);

            // 真实圆角距离（0.1414），而非扩大方角误判为 0
            Assert.AreEqual(0.02f, d2, 1e-5f);
            Assert.Greater(d, 0.10f, "扩大方角会把 0.1414 误判为接触");
        }

        [Test]
        public void G04_ZeroLengthSegment_BehavesAsPoint()
        {
            var p = new Vector2(1f, 1f);
            float point = Geometry2D.PointSegmentDistanceSquared(new Vector2(3f, 4f), p, p);
            Assert.AreEqual(13f, point, 1e-6f); // (2,3) → 4+9

            float segSeg = Geometry2D.SegmentSegmentDistanceSquared(p, p, new Vector2(2f, 1f), new Vector2(3f, 1f));
            Assert.AreEqual(1f, segSeg, 1e-6f);

            float segBox = Geometry2D.SegmentAabbDistanceSquared(p, p, new Rect(2f, 2f, 1f, 1f));
            Assert.AreEqual(2f, segBox, 1e-6f);

            Assert.IsFalse(float.IsNaN(point));
            Assert.IsFalse(float.IsNaN(segSeg));
            Assert.IsFalse(float.IsNaN(segBox));
        }
    }
}
