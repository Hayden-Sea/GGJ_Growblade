using System.Collections.Generic;
using System.IO;
using SwordGame;
using UnityEditor;
using UnityEngine;

namespace SwordGame.EditorTools
{
    /// <summary>关卡资产操作（文档 19.3）：新建 / 复制 / 保存 / 尺寸变更 / 列表排序。</summary>
    public static class LevelAssetOperations
    {
        public const string DefaultDir = "Assets/_Game/Data/Rooms";
        private const string RunDefPath = "Assets/_Game/Data/Resources/RUN_Default";

        public static RunDefinition LoadRunDef()
        {
            var run = AssetDatabase.LoadAssetAtPath<RunDefinition>(RunDefPath + ".asset");
            if (run != null)
                return run;
            run = AssetDatabase.LoadAssetAtPath<RunDefinition>("Assets/_Game/Data/Resources/RUN_Default.asset");
            return run;
        }

        public static List<RoomDefinition> LoadListLevels()
        {
            var result = new List<RoomDefinition>();
            var run = LoadRunDef();
            if (run == null)
                return result;
            foreach (var s in run.stages)
                if (s.room != null)
                    result.Add(s.room);
            return result;
        }

        public static List<RoomDefinition> ScanAllRooms()
        {
            var result = new List<RoomDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/_Game" }))
            {
                var room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (room != null)
                    result.Add(room);
            }
            return result;
        }

        public static RoomDefinition CreateNew()
        {
            Directory.CreateDirectory(DefaultDir);
            var path = AssetDatabase.GenerateUniqueAssetPath(DefaultDir + "/ROOM_New.asset");
            var room = ScriptableObject.CreateInstance<RoomDefinition>();
            room.roomId = Path.GetFileNameWithoutExtension(path);
            room.width = 13;
            room.height = 11;
            room.goalText = "前往出口，离开这里。";
            room.initialPlayerHp = 4;
            room.objective = RoomDefinition.LevelObjective.ReachExit;
            room.wallCells = new List<Vector2Int>();
            for (int x = 0; x < room.width; x++)
            {
                room.wallCells.Add(new Vector2Int(x, 0));
                room.wallCells.Add(new Vector2Int(x, room.height - 1));
            }
            for (int y = 0; y < room.height; y++)
            {
                room.wallCells.Add(new Vector2Int(0, y));
                room.wallCells.Add(new Vector2Int(room.width - 1, y));
            }
            room.playerSpawns = new List<SpawnCandidate> { new SpawnCandidate { cell = new Vector2Int(2, 2), facing = 0 } };
            room.exitCell = new Vector2Int(room.width - 3, room.height - 3);
            room.MarkMigrated();
            AssetDatabase.CreateAsset(room, path);
            AddToList(room);
            return room;
        }

        public static RoomDefinition Duplicate(RoomDefinition source)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            Directory.CreateDirectory(DefaultDir);
            var path = AssetDatabase.GenerateUniqueAssetPath(
                DefaultDir + "/" + source.name + "_copy.asset");
            if (!AssetDatabase.CopyAsset(sourcePath, path))
                return null;
            var copy = AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
            copy.roomId = Path.GetFileNameWithoutExtension(path) + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 4);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            return copy;
        }

        public static bool Save(RoomDefinition room)
        {
            if (room == null)
                return false;
            EditorUtility.SetDirty(room);
            AssetDatabase.SaveAssetIfDirty(room);
            return true;
        }

        /// <summary>改尺寸：返回将越界而被清理的对象描述；确认后执行清理（文档 19.3）。</summary>
        public static List<string> ApplyResize(RoomDefinition room, int newW, int newH, out int removed)
        {
            var impact = new List<string>();
            removed = 0;
            for (int i = room.fruitSpawns.Count - 1; i >= 0; i--)
                if (room.fruitSpawns[i].cell.x >= newW || room.fruitSpawns[i].cell.y >= newH)
                {
                    impact.Add($"果 {room.fruitSpawns[i].fruitId} @ {room.fruitSpawns[i].cell}");
                    room.fruitSpawns.RemoveAt(i);
                }
            for (int i = room.enemySpawns.Count - 1; i >= 0; i--)
                if (room.enemySpawns[i].cell.x >= newW || room.enemySpawns[i].cell.y >= newH)
                {
                    impact.Add($"敌人 {room.enemySpawns[i].kind} @ {room.enemySpawns[i].cell}");
                    room.enemySpawns.RemoveAt(i);
                }
            for (int i = room.boxSpawns.Count - 1; i >= 0; i--)
                if (room.boxSpawns[i].x >= newW || room.boxSpawns[i].y >= newH)
                { impact.Add($"箱子 @ {room.boxSpawns[i]}"); room.boxSpawns.RemoveAt(i); }
            for (int i = room.pressurePlateCells.Count - 1; i >= 0; i--)
                if (room.pressurePlateCells[i].x >= newW || room.pressurePlateCells[i].y >= newH)
                { impact.Add($"机关 @ {room.pressurePlateCells[i]}"); room.pressurePlateCells.RemoveAt(i); }
            for (int i = room.playerSpawns.Count - 1; i >= 0; i--)
                if (room.playerSpawns[i].cell.x >= newW || room.playerSpawns[i].cell.y >= newH)
                {
                    impact.Add($"主出生 @ {room.playerSpawns[i].cell}（需重新放置）");
                    room.playerSpawns.RemoveAt(i);
                }
            if (room.hasCore && (room.coreCell.x >= newW || room.coreCell.y >= newH))
            {
                impact.Add($"核心 @ {room.coreCell}");
                room.hasCore = false;
                room.coreCell = new Vector2Int(-1, -1);
            }
            if (room.exitCell.x >= newW || room.exitCell.y >= newH)
            {
                impact.Add($"出口 @ {room.exitCell}");
                room.exitCell = new Vector2Int(-1, -1);
            }
            var walls = new List<Vector2Int>(room.wallCells);
            room.wallCells = walls.FindAll(c => c.x < newW && c.y < newH);
            removed = impact.Count;
            room.width = newW;
            room.height = newH;
            // 重建边界墙
            room.wallCells.RemoveAll(c => c.x == 0 || c.y == 0 || c.x == room.width - 1 || c.y == room.height - 1);
            for (int x = 0; x < room.width; x++)
            {
                room.wallCells.Add(new Vector2Int(x, 0));
                room.wallCells.Add(new Vector2Int(x, room.height - 1));
            }
            for (int y = 0; y < room.height; y++)
            {
                room.wallCells.Add(new Vector2Int(0, y));
                room.wallCells.Add(new Vector2Int(room.width - 1, y));
            }
            return impact;
        }

        public static void AddToList(RoomDefinition room)
        {
            var run = LoadRunDef();
            if (run == null)
                return;
            foreach (var s in run.stages)
                if (s.room == room)
                    return;
            var stages = new List<RunStage>(run.stages) { new RunStage { room = room } };
            run.stages = stages;
            EditorUtility.SetDirty(run);
            AssetDatabase.SaveAssets();
        }

        public static void MoveInList(RoomDefinition room, int delta)
        {
            var run = LoadRunDef();
            if (run == null)
                return;
            var stages = new List<RunStage>(run.stages);
            int i = stages.FindIndex(s => s.room == room);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= stages.Count)
                return;
            (stages[i], stages[j]) = (stages[j], stages[i]);
            run.stages = stages;
            EditorUtility.SetDirty(run);
            AssetDatabase.SaveAssets();
        }
    }
}
