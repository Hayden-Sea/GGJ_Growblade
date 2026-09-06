using System.Linq;
using UnityEngine;

namespace SwordGame.EditorTools
{
    public enum LevelCellObject { None, Wall, Player, Enemy, Fruit, Exit, Core, Box, PressurePlate }

    /// <summary>Pure RoomDefinition cell edits shared by the window and editor regression checks.</summary>
    public static class LevelCellEditing
    {
        public static LevelCellObject Find(RoomDefinition room, Vector2Int cell)
        {
            if (room == null) return LevelCellObject.None;
            if (room.enemySpawns.Any(e => e.cell == cell)) return LevelCellObject.Enemy;
            if (room.fruitSpawns.Any(f => f.cell == cell)) return LevelCellObject.Fruit;
            if (room.playerSpawns.Any(p => p.cell == cell)) return LevelCellObject.Player;
            if (room.boxSpawns.Contains(cell)) return LevelCellObject.Box;
            if (room.hasCore && room.coreCell == cell) return LevelCellObject.Core;
            if (room.exitCell == cell) return LevelCellObject.Exit;
            if (room.pressurePlateCells.Contains(cell)) return LevelCellObject.PressurePlate;
            if (room.wallCells.Contains(cell)) return LevelCellObject.Wall;
            return LevelCellObject.None;
        }

        public static bool IsBoundary(RoomDefinition room, Vector2Int cell) =>
            cell.x == 0 || cell.y == 0 || cell.x == room.width - 1 || cell.y == room.height - 1;

        public static bool Delete(RoomDefinition room, LevelCellObject kind, Vector2Int cell, out string reason)
        {
            reason = null;
            switch (kind)
            {
                case LevelCellObject.Enemy:
                    return room.enemySpawns.RemoveAll(e => e.cell == cell) > 0;
                case LevelCellObject.Fruit:
                    return room.fruitSpawns.RemoveAll(f => f.cell == cell) > 0;
                case LevelCellObject.Player:
                    return room.playerSpawns.RemoveAll(p => p.cell == cell) > 0;
                case LevelCellObject.Box:
                    return room.boxSpawns.Remove(cell);
                case LevelCellObject.PressurePlate:
                    return room.pressurePlateCells.Remove(cell);
                case LevelCellObject.Core:
                    if (!room.hasCore || room.coreCell != cell) return false;
                    room.hasCore = false;
                    room.coreCell = new Vector2Int(-1, -1);
                    return true;
                case LevelCellObject.Exit:
                    if (room.exitCell != cell) return false;
                    room.exitCell = new Vector2Int(-1, -1);
                    return true;
                case LevelCellObject.Wall:
                    if (IsBoundary(room, cell))
                    {
                        reason = "固定边界墙不可删除";
                        return false;
                    }
                    return room.wallCells.Remove(cell);
                default:
                    reason = "该格没有可删除对象";
                    return false;
            }
        }

        public static bool CanPlace(RoomDefinition room, LevelCellObject kind, Vector2Int cell, LevelCellObject ignore, Vector2Int ignoreCell, out string reason)
            => CanPlace(room, kind, cell, ignore, ignoreCell, 0, out reason);

        public static bool CanPlace(RoomDefinition room, LevelCellObject kind, Vector2Int cell, LevelCellObject ignore,
            Vector2Int ignoreCell, int playerFacing, out string reason)
        {
            reason = null;
            if (cell.x < 0 || cell.x >= room.width || cell.y < 0 || cell.y >= room.height)
            {
                reason = "超出地图";
                return false;
            }
            if (kind == LevelCellObject.Wall)
            {
                if (IsReservedByInitialSword(room, cell, ignore, ignoreCell))
                {
                    reason = "该格被初始剑身占用";
                    return false;
                }
                if (FindIgnoring(room, cell, ignore, ignoreCell) != LevelCellObject.None)
                {
                    reason = "该格已有实体";
                    return false;
                }
                return true;
            }
            if (room.wallCells.Contains(cell))
            {
                reason = "该格是墙";
                return false;
            }
            var occupied = FindIgnoring(room, cell, ignore, ignoreCell);
            if (occupied != LevelCellObject.None)
            {
                reason = "该格已有" + Name(occupied);
                return false;
            }
            if (kind != LevelCellObject.Player && IsReservedByInitialSword(room, cell, ignore, ignoreCell))
            {
                reason = "该格被初始剑身占用";
                return false;
            }
            if (kind == LevelCellObject.Player)
            {
                var swordCell = cell + FacingOffset(playerFacing);
                if (swordCell.x < 0 || swordCell.x >= room.width || swordCell.y < 0 || swordCell.y >= room.height || room.wallCells.Contains(swordCell))
                {
                    reason = "该朝向的初始剑身不在可用地面";
                    return false;
                }
                var swordOccupied = FindIgnoring(room, swordCell, ignore, ignoreCell);
                if (swordOccupied != LevelCellObject.None)
                {
                    reason = "初始剑身格已有" + Name(swordOccupied);
                    return false;
                }
            }
            return true;
        }

        public static bool TryMove(RoomDefinition room, LevelCellObject kind, Vector2Int from, Vector2Int to, out string reason)
        {
            reason = null;
            if (from == to) return true;
            if (kind == LevelCellObject.Wall || kind == LevelCellObject.None)
            {
                reason = "该对象不能移动";
                return false;
            }
            int facing = 0;
            if (kind == LevelCellObject.Player)
            {
                int playerIndex = room.playerSpawns.FindIndex(p => p.cell == from);
                if (playerIndex >= 0) facing = room.playerSpawns[playerIndex].facing;
            }
            if (!CanPlace(room, kind, to, kind, from, facing, out reason)) return false;
            switch (kind)
            {
                case LevelCellObject.Enemy:
                    for (int i = 0; i < room.enemySpawns.Count; i++)
                        if (room.enemySpawns[i].cell == from) { var v = room.enemySpawns[i]; v.cell = to; room.enemySpawns[i] = v; return true; }
                    break;
                case LevelCellObject.Fruit:
                    for (int i = 0; i < room.fruitSpawns.Count; i++)
                        if (room.fruitSpawns[i].cell == from) { var v = room.fruitSpawns[i]; v.cell = to; room.fruitSpawns[i] = v; return true; }
                    break;
                case LevelCellObject.Player:
                    for (int i = 0; i < room.playerSpawns.Count; i++)
                        if (room.playerSpawns[i].cell == from) { var v = room.playerSpawns[i]; v.cell = to; room.playerSpawns[i] = v; return true; }
                    break;
                case LevelCellObject.Box:
                    if (room.boxSpawns.Remove(from)) { room.boxSpawns.Add(to); return true; }
                    break;
                case LevelCellObject.PressurePlate:
                    if (room.pressurePlateCells.Remove(from)) { room.pressurePlateCells.Add(to); return true; }
                    break;
                case LevelCellObject.Exit:
                    if (room.exitCell == from) { room.exitCell = to; return true; }
                    break;
                case LevelCellObject.Core:
                    if (room.hasCore && room.coreCell == from) { room.coreCell = to; return true; }
                    break;
            }
            reason = "原位置没有对应对象";
            return false;
        }

        public static bool SetEnemyKind(RoomDefinition room, Vector2Int cell, EnemyKind kind)
        {
            for (int i = 0; i < room.enemySpawns.Count; i++)
                if (room.enemySpawns[i].cell == cell)
                {
                    var v = room.enemySpawns[i];
                    if (v.kind == kind) return false;
                    v.kind = kind;
                    room.enemySpawns[i] = v;
                    return true;
                }
            return false;
        }

        public static bool TrySetPlayerFacing(RoomDefinition room, Vector2Int cell, int facing, out string reason)
        {
            int index = room.playerSpawns.FindIndex(p => p.cell == cell);
            if (index < 0) { reason = "该格没有玩家"; return false; }
            facing = (facing % 4 + 4) % 4;
            if (!CanPlace(room, LevelCellObject.Player, cell, LevelCellObject.Player, cell, facing, out reason))
                return false;
            var player = room.playerSpawns[index];
            player.facing = facing;
            room.playerSpawns[index] = player;
            return true;
        }

        public static bool SetFruitId(RoomDefinition room, Vector2Int cell, string id, out string reason)
        {
            reason = null;
            id = id == null ? "" : id.Trim();
            if (string.IsNullOrEmpty(id)) { reason = "fruitId 不能为空"; return false; }
            for (int i = 0; i < room.fruitSpawns.Count; i++)
                if (room.fruitSpawns[i].cell != cell && room.fruitSpawns[i].fruitId == id)
                { reason = "fruitId 必须在本关唯一"; return false; }
            for (int i = 0; i < room.fruitSpawns.Count; i++)
                if (room.fruitSpawns[i].cell == cell)
                {
                    var v = room.fruitSpawns[i];
                    if (v.fruitId == id) return false;
                    v.fruitId = id;
                    room.fruitSpawns[i] = v;
                    return true;
                }
            reason = "该格没有果";
            return false;
        }

        public static bool SetFruitGrowthValue(RoomDefinition room, Vector2Int cell, int value, out string reason)
        {
            reason = null;
            value = Mathf.Clamp(value, 1, 9);
            for (int i = 0; i < room.fruitSpawns.Count; i++)
            {
                if (room.fruitSpawns[i].cell != cell) continue;
                var fruit = room.fruitSpawns[i];
                if (Mathf.Max(1, fruit.growthValue) == value) return false;
                fruit.growthValue = value;
                room.fruitSpawns[i] = fruit;
                return true;
            }
            reason = "该格没有果";
            return false;
        }

        public static string Name(LevelCellObject kind)
        {
            switch (kind)
            {
                case LevelCellObject.Wall: return "墙";
                case LevelCellObject.Player: return "玩家";
                case LevelCellObject.Enemy: return "怪物";
                case LevelCellObject.Fruit: return "果";
                case LevelCellObject.Exit: return "出口";
                case LevelCellObject.Core: return "核心";
                case LevelCellObject.Box: return "箱子";
                case LevelCellObject.PressurePlate: return "机关";
                default: return "对象";
            }
        }

        private static LevelCellObject FindIgnoring(RoomDefinition room, Vector2Int cell, LevelCellObject ignore, Vector2Int ignoreCell)
        {
            var found = Find(room, cell);
            return cell == ignoreCell && found == ignore ? LevelCellObject.None : found;
        }

        public static Vector2Int FacingOffset(int facing)
        {
            switch ((facing % 4 + 4) % 4)
            {
                case 1: return Vector2Int.up;
                case 2: return Vector2Int.left;
                case 3: return Vector2Int.down;
                default: return Vector2Int.right;
            }
        }

        public static Vector2Int InitialSwordCell(SpawnCandidate spawn) => spawn.cell + FacingOffset(spawn.facing);

        private static bool IsReservedByInitialSword(RoomDefinition room, Vector2Int cell, LevelCellObject ignore, Vector2Int ignoreCell)
        {
            foreach (var spawn in room.playerSpawns)
            {
                if (ignore == LevelCellObject.Player && spawn.cell == ignoreCell) continue;
                if (InitialSwordCell(spawn) == cell) return true;
            }
            return false;
        }
    }
}
