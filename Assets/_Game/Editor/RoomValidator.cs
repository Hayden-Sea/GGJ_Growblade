using System.Collections.Generic;
using System.Linq;
using SwordGame;
using UnityEditor;
using UnityEngine;

namespace SwordGame.EditorTools
{
    /// <summary>关卡校验（文档 19.4）：快速校验 = 硬错误（阻止试玩）+ 设计警告；
    /// 深度检查 = 初始短剑静态可达性（显式按钮，忽略敌人战斗与动态成长）。
    /// v1.3 不再按“所有成长剑形跨关继承”的旧口径验证。</summary>
    public static class RoomValidator
    {
        private const string MenuRoot = "Tools/剑会变长/";

        public struct Issue
        {
            public string severity;   // Error / Warning / Info
            public string code;
            public string message;
            public Vector2Int? cell;
        }

        [MenuItem(MenuRoot + "Validate All Rooms")]
        public static void ValidateAllRooms()
        {
            var guids = AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/_Game" });
            if (guids.Length == 0)
            {
                Debug.LogWarning("[RoomValidator] 未找到 RoomDefinition 资产");
                return;
            }

            var cfg = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Game/Data/Resources/CFG_Game_Default.asset");
            if (cfg == null)
                cfg = ScriptableObject.CreateInstance<GameConfig>();

            int errorCount = 0;
            foreach (var guid in guids)
            {
                var room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (room == null)
                    continue;
                var issues = QuickValidate(room, cfg);
                bool hasError = issues.Exists(i => i.severity == "Error");
                if (hasError)
                    errorCount++;
                Debug.Log($"[RoomValidator] {room.roomId}: {(hasError ? "FAIL" : "PASS")}（{issues.Count} 项）\n    " +
                          string.Join("\n    ", issues.ConvertAll(i => $"[{i.severity}] {i.code}: {i.message}" + (i.cell.HasValue ? $" @ {i.cell.Value}" : "")).ToArray()));
            }
            Debug.Log(errorCount == 0
                ? "[RoomValidator] 全部房间通过"
                : $"[RoomValidator] {errorCount} 个房间存在硬错误");
        }

        [MenuItem(MenuRoot + "Migrate Rooms To v1.3 Schema")]
        public static void MigrateAllRooms()
        {
            var guids = AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/_Game" });
            int migrated = 0;
            foreach (var guid in guids)
            {
                var room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (room == null || room.SchemaVersion >= 2)
                    continue;
                room.MarkMigrated(); // 旧核心关由 EffectiveObjective 统一解释为“清除敌人”
                EditorUtility.SetDirty(room);
                migrated++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[RoomValidator] 已迁移 {migrated} 个房间资产到 schemaVersion=2");
        }

        /// <summary>快速校验：硬错误 + 设计警告（文档 19.4）。</summary>
        public static List<Issue> QuickValidate(RoomDefinition room, GameConfig cfg)
        {
            var issues = new List<Issue>();
            var board = room.BuildBoard();
            var sword = SwordState.CreateInitial();

            if (room.width < 2 || room.width > 31 || room.height < 2 || room.height > 31)
                issues.Add(new Issue { severity = "Error", code = "SIZE", message = $"尺寸 {room.width}x{room.height} 超出支持范围 2–31" });

            // 边界闭合
            for (int x = 0; x < room.width; x++)
                foreach (var y in new[] { 0, room.height - 1 })
                    if (!board.IsWall(new Vector2Int(x, y)))
                        issues.Add(new Issue { severity = "Error", code = "BOUNDARY", message = "边界未闭合", cell = new Vector2Int(x, y) });
            for (int y = 0; y < room.height; y++)
                foreach (var x in new[] { 0, room.width - 1 })
                    if (!board.IsWall(new Vector2Int(x, y)))
                        issues.Add(new Issue { severity = "Error", code = "BOUNDARY", message = "边界未闭合", cell = new Vector2Int(x, y) });

            // 主出生
            if (room.playerSpawns.Count == 0)
                issues.Add(new Issue { severity = "Error", code = "SPAWN", message = "缺少主出生点" });
            else
            {
                bool anyValid = false;
                foreach (var cand in room.playerSpawns)
                {
                    var blockers = new List<EnemySpawnDef>(room.enemySpawns);
                    foreach (var box in room.boxSpawns) blockers.Add(new EnemySpawnDef { cell = box, kind = EnemyKind.Box });
                    if (SpawnValidator.TryPickSpawn(board, new List<SpawnCandidate> { cand }, sword, blockers,
                            room.hasCore ? room.coreCell : (Vector2Int?)null, cfg, out _, out var reason))
                    {
                        anyValid = true;
                        break;
                    }
                    issues.Add(new Issue { severity = "Warning", code = "SPAWN_INVALID", message = "出生候选不可用：" + reason, cell = cand.cell });
                }
                if (!anyValid && room.playerSpawns.Count > 0)
                    issues.Add(new Issue { severity = "Error", code = "SPAWN_ALL_INVALID", message = "全部出生候选对初始短剑不合法" });
            }

            // 目标
            if (!board.IsFloor(room.exitCell))
                issues.Add(new Issue { severity = "Error", code = "EXIT_INVALID", message = "出口不在地板上", cell = room.exitCell });
            if (room.RequiresPlates)
            {
                if (room.boxSpawns.Count == 0 || room.pressurePlateCells.Count == 0)
                    issues.Add(new Issue { severity = "Error", code = "BOX_OBJECTIVE_MISSING", message = "箱子机关关至少需要一个箱子和一个机关" });
                if (room.boxSpawns.Count != room.pressurePlateCells.Count)
                    issues.Add(new Issue { severity = "Error", code = "BOX_PLATE_COUNT", message = $"箱子 {room.boxSpawns.Count} 个，但机关 {room.pressurePlateCells.Count} 个" });
            }

            // 果：普通果给 1 点；多生长果给对应点数且必须连续用完。
            int fruitBudget = room.fruitSpawns.Sum(f => Mathf.Max(1, f.growthValue));
            if (room.fruitSpawns.Count > 0)
                issues.Add(new Issue { severity = "Info", code = "FRUIT_BUDGET", message = $"本关 {room.fruitSpawns.Count} 个果，最多提供 {fruitBudget} 次生长" });
            var ids = new HashSet<string>();
            foreach (var f in room.fruitSpawns)
            {
                if (string.IsNullOrEmpty(f.fruitId))
                    issues.Add(new Issue { severity = "Error", code = "FRUIT_ID", message = "fruitId 为空", cell = f.cell });
                else if (!ids.Add(f.fruitId))
                    issues.Add(new Issue { severity = "Error", code = "FRUIT_DUP", message = $"fruitId 重复：{f.fruitId}", cell = f.cell });
                if (!board.IsFloor(f.cell))
                    issues.Add(new Issue { severity = "Error", code = "FRUIT_WALL", message = "果在墙上", cell = f.cell });
                if (f.growthValue < 0 || f.growthValue > 9)
                    issues.Add(new Issue { severity = "Error", code = "FRUIT_VALUE", message = "果的成长次数必须为 1–9", cell = f.cell });
            }
            if (room.fruitSpawns.Count == 0)
                issues.Add(new Issue { severity = "Warning", code = "NO_FRUIT", message = "本关没有果：无法成长（基线测试关除外）" });

            // 敌人
            if (room.RequiresEnemyClear && room.enemySpawns.FindAll(e => e.kind != EnemyKind.Core).Count == 0 && !room.hasCore)
                issues.Add(new Issue { severity = "Warning", code = "NO_ENEMY", message = "清敌目标中没有敌人或核心" });
            if (room.enemySpawns.Count > 6)
                issues.Add(new Issue { severity = "Error", code = "ENEMY_COUNT", message = $"敌人数 {room.enemySpawns.Count} 超过上限 6" });
            var enemyCells = new HashSet<Vector2Int>();
            foreach (var e in room.enemySpawns)
            {
                if (!board.IsFloor(e.cell))
                    issues.Add(new Issue { severity = "Error", code = "ENEMY_WALL", message = "敌人在墙上", cell = e.cell });
                else if (!enemyCells.Add(e.cell))
                    issues.Add(new Issue { severity = "Error", code = "ENEMY_DUP", message = "敌人同格重叠", cell = e.cell });
            }

            var boxCells = new HashSet<Vector2Int>();
            foreach (var box in room.boxSpawns)
            {
                if (!board.IsFloor(box)) issues.Add(new Issue { severity = "Error", code = "BOX_WALL", message = "箱子在墙上", cell = box });
                else if (!boxCells.Add(box)) issues.Add(new Issue { severity = "Error", code = "BOX_DUP", message = "箱子同格重叠", cell = box });
                if (enemyCells.Contains(box)) issues.Add(new Issue { severity = "Error", code = "OVERLAP", message = "箱子与敌人同格", cell = box });
            }
            var plateCells = new HashSet<Vector2Int>();
            foreach (var plate in room.pressurePlateCells)
            {
                if (!board.IsFloor(plate)) issues.Add(new Issue { severity = "Error", code = "PLATE_WALL", message = "机关在墙上", cell = plate });
                else if (!plateCells.Add(plate)) issues.Add(new Issue { severity = "Error", code = "PLATE_DUP", message = "机关同格重叠", cell = plate });
            }

            // 同格冲突：果 vs 敌人 / 出生 / 核心 / 出口
            foreach (var f in room.fruitSpawns)
            {
                foreach (var e in room.enemySpawns)
                    if (e.cell == f.cell)
                        issues.Add(new Issue { severity = "Error", code = "OVERLAP", message = "果与敌人同格", cell = f.cell });
                if (room.hasCore && room.coreCell == f.cell)
                    issues.Add(new Issue { severity = "Error", code = "OVERLAP", message = "果与核心同格", cell = f.cell });
            }

            // 设计警告：静态可达性（初始短剑，忽略敌人）
            var reachabilityBlockers = new List<EnemySpawnDef>(room.enemySpawns);
            foreach (var box in room.boxSpawns) reachabilityBlockers.Add(new EnemySpawnDef { cell = box, kind = EnemyKind.Box });
            if (room.playerSpawns.Count > 0 && SpawnSelector.TryPickSpawn(board, room.playerSpawns, sword, reachabilityBlockers,
                    room.hasCore ? room.coreCell : (Vector2Int?)null, cfg, room, out var spawn, out _, out _))
            {
                var bfs = SpawnSelector.Reachable(board, cfg, sword, spawn, room, room.hasCore ? room.coreCell : (Vector2Int?)null);
                if (!bfs.reachable)
                    issues.Add(new Issue
                    {
                        severity = "Warning",
                        code = "UNREACHABLE",
                        message = $"初始短剑静态无法{bfs.goalInfo}（忽略敌人与成长；访问 {bfs.visited} 态）",
                    });
                // 果的静态可触达（身体可达或剑可及）
                foreach (var f in room.fruitSpawns)
                {
                    var fRoom = ScriptableObject.CreateInstance<RoomDefinition>();
                    fRoom.width = room.width; fRoom.height = room.height;
                    fRoom.wallCells = room.wallCells; fRoom.exitCell = f.cell; // 借用出口字段做身体可达检查
                    var body = SpawnSelector.Reachable(board, cfg, sword, spawn, fRoom, null);
                    if (!body.reachable)
                        issues.Add(new Issue { severity = "Warning", code = "FRUIT_FAR", message = "果无法被身体到达（剑或许可及）", cell = f.cell });
                    Object.DestroyImmediate(fRoom);
                }
            }
            else if (room.playerSpawns.Count > 0)
            {
                issues.Add(new Issue { severity = "Error", code = "SPAWN_BLOCK", message = "出生点不合法，无法进行可达性检查" });
            }

            if (room.SchemaVersion < 2)
                issues.Add(new Issue { severity = "Warning", code = "SCHEMA", message = "旧 schema 资产：建议执行迁移（目标类型 / 果配置显式化）" });

            return issues;
        }
    }
}
