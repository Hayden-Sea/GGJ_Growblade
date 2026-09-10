using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>Flat garden board. Wall/floor cells and actor coordinates still come from the existing board model.</summary>
    public class RoomView : MonoBehaviour
    {
        private SpriteRenderer _exitMarker;
        private GameConfig _cfg;
        public void Build(RoomDefinition room, BoardModel board, GameConfig cfg)
        {
            Clear(); _cfg = cfg;
            var center = new Vector2(room.width * .5f, room.height * .5f);
            Backplate(center + new Vector2(.09f, -.13f), room.width + .22f, room.height + .22f, GardenTheme.Alpha(GardenTheme.Ink, .16f), -14);
            Backplate(center, room.width + .14f, room.height + .14f, GardenTheme.Ink, -13);
            for (int y = 0; y < room.height; y++)
            for (int x = 0; x < room.width; x++)
            {
                var cell = new Vector2Int(x, y);
                bool wall = board.IsWall(cell);
                var sr = MakeSprite(GardenTheme.Tile(wall, (x + y * 3 + x * y) % 3), BoardModel.CellCenter(cell), 1f, Color.white, wall ? -5 : -10);
                sr.name = wall ? "Hedge_" + x + "_" + y : "Floor_" + x + "_" + y;
            }
            foreach (var plate in room.pressurePlateCells)
            {
                var sr = MakeSprite(cfg.pressurePlateSprite != null ? cfg.pressurePlateSprite : GardenTheme.Plate(GardenTheme.Gold, false),
                    BoardModel.CellCenter(plate), .92f, Color.white, -3);
                sr.name = "PressurePlate_" + plate.x + "_" + plate.y;
            }
            _exitMarker = MakeSprite(cfg.exitSprite != null ? cfg.exitSprite : GardenTheme.Icon(5), BoardModel.CellCenter(room.exitCell), .68f, cfg.exitDimColor, -4);
            _exitMarker.name = "Exit";
            _exitMarker.gameObject.SetActive(!room.RequiresPlates);
            // Decoration is outside the board and owns no collider.
            Decor(7, new Vector2(-.66f, 1.2f), .5f);
            Decor(6, new Vector2(room.width + .62f, room.height - 2f), .5f);
            Decor(6, new Vector2(.5f, room.height + .57f), .34f);
            Decor(7, new Vector2(room.width - .35f, -.60f), .35f);
        }
        private void Decor(int icon, Vector2 pos, float size) => MakeSprite(GardenTheme.Icon(icon), pos, size, GardenTheme.Alpha(Color.white, .8f), -12);
        private void Backplate(Vector2 center, float width, float height, Color fill, int order)
        {
            var go = new GameObject("BoardPaper"); go.transform.SetParent(transform, false); go.transform.position = center;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = GardenTheme.Plate(fill, false);
            sr.drawMode = SpriteDrawMode.Sliced; sr.size = new Vector2(width, height); sr.sortingOrder = order;
        }
        private SpriteRenderer MakeSprite(Sprite sprite, Vector2 center, float size, Color tint, int order)
        {
            var go = new GameObject("Cell"); go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.color = tint; sr.sortingOrder = order;
            float baseSize = sprite.bounds.size.x;
            go.transform.localScale = Vector3.one * (size / Mathf.Max(.001f, baseSize));
            go.transform.position = center; return sr;
        }
        public void SetExitLit(bool lit)
        {
            if (_exitMarker == null) return;
            _exitMarker.color = lit ? Color.white : _cfg.exitDimColor;
        }
        public void SetExitVisible(bool visible)
        {
            if (_exitMarker != null) _exitMarker.gameObject.SetActive(visible);
        }
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _exitMarker = null;
        }
    }
}
