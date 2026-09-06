using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>出生选择：合法候选 + 几何可达性双重验证（文档 5.5 / 9.5）。忽略敌人，仅验证几何。</summary>
    public static class SpawnSelector
    {
        public struct Result
        {
            public SpawnCandidate spawn;
            public bool reachable;
            public string failReason;
            public int visitedStates;
        }

        public static bool TryPickSpawn(
            BoardModel board, IList<SpawnCandidate> candidates, SwordState sword, IList<EnemySpawnDef> enemies,
            Vector2Int? coreCell, GameConfig cfg, RoomDefinition room, out SpawnCandidate spawn, out string failReason, out int visited)
        {
            var segments = SwordGeometry.GetSegments(sword);
            string firstValidReason = null;
            SpawnCandidate firstValid = default;
            bool hasFirstValid = false;
            visited = 0;

            foreach (var cand in candidates)
            {
                if (!SpawnValidator.TryPickSpawn(board, new List<SpawnCandidate> { cand }, sword, enemies, coreCell, cfg,
                        out spawn, out string reason))
                {
                    if (firstValidReason == null)
                        firstValidReason = reason;
                    continue;
                }
                if (!hasFirstValid)
                {
                    firstValid = cand;
                    hasFirstValid = true;
                }

                var bfs = Reachable(board, cfg, sword, cand, room, coreCell);
                visited += bfs.visited;
                if (bfs.reachable)
                {
                    spawn = cand;
                    failReason = null;
                    return true;
                }
                if (firstValidReason == null)
                    firstValidReason = $"从 {cand.cell}/{cand.facing} 无法{bfs.goalInfo}（访问 {bfs.visited} 态）";
            }

            // 没有可达候选：回退到首个合法候选并显式报错（不允许静默困死）
            if (hasFirstValid)
            {
                Debug.LogError($"[SpawnSelector] 房间 {room.roomId} 剑形 {SpawnValidator.Describe(sword)} 无几何可达出生，回退首个合法候选 {firstValid.cell}/{firstValid.facing}：{firstValidReason}");
                spawn = firstValid;
                failReason = null;
                return true;
            }

            spawn = default;
            failReason = firstValidReason ?? "全部候选失败";
            return false;
        }

        public struct BfsResult
        {
            public bool reachable;
            public int visited;
            public string goalInfo;
        }

        /// <summary>有限状态搜索（状态 = (格, 朝向)，边 = 四向合法平移 + 两个完整 90° 旋转）。</summary>
        public static BfsResult Reachable(BoardModel board, GameConfig cfg, SwordState sword,
            SpawnCandidate start, RoomDefinition room, Vector2Int? coreCell)
        {
            var result = new BfsResult { goalInfo = "到达出口" };
            var solver = new TrajectorySolver(cfg);

            bool IsGoal(Vector2Int cell, int facing)
            {
                return cell == room.exitCell;
            }

            var state = new GameState
            {
                sessionId = 0,
                player = new PlayerState { cell = start.cell, facing = start.facing },
                sword = sword.Clone(),
                enemies = new List<EnemyState>(),
            };

            var seen = new HashSet<long>();
            var queue = new Queue<(Vector2Int cell, int facing)>();
            void Enqueue(Vector2Int cell, int facing)
            {
                long key = cell.x * 10000L + cell.y * 10L + facing;
                if (seen.Add(key))
                {
                    if (IsGoal(cell, facing))
                        result.reachable = true;
                    queue.Enqueue((cell, facing));
                }
            }

            Enqueue(start.cell, start.facing);
            while (queue.Count > 0 && !result.reachable)
            {
                var (cell, facing) = queue.Dequeue();
                result.visited++;

                foreach (var dir in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.down, Vector2Int.right })
                {
                    var next = cell + dir;
                    if (!board.IsFloor(next) || seen.Contains(next.x * 10000L + next.y * 10L + facing))
                        continue;
                    var actionState = WithPlayer(state, cell, facing);
                    if (solver.BodyPathHitsWall(cell, next, board))
                        continue;
                    if (solver.FindFirstWallContact(actionState, board, PlayerAction.Move(dir)).hasContact)
                        continue;
                    Enqueue(next, facing);
                }

                foreach (var turn in new[] { 1, -1 })
                {
                    int nf = ((facing + turn) % 4 + 4) % 4;
                    if (seen.Contains(cell.x * 10000L + cell.y * 10L + nf))
                        continue;
                    var actionState = WithPlayer(state, cell, facing);
                    var wall = solver.FindFirstWallContact(actionState, board, PlayerAction.Rotate(turn));
                    if (wall.hasContact)
                        continue; // 回弹旋转不产生新状态（文档 9.5）
                    Enqueue(cell, nf);
                }
            }
            return result;
        }

        private static GameState WithPlayer(GameState probe, Vector2Int cell, int facing)
        {
            if (probe == null)
                return null;
            var clone = probe.Clone();
            clone.player.cell = cell;
            clone.player.facing = facing;
            return clone;
        }
    }
}
