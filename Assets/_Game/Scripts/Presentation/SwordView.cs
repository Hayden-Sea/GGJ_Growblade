using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>连续剑身渲染：每条 BladeSegment 一个 LineRenderer（文档 11.3）。</summary>
    public class SwordView : MonoBehaviour
    {
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private readonly List<LineRenderer> _outlines = new List<LineRenderer>();
        private readonly List<BladeSegment> _segments = new List<BladeSegment>();
        private GameConfig _cfg;
        private Coroutine _flashRoutine;

        public int SegmentCount => _segments.Count;

        public void Rebuild(SwordState sword, GameConfig cfg)
        {
            _cfg = cfg;
            _segments.Clear();
            _segments.AddRange(SwordGeometry.GetSegments(sword));

            while (_lines.Count < _segments.Count)
            {
                var go = new GameObject($"Blade_{_lines.Count}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.sharedMaterial = GardenTheme.LineMaterial;
                lr.numCornerVertices = 3;
                lr.numCapVertices = 4;
                lr.sortingOrder = 10;
                _lines.Add(lr);
                var inkGo = new GameObject($"BladeInk_{_outlines.Count}");
                inkGo.transform.SetParent(transform, false);
                var ink = inkGo.AddComponent<LineRenderer>();
                ink.useWorldSpace = false;
                ink.sharedMaterial = GardenTheme.LineMaterial;
                ink.numCapVertices = 6;
                ink.sortingOrder = 9;
                ink.startColor = ink.endColor = GardenTheme.Ink;
                _outlines.Add(ink);
            }
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].gameObject.SetActive(i < _segments.Count);
                _outlines[i].gameObject.SetActive(i < _segments.Count);
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                var lr = _lines[i];
                var seg = _segments[i];
                lr.positionCount = 2;
                var a = SwordGeometry.NodeToLocal(seg.aHalf);
                var b = SwordGeometry.NodeToLocal(seg.bHalf);
                lr.SetPosition(0, new Vector3(a.x, a.y, 0f));
                lr.SetPosition(1, new Vector3(b.x, b.y, 0f));
                lr.startWidth = cfg.bladeVisualWidth;
                lr.endWidth = cfg.bladeVisualWidth;
                lr.startColor = cfg.bladeColor;
                lr.endColor = cfg.bladeColor;
                lr.sortingOrder = 10;
                var ink = _outlines[i];
                ink.positionCount = 2;
                ink.SetPosition(0, lr.GetPosition(0));
                ink.SetPosition(1, lr.GetPosition(1));
                ink.startWidth = ink.endWidth = cfg.bladeVisualWidth + .04f;
            }
        }

        public void SetColor(Color c)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                _lines[i].startColor = c;
                _lines[i].endColor = c;
            }
        }

        /// <summary>指定剑段闪白（文档 7.4）。</summary>
        public void FlashSegment(int index, float duration)
        {
            if (index < 0 || index >= _lines.Count || !_lines[index].gameObject.activeSelf)
                return;
            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(index, duration));
        }

        private IEnumerator FlashRoutine(int index, float duration)
        {
            var lr = _lines[index];
            Color original = _cfg != null ? _cfg.bladeColor : Color.white;
            Color flash = _cfg != null ? _cfg.bladeFlashColor : Color.white;
            float t = 0f;
            while (t < duration)
            {
                float k = 1f - t / Mathf.Max(0.01f, duration);
                lr.startColor = Color.Lerp(original, flash, k);
                lr.endColor = Color.Lerp(original, flash, k);
                t += Time.deltaTime;
                yield return null;
            }
            lr.startColor = original;
            lr.endColor = original;
            _flashRoutine = null;
        }

        /// <summary>采样世界空间剑段（轨迹预览用）。</summary>
        public static void SampleWorldSegments(
            List<BladeSegment> segments, Vector2 pivot, float angleRad,
            Vector2[] outA, Vector2[] outB, int count)
        {
            float c = Mathf.Cos(angleRad);
            float s = Mathf.Sin(angleRad);
            for (int i = 0; i < count; i++)
            {
                var a = SwordGeometry.NodeToLocal(segments[i].aHalf);
                var b = SwordGeometry.NodeToLocal(segments[i].bHalf);
                outA[i] = pivot + new Vector2(a.x * c - a.y * s, a.x * s + a.y * c);
                outB[i] = pivot + new Vector2(b.x * c - b.y * s, b.x * s + b.y * c);
            }
        }
    }
}
