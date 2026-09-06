using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>世界内成长候选覆盖层（文档 v1.4 5.4 / 11.4）：
    /// 候选画在战斗场景实际剑位置（主相机可见）；不使用 RT / 独立相机 / RawImage / 遮屏面板。
    /// 悬停仅高亮一条候选并显示预计命中敌人；全部为装饰显示，不挂伤害 Collider。</summary>
    public class WorldGrowthOverlay : MonoBehaviour
    {
        private Transform _root;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private SpriteRenderer _endpoint;
        private SpriteRenderer _anchorGlow;
        private readonly List<SpriteRenderer> _hitMarks = new List<SpriteRenderer>();
        private int _hoverIndex = -1;
        private bool _hoverDirty;

        private static readonly Color IdleColor = GardenTheme.Alpha(GardenTheme.Ink, 0.52f);
        private static readonly Color HoverColor = GardenTheme.Gold;

        public void Setup(Transform parent)
        {
            var go = new GameObject("WorldGrowthOverlay");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            _root.gameObject.SetActive(false);

            _endpoint = MakeMarker("Endpoint", GardenTheme.Ring(), GardenTheme.Ink, 0.20f, 41);
            _anchorGlow = MakeMarker("AnchorGlow", SpriteFactory.SoftCircle(), GardenTheme.Alpha(GardenTheme.Gold, 0.4f), 0.44f, 40);
        }

        private SpriteRenderer MakeMarker(string name, Sprite sprite, Color color, float size, int order)
        {
            var mGo = new GameObject(name);
            mGo.transform.SetParent(_root, false);
            var sr = mGo.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            float baseSize = sprite.bounds.size.x;
            if (baseSize > 0.001f)
                mGo.transform.localScale = Vector3.one * (size / baseSize);
            mGo.gameObject.SetActive(false);
            return sr;
        }

        /// <summary>显示候选覆盖层（战斗主画面的实际玩家格位 / 朝向）。</summary>
        public void Show(Vector2Int playerCell, int facing, IReadOnlyList<GrowthCandidate> candidates)
        {
            RebuildLines(playerCell, facing, candidates);
            _root.gameObject.SetActive(true);
            SetHover(-1);
        }

        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
            _hoverIndex = -1;
            _hoverDirty = true;
        }

        public void SetHover(int index)
        {
            if (_root == null || !_root.gameObject.activeSelf)
                return;
            if (index == _hoverIndex && !_hoverDirty)
                return;
            _hoverDirty = false;

            if (_hoverIndex >= 0 && _hoverIndex < _lines.Count)
            {
                var prev = _lines[_hoverIndex];
                prev.startColor = IdleColor;
                prev.endColor = IdleColor;
                prev.widthMultiplier = 0.06f;
            }
            _hoverIndex = index;

            bool has = index >= 0 && index < _lines.Count;
            _endpoint.gameObject.SetActive(has);
            _anchorGlow.gameObject.SetActive(has);
            if (has)
            {
                var lr = _lines[index];
                lr.startColor = HoverColor;
                lr.endColor = HoverColor;
                lr.widthMultiplier = 0.11f;
                _endpoint.transform.position = lr.GetPosition(1);
                _anchorGlow.transform.position = lr.GetPosition(0);
            }
        }

        /// <summary>悬停候选的预计命中提示：目标外圈 + 击退箭头 / 阻挡叉（文档 5.4）。</summary>
        public void ShowHitPreview(IReadOnlyList<GrowthHitEvent> hits)
        {
            ClearHitMarks();
            if (hits == null)
                return;
            foreach (var h in hits)
            {
                if (h.isCore)
                    continue; // 核心命中只由伤害反馈表达
                var fromPos = BoardModel.CellCenter(h.knockFrom);
                var ring = MakeHitMark(GardenTheme.Ring(), GardenTheme.Coral, 0.82f, fromPos);
                ring.sortingOrder = 4;
                if (h.movedByKnock)
                {
                    var toPos = BoardModel.CellCenter(h.knockTo);
                    var dir = (toPos - fromPos).normalized;
                    var arrow = MakeHitMark(GardenTheme.Arrow(), GardenTheme.Ink, 0.3f,
                        Vector2.Lerp(fromPos, toPos, 0.5f));
                    arrow.sortingOrder = 5;
                    arrow.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
                }
                else
                {
                    var cross = MakeHitMark(SpriteFactory.Cross(), new Color(1f, 0.45f, 0.35f, 0.9f), 0.24f, fromPos);
                    cross.sortingOrder = 5; // 击退被阻挡
                }
            }
        }

        private SpriteRenderer MakeHitMark(Sprite sprite, Color color, float size, Vector2 pos)
        {
            var go = new GameObject("HitMark");
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            float baseSize = sprite.bounds.size.x;
            if (baseSize > 0.001f)
                go.transform.localScale = Vector3.one * (size / baseSize);
            go.transform.position = pos;
            _hitMarks.Add(sr);
            return sr;
        }

        private void ClearHitMarks()
        {
            foreach (var sr in _hitMarks)
                if (sr != null)
                    Destroy(sr.gameObject);
            _hitMarks.Clear();
        }

        private void RebuildLines(Vector2Int playerCell, int facing, IReadOnlyList<GrowthCandidate> candidates)
        {
            foreach (var lr in _lines)
                if (lr != null)
                    Destroy(lr.gameObject);
            _lines.Clear();
            ClearHitMarks();
            _hoverIndex = -1;
            _hoverDirty = true;
            if (candidates == null)
                return;

            var pivot = BoardModel.CellCenter(playerCell);
            foreach (var cand in candidates)
            {
                var aWorld = SwordGeometry.HalfToWorld(cand.anchor, pivot, facing);
                var bWorld = SwordGeometry.HalfToWorld(cand.newNode, pivot, facing);
                var go = new GameObject($"Cand_{cand.edgeKey}");
                go.transform.SetParent(_root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.SetPosition(0, aWorld);
                lr.SetPosition(1, bWorld);
                lr.widthMultiplier = 0.06f;
                lr.numCapVertices = 2;
                lr.sharedMaterial = GardenTheme.LineMaterial;
                lr.sortingOrder = 30;
                lr.startColor = IdleColor;
                lr.endColor = IdleColor;
                _lines.Add(lr);
            }
        }

        private void OnDestroy()
        {
            // 子物体由 _root 层级统一销毁
        }
    }
}
