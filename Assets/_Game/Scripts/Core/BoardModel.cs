using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>墙体、边界与格心转换（文档 3.3 / 9.3）。逻辑来源为墙格集合，不跨组件查询。</summary>
    public sealed class BoardModel
    {
        public readonly int Width;
        public readonly int Height;
        public readonly HashSet<Vector2Int> Walls = new HashSet<Vector2Int>();

        public BoardModel(int width, int height, IEnumerable<Vector2Int> wallCells)
        {
            Width = width;
            Height = height;
            if (wallCells != null)
                foreach (var c in wallCells)
                    if (c.x >= 0 && c.x < width && c.y >= 0 && c.y < height)
                        Walls.Add(c);
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.x < Width && c.y >= 0 && c.y < Height;

        public bool IsWall(Vector2Int c) => !InBounds(c) || Walls.Contains(c);

        public bool IsFloor(Vector2Int c) => InBounds(c) && !Walls.Contains(c);

        public Rect WallRect(Vector2Int c) => new Rect(c.x, c.y, 1f, 1f);

        public static Vector2 CellCenter(Vector2Int c) => new Vector2(c.x + 0.5f, c.y + 0.5f);

        /// <summary>遍历所有墙格 AABB（首版房间小，直接遍历；调用方可先按包围盒过滤）。</summary>
        public IEnumerable<Rect> AllWallRects()
        {
            foreach (var c in Walls)
                yield return WallRect(c);
        }
    }
}
