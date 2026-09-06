using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace SwordGame.Editor
{
    /// <summary>Import the original doodle atlas and apply only presentation fields. Safe to rerun.</summary>
    public static class GardenArtSetup
    {
        public const string AtlasPath = "Assets/_Game/Art/Garden/Resources/DoodleAtlas.png";
        private const string ConfigPath = "Assets/_Game/Data/Resources/CFG_Game_Default.asset";
        private const string BackupPath = "Assets/_Game/Art/Garden/CFG_PreGarden.asset";

        [MenuItem("Tools/剑会变长/Apply Garden Art")]
        public static void Apply()
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException("Missing Garden/Resources/DoodleAtlas.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100;
            importer.SaveAndReimport();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var pixels = tex.GetPixels32();
            var names = new[] { "Knight", "Charger", "Archer", "Core", "Fruit", "Portal", "Grass", "Flower" };
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects().ToDictionary(s => s.name);
            var slices = new List<SpriteRect>();
            for (int i = 0; i < names.Length; i++)
            {
                int col = i % 4, row = i / 4;
                int x0 = col * tex.width / 4, x1 = (col + 1) * tex.width / 4;
                int y0 = tex.height - (row + 1) * tex.height / 2, y1 = tex.height - row * tex.height / 2;
                int minX = x1, minY = y1, maxX = x0, maxY = y0;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (pixels[y * tex.width + x].a < 24) continue;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                }
                if (maxX <= minX || maxY <= minY) throw new System.InvalidOperationException("Empty sprite cell: " + names[i]);
                minX = Mathf.Max(x0, minX - 3); minY = Mathf.Max(y0, minY - 3);
                maxX = Mathf.Min(x1 - 1, maxX + 3); maxY = Mathf.Min(y1 - 1, maxY + 3);
                slices.Add(new SpriteRect { name = names[i], rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), pivot = new Vector2(.5f, .5f), alignment = SpriteAlignment.Center, spriteID = previous.TryGetValue(names[i], out var old) ? old.spriteID : GUID.Generate() });
            }
            // Existing slice names make asset references stable on subsequent imports.
            provider.SetSpriteRects(slices.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(slices.Select(s => new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            var icons = AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().ToDictionary(s => s.name);
            if (icons.Count != 8) throw new System.InvalidOperationException("Expected exactly eight garden sprites.");
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (cfg == null) throw new System.InvalidOperationException("Missing existing game config.");
            if (AssetDatabase.LoadAssetAtPath<GameConfig>(BackupPath) == null && !AssetDatabase.CopyAsset(ConfigPath, BackupPath))
                throw new System.InvalidOperationException("Cannot preserve original art config.");
            Undo.RecordObject(cfg, "Apply garden art");
            cfg.playerSprite = icons["Knight"]; cfg.chargerSprite = icons["Charger"];
            cfg.archerSprite = icons["Archer"]; cfg.coreSprite = icons["Core"];
            cfg.fruitSprite = icons["Fruit"]; cfg.exitSprite = icons["Portal"];
            cfg.uiFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Codely/Fonts/NotoSansSC-Regular SDF.asset");
            cfg.backgroundColor = GardenTheme.Paper;
            cfg.floorTint = cfg.wallTint = Color.white;
            cfg.bladeColor = GardenTheme.Cream; cfg.bladeFlashColor = GardenTheme.Gold;
            cfg.trailOutColor = GardenTheme.Alpha(GardenTheme.Ink, .34f);
            cfg.trailReturnColor = GardenTheme.Alpha(GardenTheme.Moss, .22f);
            cfg.intentArrowColor = GardenTheme.Coral; cfg.intentRayColor = GardenTheme.Alpha(GardenTheme.Coral, .65f);
            cfg.exitLitColor = Color.white; cfg.exitDimColor = new Color(.74f, .77f, .69f, .72f);
            cfg.hitSparkColor = GardenTheme.Gold; cfg.wallSparkColor = GardenTheme.Ink;
            EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssetIfDirty(cfg);
            Debug.Log("Garden art applied: 8 alpha sprites, Chinese font, unified palette. Gameplay fields unchanged.");
        }
    }
}
