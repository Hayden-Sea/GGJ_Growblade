using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>敌人意图视图：移动箭头 / 蓄力与射线 / 脉冲线 / 取消反馈（文档 8、7.5）。</summary>
    public class IntentView : MonoBehaviour
    {
        private SpriteRenderer _arrow;
        private LineRenderer _lineA;
        private LineRenderer _lineB;
        private GameConfig _cfg;

        public void Init(GameConfig cfg)
        {
            _cfg = cfg;
            var arrowGo = new GameObject("Arrow");
            arrowGo.transform.SetParent(transform, false);
            _arrow = arrowGo.AddComponent<SpriteRenderer>();
            _arrow.sprite = GardenTheme.Arrow();
            _arrow.sortingOrder = 4;

            _lineA = MakeLine("RayA");
            _lineB = MakeLine("RayB");
            HideAll();
        }

        private LineRenderer MakeLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            lr.widthMultiplier = 0.06f;
            lr.numCapVertices = 2;
            lr.sharedMaterial = GardenTheme.LineMaterial;
            lr.sortingOrder = 1;
            return lr;
        }

        public void HideAll()
        {
            if (_arrow != null) _arrow.enabled = false;
            _lineA.positionCount = 0;
            _lineB.positionCount = 0;
        }

        /// <summary>按意图刷新显示。cancelGlint 用于预览「意图将被取消」的置灰。</summary>
        public void Show(EnemyIntentSnapshot snap, BoardModel board, GameConfig cfg, bool cancelledByPreview)
        {
            var origin = BoardModel.CellCenter(snap.cell);
            var intent = snap.intent;
            Color arrowColor = cfg.intentArrowColor;
            Color rayColor = cfg.intentRayColor;
            if (cancelledByPreview)
            {
                arrowColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                rayColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            }

            switch (intent.kind)
            {
                case IntentKind.Move:
                    ShowArrow(origin, intent.direction, arrowColor);
                    HideLines();
                    break;
                case IntentKind.Aim:
                case IntentKind.ShootLine:
                    HideArrow();
                    ShowRay(origin, intent.direction, board, rayColor);
                    break;
                case IntentKind.CorePulse:
                    HideArrow();
                    var axis = intent.direction.x != 0 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
                    ShowTwoRays(origin, axis, board, rayColor);
                    break;
                default:
                    HideAll();
                    break;
            }
        }

        private void ShowArrow(Vector2 origin, Vector2Int dir, Color color)
        {
            if (dir == Vector2Int.zero)
            {
                HideArrow();
                return;
            }
            _arrow.enabled = true;
            _arrow.color = color;
            transform.position = origin;
            _arrow.transform.localPosition = new Vector3(dir.x * 0.50f, dir.y * 0.50f, 0f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _arrow.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            _arrow.transform.localScale = Vector3.one * (0.32f / _arrow.sprite.bounds.size.x);
        }

        private void HideArrow()
        {
            if (_arrow != null) _arrow.enabled = false;
        }

        private void ShowRay(Vector2 origin, Vector2Int dir, BoardModel board, Color color)
        {
            var cells = EnemyPlanner.CollectRay(WorldCellOf(origin), dir, board);
            SetLine(_lineA, origin, cells, color);
            _lineB.positionCount = 0;
        }

        private void ShowTwoRays(Vector2 origin, Vector2Int axis, BoardModel board, Color color)
        {
            var cellsA = EnemyPlanner.CollectRay(WorldCellOf(origin), axis, board);
            var cellsB = EnemyPlanner.CollectRay(WorldCellOf(origin), -axis, board);
            SetLine(_lineA, origin, cellsA, color);
            SetLine(_lineB, origin, cellsB, color);
        }

        private void HideLines()
        {
            _lineA.positionCount = 0;
            _lineB.positionCount = 0;
        }

        private static Vector2Int WorldCellOf(Vector2 center)
        {
            return new Vector2Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y));
        }

        private void SetLine(LineRenderer lr, Vector2 origin, List<Vector2Int> cells, Color color)
        {
            if (cells == null || cells.Count == 0)
            {
                lr.positionCount = 0;
                return;
            }
            lr.positionCount = cells.Count + 1;
            lr.SetPosition(0, origin);
            for (int i = 0; i < cells.Count; i++)
            {
                var c = BoardModel.CellCenter(cells[i]);
                lr.SetPosition(i + 1, new Vector3(c.x, c.y, 0f));
            }
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g, color.b, color.a * 0.6f);
        }
    }
}
