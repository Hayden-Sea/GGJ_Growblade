using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SwordGame.Tests
{
    public class LevelEditorEditingTests
    {
        private RoomDefinition room;

        [SetUp]
        public void Setup()
        {
            room = ScriptableObject.CreateInstance<RoomDefinition>();
            room.width = 7; room.height = 7; room.MarkMigrated();
            room.wallCells.Clear();
            for (int x = 0; x < 7; x++) { room.wallCells.Add(new Vector2Int(x,0)); room.wallCells.Add(new Vector2Int(x,6)); }
            for (int y = 1; y < 6; y++) { room.wallCells.Add(new Vector2Int(0,y)); room.wallCells.Add(new Vector2Int(6,y)); }
            room.playerSpawns.Add(new SpawnCandidate { cell = new Vector2Int(1,1), facing = 0 });
            room.exitCell = new Vector2Int(5,5);
            room.enemySpawns.Add(new EnemySpawnDef { cell = new Vector2Int(3,2), kind = EnemyKind.Charger });
            room.fruitSpawns.Add(new RoomDefinition.FruitSpawnDef { cell = new Vector2Int(2,2), fruitId = "fruit_a" });
            room.fruitSpawns.Add(new RoomDefinition.FruitSpawnDef { cell = new Vector2Int(2,3), fruitId = "fruit_b" });
        }

        [TearDown]
        public void Cleanup()
        {
            if (room != null) Object.DestroyImmediate(room);
        }

        [Test]
        public void ExistingFruitAndEnemyCanBeDeletedIndependently()
        {
            Assert.IsTrue(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.Fruit, new Vector2Int(2,2), out _));
            Assert.AreEqual(1, room.fruitSpawns.Count);
            Assert.AreEqual("fruit_b", room.fruitSpawns[0].fruitId);
            Assert.AreEqual(1, room.enemySpawns.Count);

            Assert.IsTrue(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.Enemy, new Vector2Int(3,2), out _));
            Assert.AreEqual(0, room.enemySpawns.Count);
        }

        [Test]
        public void BoxAndPressurePlateCanBePlacedMovedAndDeleted()
        {
            room.boxSpawns.Add(new Vector2Int(4, 2));
            room.pressurePlateCells.Add(new Vector2Int(4, 4));
            Assert.AreEqual(EditorTools.LevelCellObject.Box, EditorTools.LevelCellEditing.Find(room, new Vector2Int(4, 2)));
            Assert.AreEqual(EditorTools.LevelCellObject.PressurePlate, EditorTools.LevelCellEditing.Find(room, new Vector2Int(4, 4)));
            Assert.IsTrue(EditorTools.LevelCellEditing.TryMove(room, EditorTools.LevelCellObject.Box,
                new Vector2Int(4, 2), new Vector2Int(4, 3), out _));
            Assert.IsTrue(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.PressurePlate,
                new Vector2Int(4, 4), out _));
            CollectionAssert.Contains(room.boxSpawns, new Vector2Int(4, 3));
            Assert.AreEqual(0, room.pressurePlateCells.Count);
        }

        [Test]
        public void ExistingEntityCanMoveButCannotOverwriteAnotherObject()
        {
            Assert.IsTrue(EditorTools.LevelCellEditing.TryMove(room, EditorTools.LevelCellObject.Enemy,
                new Vector2Int(3,2), new Vector2Int(4,2), out _));
            Assert.AreEqual(new Vector2Int(4,2), room.enemySpawns[0].cell);

            Assert.IsFalse(EditorTools.LevelCellEditing.TryMove(room, EditorTools.LevelCellObject.Enemy,
                new Vector2Int(4,2), new Vector2Int(2,3), out var reason));
            StringAssert.Contains("已有", reason);
            Assert.AreEqual(new Vector2Int(4,2), room.enemySpawns[0].cell);
        }

        [Test]
        public void ExistingEnemyTypeAndFruitIdCanBeEdited()
        {
            Assert.IsTrue(EditorTools.LevelCellEditing.SetEnemyKind(room, new Vector2Int(3,2), EnemyKind.Archer));
            Assert.AreEqual(EnemyKind.Archer, room.enemySpawns[0].kind);
            Assert.IsTrue(EditorTools.LevelCellEditing.SetFruitId(room, new Vector2Int(2,2), "golden", out _));
            Assert.AreEqual("golden", room.fruitSpawns[0].fruitId);
            Assert.IsFalse(EditorTools.LevelCellEditing.SetFruitId(room, new Vector2Int(2,2), "fruit_b", out var reason));
            StringAssert.Contains("唯一", reason);
        }

        [Test]
        public void BoundaryWallCannotBeDeletedButInteriorWallCan()
        {
            room.wallCells.Add(new Vector2Int(3,3));
            Assert.IsFalse(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.Wall, new Vector2Int(0,3), out var reason));
            StringAssert.Contains("边界", reason);
            Assert.IsTrue(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.Wall, new Vector2Int(3,3), out _));
        }

        [Test]
        public void FruitBrushDeletionMarksRoomDirtyAndRepaintsData()
        {
            var window = ScriptableObject.CreateInstance<EditorTools.LevelEditorWindow>();
            try
            {
                typeof(EditorTools.LevelEditorWindow).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, room);
                var toolField = typeof(EditorTools.LevelEditorWindow).GetField("_tool", BindingFlags.Instance | BindingFlags.NonPublic);
                toolField.SetValue(window, System.Enum.Parse(toolField.FieldType, "Fruit"));
                typeof(EditorTools.LevelEditorWindow).GetMethod("PaintAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { new Vector2Int(2,2), true });
                Assert.AreEqual(1, room.fruitSpawns.Count);
                Assert.IsTrue(EditorUtility.IsDirty(room), "删除后必须进入未保存状态");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectionFlowCanDeleteAnExistingEnemy()
        {
            var window = ScriptableObject.CreateInstance<EditorTools.LevelEditorWindow>();
            try
            {
                var type = typeof(EditorTools.LevelEditorWindow);
                type.GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, room);
                type.GetMethod("SelectAt", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(window, new object[] { new Vector2Int(3,2) });
                Assert.AreEqual(EditorTools.LevelCellObject.Enemy,
                    type.GetField("_selectedObject", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window));
                type.GetMethod("DeleteSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Assert.AreEqual(0, room.enemySpawns.Count);
                Assert.IsTrue(EditorUtility.IsDirty(room));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void DeleteAndUndoRestoresFruitData()
        {
            Undo.RecordObject(room, "Test fruit deletion");
            Assert.IsTrue(EditorTools.LevelCellEditing.Delete(room, EditorTools.LevelCellObject.Fruit, new Vector2Int(2,2), out _));
            Undo.FlushUndoRecordObjects();
            Assert.AreEqual(1, room.fruitSpawns.Count);
            Undo.PerformUndo();
            Assert.AreEqual(2, room.fruitSpawns.Count);
            Assert.AreEqual("fruit_a", room.fruitSpawns[0].fruitId);
            Undo.ClearUndo(room);
        }

        [Test]
        public void FruitBrushShowsLegalOnEmptyFloorAndOnExistingFruit()
        {
            var window = ScriptableObject.CreateInstance<EditorTools.LevelEditorWindow>();
            try
            {
                var type = typeof(EditorTools.LevelEditorWindow);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                type.GetField("_room", flags).SetValue(window, room);
                var tool = type.GetField("_tool", flags);
                tool.SetValue(window, System.Enum.Parse(tool.FieldType, "Fruit"));
                var legal = type.GetMethod("IsPlacementLegal", flags);
                Assert.IsTrue((bool)legal.Invoke(window, new object[] { new Vector2Int(4,4) }));
                Assert.IsTrue((bool)legal.Invoke(window, new object[] { new Vector2Int(2,2) }));
                Assert.IsFalse((bool)legal.Invoke(window, new object[] { new Vector2Int(0,2) }));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ResizeSmaller_RemovesOutOfBoundsEntitiesAndRebuildsBoundary()
        {
            room.playerSpawns.Add(new SpawnCandidate { cell = new Vector2Int(6, 5), facing = 0 });
            room.enemySpawns.Add(new EnemySpawnDef { cell = new Vector2Int(6, 4), kind = EnemyKind.Archer });
            room.fruitSpawns.Add(new RoomDefinition.FruitSpawnDef { cell = new Vector2Int(5, 5), fruitId = "edge_fruit" });
            room.hasCore = true;
            room.coreCell = new Vector2Int(6, 3);
            room.exitCell = new Vector2Int(6, 2);

            var impact = EditorTools.LevelAssetOperations.ApplyResize(room, 5, 5, out var removed);

            Assert.AreEqual(5, room.width);
            Assert.AreEqual(5, room.height);
            Assert.AreEqual(5, removed, "出生、敌人、果、核心和出口均应计入缩图影响");
            Assert.AreEqual(1, room.playerSpawns.Count, "保留范围内出生点不应受影响");
            Assert.AreEqual(1, room.enemySpawns.Count, "保留范围内敌人不应受影响");
            Assert.AreEqual(2, room.fruitSpawns.Count, "保留范围内果不应受影响");
            Assert.IsFalse(room.hasCore);
            Assert.AreEqual(new Vector2Int(-1, -1), room.exitCell);
            CollectionAssert.Contains(room.wallCells, new Vector2Int(4, 4));
        }

        [Test]
        public void MinimumEditableSize_2By2_IsNotRejectedBySizeValidation()
        {
            EditorTools.LevelAssetOperations.ApplyResize(room, 2, 2, out _);
            var cfg = ScriptableObject.CreateInstance<GameConfig>();
            try
            {
                var issues = EditorTools.RoomValidator.QuickValidate(room, cfg);
                Assert.IsFalse(issues.Exists(issue => issue.code == "SIZE"),
                    "2×2 应是可编辑尺寸；它是否可试玩由出生点、目标等其他校验决定。");
                Assert.AreEqual(2, room.width);
                Assert.AreEqual(2, room.height);
                CollectionAssert.Contains(room.wallCells, new Vector2Int(0, 0));
                CollectionAssert.Contains(room.wallCells, new Vector2Int(1, 1));
            }
            finally
            {
                Object.DestroyImmediate(cfg);
            }
        }

        [Test]
        public void InitialSword_ReservesOnlyOneCellAndFacingCanBeChanged()
        {
            room.playerSpawns.Clear();
            room.playerSpawns.Add(new SpawnCandidate { cell = new Vector2Int(2, 4), facing = 0 });

            Assert.AreEqual(new Vector2Int(3, 4), EditorTools.LevelCellEditing.InitialSwordCell(room.playerSpawns[0]));
            Assert.IsFalse(EditorTools.LevelCellEditing.CanPlace(room, EditorTools.LevelCellObject.Wall,
                new Vector2Int(3, 4), EditorTools.LevelCellObject.None, default, out var blockedReason));
            StringAssert.Contains("初始剑身", blockedReason);
            Assert.IsTrue(EditorTools.LevelCellEditing.CanPlace(room, EditorTools.LevelCellObject.Wall,
                new Vector2Int(4, 4), EditorTools.LevelCellObject.None, default, out _), "第二个前方格不再保留");

            Assert.IsTrue(EditorTools.LevelCellEditing.TrySetPlayerFacing(room, new Vector2Int(2, 4), 1, out _));
            Assert.AreEqual(new Vector2Int(2, 5), EditorTools.LevelCellEditing.InitialSwordCell(room.playerSpawns[0]));
        }

        [Test]
        public void PlayerFacing_RejectsSwordCellWithWall()
        {
            room.playerSpawns.Clear();
            room.playerSpawns.Add(new SpawnCandidate { cell = new Vector2Int(4, 3), facing = 0 });
            room.wallCells.Add(new Vector2Int(4, 4));

            Assert.IsFalse(EditorTools.LevelCellEditing.TrySetPlayerFacing(room, new Vector2Int(4, 3), 1, out var reason));
            StringAssert.Contains("初始剑身", reason);
            Assert.AreEqual(0, room.playerSpawns[0].facing);
        }
    }
}
