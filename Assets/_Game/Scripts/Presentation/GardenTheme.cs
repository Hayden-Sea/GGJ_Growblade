using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>Shared garden-sketch palette and small code-native UI primitives. No gameplay geometry.</summary>
    public static class GardenTheme
    {
        public static readonly Color Ink = Hex(0x293F38);
        public static readonly Color Paper = Hex(0xF4ECD5);
        public static readonly Color Cream = Hex(0xFFF8E8);
        public static readonly Color Sage = Hex(0xA6B997);
        public static readonly Color Moss = Hex(0x648A71);
        public static readonly Color Gold = Hex(0xE9AF46);
        public static readonly Color Coral = Hex(0xD77361);
        public static readonly Color Plum = Hex(0x8B789A);
        public static readonly Color Muted = Hex(0x738171);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly string[] IconNames = { "Knight", "Charger", "Archer", "Core", "Fruit", "Portal", "Grass", "Flower" };
        private static Sprite[] _atlas;
        private static Material _lineMaterial;

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        public static Color Alpha(Color c, float alpha) { c.a = alpha; return c; }
        public static Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null) _lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Garden ink" };
                return _lineMaterial;
            }
        }

        public static Sprite Icon(int index)
        {
            if (_atlas == null || _atlas.Length == 0) _atlas = Resources.LoadAll<Sprite>("DoodleAtlas");
            if (index >= 0 && index < IconNames.Length)
                foreach (var sprite in _atlas)
                    if (sprite.name == IconNames[index]) return sprite;
            return SpriteFactory.Circle();
        }

        public static Sprite Plate(Color fill, bool outline = true)
        {
            string key = "plate" + ColorUtility.ToHtmlStringRGBA(fill) + outline;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            const int size = 96;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - 47.5f) - 32f;
                float dy = Mathf.Abs(y - 47.5f) - 32f;
                float d = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0) - 12f;
                d += 0.32f * Mathf.Sin(x * 0.21f + y * 0.17f);
                var c = outline && d > -2.6f ? Ink : fill;
                c.a *= Mathf.Clamp01(0.7f - d);
                pixels[y * size + x] = c;
            }
            return Save(key, size, size, pixels, new Vector4(18, 18, 18, 18));
        }

        public static Sprite Tile(bool wall, int variant = 0)
        {
            string key = "tile" + wall;
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            const int n = 128;
            var pixels = new Color32[n * n];
            Color fill = wall ? Hex(0x66876A) : Hex(0xC2CDA9);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color c = fill;
                if (wall)
                {
                    if (x < 3 || x > 124 || y < 3 || y > 124) c = Ink;
                    else if (y < 14) c = Hex(0x4F7059);
                    else if (y > 117) c = Hex(0x89A17C);
                }
                else
                {
                    // One consistent ground color, with a quiet inset grid shadow.
                    if (x < 3 || y < 3) c = Color.Lerp(fill, Moss, 0.13f);
                    else if (x < 6 || y < 6) c = Color.Lerp(fill, Moss, 0.045f);
                }
                pixels[y * n + x] = c;
            }
            return Save(key, n, n, pixels, Vector4.zero);
        }

        public static Sprite Heart()
        {
            if (Sprites.TryGetValue("heart", out var cached) && cached != null) return cached;
            const int n = 96;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = (x - 47.5f) / 40f, py = (y - 46f) / 40f;
                bool inside = InHeart(px, py);
                bool inner = InHeart(px / 0.86f, py / 0.86f);
                pixels[y * n + x] = inside ? (inner ? Coral : Ink) : Color.clear;
            }
            return Save("heart", n, n, pixels, Vector4.zero);
        }
        private static bool InHeart(float x, float y)
        {
            float k = x * x + y * y - 0.72f;
            return k * k * k - x * x * y * y * y <= 0;
        }

        /// <summary>An outlined arrow pointing along local +X; shared by enemy and growth previews.</summary>
        public static Sprite Arrow()
        {
            if (Sprites.TryGetValue("arrow", out var cached) && cached != null) return cached;
            const int n = 96;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float py = Mathf.Abs(y - 47.5f);
                bool body = x >= 10 && x <= 57 && py <= 10;
                bool tip = x >= 46 && x <= 86 && py <= (86 - x) * 0.75f;
                pixels[y * n + x] = body || tip ? Color.white : Color.clear;
            }
            return Save("arrow", n, n, pixels, Vector4.zero);
        }

        public static Sprite Ring()
        {
            if (Sprites.TryGetValue("ring", out var cached) && cached != null) return cached;
            const int n = 96;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float radius = new Vector2(x - 47.5f, y - 47.5f).magnitude;
                float a = Mathf.Clamp01(2.4f - Mathf.Abs(radius - 40));
                pixels[y * n + x] = new Color(1, 1, 1, a);
            }
            return Save("ring", n, n, pixels, Vector4.zero);
        }

        private static Sprite Save(string key, int w, int h, Color32[] pixels, Vector4 border)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Garden_" + key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = tex.name;
            Sprites[key] = sprite;
            return sprite;
        }
    }
}
