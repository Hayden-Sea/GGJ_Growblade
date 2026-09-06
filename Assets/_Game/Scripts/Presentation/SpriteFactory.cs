using UnityEngine;

namespace SwordGame
{
    /// <summary>运行时生成占位/装饰用精灵（圆、方、三角、心形、菱形火花、叉）。</summary>
    public static class SpriteFactory
    {
        private static Sprite _circle;
        private static Sprite _softCircle;
        private static Sprite _triangle;
        private static Sprite _heart;
        private static Sprite _diamond;
        private static Sprite _cross;

        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            _circle = Build(64, (x, y, c) =>
            {
                float dx = x - c, dy = y - c;
                return dx * dx + dy * dy <= 900 ? (byte)255 : (byte)0; // 半径 30
            });
            return _circle;
        }

        public static Sprite SoftCircle()
        {
            if (_softCircle != null) return _softCircle;
            _softCircle = Build(64, (x, y, c) =>
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if (d <= 26f) return (byte)255;
                return (byte)Mathf.Clamp(255 - (d - 26f) / 6f * 255, 0, 255);
            });
            return _softCircle;
        }

        public static Sprite Triangle()
        {
            if (_triangle != null) return _triangle;
            _triangle = Build(64, (x, y, c) =>
            {
                // 指向 +X 的等腰三角形
                float fx = x - 8f;
                float half = fx * 0.5f;
                if (fx < 0 || half < 0.5f) return (byte)0;
                return Mathf.Abs(y - c) <= half ? (byte)255 : (byte)0;
            });
            return _triangle;
        }

        public static Sprite Heart()
        {
            if (_heart != null) return _heart;
            _heart = Build(64, (x, y, c) =>
            {
                float nx = (x - c) / 26f;
                float ny = (c - y) / 26f + 0.25f;
                float a = nx * nx + ny * ny - 0.55f;
                return a < 0f ? (byte)255 : (byte)0;
            });
            return _heart;
        }

        public static Sprite Diamond()
        {
            if (_diamond != null) return _diamond;
            _diamond = Build(64, (x, y, c) =>
                Mathf.Abs(x - c) + Mathf.Abs(y - c) <= 28 ? (byte)255 : (byte)0);
            return _diamond;
        }

        public static Sprite Cross()
        {
            if (_cross != null) return _cross;
            _cross = Build(64, (x, y, c) =>
            {
                bool bar1 = Mathf.Abs(x - c) <= 6 && Mathf.Abs(y - c) <= 24;
                bool bar2 = Mathf.Abs(y - c) <= 6 && Mathf.Abs(x - c) <= 24;
                return bar1 || bar2 ? (byte)255 : (byte)0;
            });
            return _cross;
        }

        private static Sprite Build(int size, System.Func<int, int, int, byte> alphaAt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RuntimeSprite";
            int c = size / 2;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, alphaAt(x, y, c));
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        }
    }
}
