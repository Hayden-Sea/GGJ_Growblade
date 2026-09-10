using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>可用成长点、世界内候选、鼠标悬停选择、左键确认（文档 v1.4 5.4-5.5）。
    /// 不设次数 / 边数上限；候选 = 图规则 ∩ 房间放置过滤（敌人 / 核心不阻止）。
    /// 确认与执行由 RunController 事务完成，这里只维护可见候选与悬停选择。</summary>
    public class GrowthController
    {
        private readonly GameConfig _cfg;
        private readonly List<GrowthCandidate> _candidates = new List<GrowthCandidate>();
        private SwordState _base;
        private int _selectedIndex = -1;
        private int _baseSwordVersion;

        public event Action<string> OnStatsChanged;

        public GrowthController(GameConfig cfg) { _cfg = cfg; }

        public bool HasCandidates => _candidates.Count > 0;
        public int CandidateCount => _candidates.Count;
        public bool HasSelection => _selectedIndex >= 0 && _selectedIndex < _candidates.Count;
        public int SelectedIndex => _selectedIndex;
        public GrowthCandidate Selected => HasSelection ? _candidates[_selectedIndex] : default;
        public IReadOnlyList<GrowthCandidate> Candidates => _candidates;
        public int BaseSwordVersion => _baseSwordVersion;

        /// <summary>打开候选选择：初始不默认选第一个（文档 5.4）。</summary>
        public void Open(SwordState current, int swordVersion, Func<SwordState, BladeSegment, bool> placeable)
        {
            _base = current.Clone();
            _baseSwordVersion = swordVersion;
            _candidates.Clear();
            _selectedIndex = -1;
            foreach (var c in GrowthOptions.GetGraphCandidates(current, _cfg))
                if (placeable(current, c.edge))
                    _candidates.Add(c);
            Notify();
        }

        /// <summary>鼠标世界位置（剑局部，格单位）选择最近候选；旧剑 / 结点死区不选择。</summary>
        public bool PickCandidate(Vector2 localWorld, SwordState baseSword, GameConfig cfg)
        {
            if (_candidates.Count == 0)
                return false;

            foreach (var n in SwordGeometry.GetNodes(baseSword))
            {
                var nw = SwordGeometry.NodeToLocal(n);
                if (Vector2.Distance(nw, localWorld) < cfg.growthNodeDeadZone)
                {
                    ClearSelection();
                    return false;
                }
            }
            foreach (var e in baseSword.edges)
            {
                var a = SwordGeometry.NodeToLocal(e.aHalf);
                var b = SwordGeometry.NodeToLocal(e.bHalf);
                if (Geometry2D.PointSegmentDistanceSquared(localWorld, a, b) <= 0.15f * 0.15f)
                {
                    ClearSelection();
                    return false;
                }
            }

            // 等距平局先比中点距离、再比规范化边键（文档 5.4）
            int best = -1;
            float bestD = cfg.growthHoverRadius;
            float bestMid = float.MaxValue;
            string bestKey = null;
            for (int i = 0; i < _candidates.Count; i++)
            {
                var e = _candidates[i].edge;
                var a = SwordGeometry.NodeToLocal(e.aHalf);
                var b = SwordGeometry.NodeToLocal(e.bHalf);
                float d = Mathf.Sqrt(Geometry2D.PointSegmentDistanceSquared(localWorld, a, b));
                if (d > bestD)
                    continue;
                float mid = Vector2.Distance(localWorld, (a + b) * 0.5f);
                bool better = best < 0
                    || d < bestD - 1e-6f
                    || (Mathf.Abs(d - bestD) <= 1e-6f && (mid < bestMid - 1e-6f ||
                        (Mathf.Abs(mid - bestMid) <= 1e-6f && string.CompareOrdinal(_candidates[i].edgeKey, bestKey) < 0)));
                if (!better)
                    continue;
                best = i;
                bestD = d;
                bestMid = mid;
                bestKey = _candidates[i].edgeKey;
            }
            if (best < 0)
            {
                ClearSelection();
                return false;
            }
            if (_selectedIndex == best)
                return false; // 同一候选重复悬停：不重复通知
            _selectedIndex = best;
            Notify();
            return true;
        }

        public void ClearSelection()
        {
            if (_selectedIndex != -1)
            {
                _selectedIndex = -1;
                Notify();
            }
        }

        private void Notify()
        {
            var preview = HasSelection ? GrowthOptions.WithEdge(_base, Selected.edge) : _base;
            OnStatsChanged?.Invoke(Describe(preview, HasSelection));
        }

        private string Describe(SwordState preview, bool hasSel)
        {
            float reach = SwordGeometry.MaxReachRadius(preview);
            return GameLocalization.GrowthPreview(hasSel, preview.edges.Count, reach);
        }
    }
}
