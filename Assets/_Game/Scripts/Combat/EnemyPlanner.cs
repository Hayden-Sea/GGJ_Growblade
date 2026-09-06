using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>生成和执行敌人意图（文档 8.3/8.4/8.5/4.4）。固定 actorId 顺序，确定性结果。</summary>
    public static class EnemyPlanner
    {
        public static void PlanNextIntents(GameState state, BoardModel board, GameConfig cfg)
        {
            foreach (var enemy in state.enemies)
            {
                enemy.interruptedThisBeat = false;
                if (enemy.hp <= 0)
                    continue;
                switch (enemy.kind)
                {
                    case EnemyKind.Charger:
                        enemy.intent = PlanCharger(enemy, state, board, cfg);
                        break;
                    case EnemyKind.Archer:
                        enemy.intent = PlanArcher(enemy, state, board);
                        break;
                    case EnemyKind.Core:
                        enemy.intent = PlanCore(enemy);
                        break;
                    case EnemyKind.Box:
                        enemy.intent = EnemyIntent.Wait(false);
                        break;
                }
            }
        }

        private static EnemyIntent PlanCharger(EnemyState enemy, GameState state, BoardModel board, GameConfig cfg)
        {
            if (TryFindShortestStep(enemy, state, board, cfg, out var dir))
                return new EnemyIntent { kind = IntentKind.Move, targetCell = enemy.cell + dir, direction = dir, interruptible = true };
            return EnemyIntent.Wait();
        }

        /// <summary>BFS over legal one-cell sweeps. Sword segments are continuous capsule obstacles, not occupied grid cells.</summary>
        private static bool TryFindShortestStep(EnemyState enemy, GameState state, BoardModel board, GameConfig cfg, out Vector2Int firstStep)
        {
            firstStep = Vector2Int.zero;
            Vector2Int start = enemy.cell;
            Vector2Int goal = state.player.cell;
            if (start == goal)
                return false;

            var queue = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int> { start };
            var firstByCell = new Dictionary<Vector2Int, Vector2Int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (var dir in OrderedDirections(cell, goal, enemy.actorId))
                {
                    Vector2Int next = cell + dir;
                    if (visited.Contains(next) || !CellFreeForEnemy(next, enemy, state, board))
                        continue;
                    if (MovementCollision.EnemyPathHitsSword(enemy.kind, cell, next, state, cfg))
                        continue;

                    visited.Add(next);
                    Vector2Int first = cell == start ? dir : firstByCell[cell];
                    firstByCell[next] = first;
                    if (next == goal)
                    {
                        firstStep = first;
                        return true;
                    }
                    queue.Enqueue(next);
                }
            }
            return false;
        }

        private static List<Vector2Int> OrderedDirections(Vector2Int cell, Vector2Int goal, int actorId)
        {
            var dirs = (actorId & 1) == 0
                ? new List<Vector2Int> { Vector2Int.up, Vector2Int.left, Vector2Int.down, Vector2Int.right }
                : new List<Vector2Int> { Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up };
            dirs.Sort((a, b) =>
            {
                int da = Mathf.Abs(goal.x - cell.x - a.x) + Mathf.Abs(goal.y - cell.y - a.y);
                int db = Mathf.Abs(goal.x - cell.x - b.x) + Mathf.Abs(goal.y - cell.y - b.y);
                if (da != db) return da.CompareTo(db);
                return DirectionRank(a, actorId).CompareTo(DirectionRank(b, actorId));
            });
            return dirs;
        }

        private static int DirectionRank(Vector2Int dir, int actorId)
        {
            Vector2Int[] order = (actorId & 1) == 0
                ? new[] { Vector2Int.up, Vector2Int.left, Vector2Int.down, Vector2Int.right }
                : new[] { Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up };
            for (int i = 0; i < order.Length; i++)
                if (order[i] == dir) return i;
            return order.Length;
        }

        private static bool CellFreeForEnemy(Vector2Int cell, EnemyState self, GameState state, BoardModel board)
        {
            if (!board.IsFloor(cell))
                return false;
            foreach (var e in state.enemies)
                if (e.hp > 0 && e.actorId != self.actorId && e.cell == cell)
                    return false;
            return true;
        }

        private static int Sign(int v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        private static EnemyIntent PlanArcher(EnemyState enemy, GameState state, BoardModel board)
        {
            // Aim → ShootLine → Cooldown → Aim 循环；冷却拍不显示或执行射线。
            if (enemy.intent.kind == IntentKind.Aim)
            {
                return new EnemyIntent { kind = IntentKind.ShootLine, direction = enemy.intent.direction, interruptible = true };
            }
            if (enemy.intent.kind == IntentKind.ShootLine)
            {
                return new EnemyIntent { kind = IntentKind.Cooldown, direction = enemy.intent.direction, interruptible = true };
            }

            Vector2Int delta = state.player.cell - enemy.cell;
            int absX = Mathf.Abs(delta.x);
            int absY = Mathf.Abs(delta.y);
            Vector2Int dir = absY > absX
                ? new Vector2Int(0, Sign(delta.y))
                : new Vector2Int(Sign(delta.x), 0); // 相等时水平优先
            if (dir == Vector2Int.zero)
                dir = Vector2Int.right;
            return new EnemyIntent { kind = IntentKind.Aim, direction = dir, targetCell = enemy.cell + dir, interruptible = true };
        }

        private static EnemyIntent PlanCore(EnemyState core)
        {
            // 水平/竖直交替脉冲；direction 记录脉冲轴
            bool nextHorizontal = core.intent.kind != IntentKind.CorePulse || core.intent.direction.x == 0;
            return new EnemyIntent
            {
                kind = IntentKind.CorePulse,
                direction = nextHorizontal ? new Vector2Int(1, 0) : new Vector2Int(0, 1),
                interruptible = false,
            };
        }

        // ---- 执行（玩家动作之后） ----

        public static void ExecuteIntents(GameState state, BoardModel board, GameConfig cfg, ActionPresentation p)
        {
            // 固定 actorId 顺序（列表按插入序即 actorId 升序构造；核心 actorId 最大）
            var order = new List<EnemyState>(state.enemies);
            order.Sort((a, b) => a.actorId.CompareTo(b.actorId));

            foreach (var enemy in order)
            {
                if (enemy.hp <= 0 || enemy.interruptedThisBeat)
                    continue;
                var intent = enemy.intent;
                var evt = new EnemyEvent { actorId = enemy.actorId, fromCell = enemy.cell, toCell = enemy.cell, direction = intent.direction };
                bool damagePlayer = false;

                switch (intent.kind)
                {
                    case IntentKind.Move:
                    {
                        var target = intent.targetCell;
                        if (MovementCollision.EnemyPathHitsSword(enemy, target, state, cfg))
                        {
                            evt.kind = EnemyEventKind.Blocked;
                        }
                        else if (target == state.player.cell)
                        {
                            evt.kind = EnemyEventKind.AttackMove;
                            damagePlayer = true;
                        }
                        else if (CellOccupiedByEnemy(target, state) || !board.IsFloor(target))
                        {
                            evt.kind = EnemyEventKind.Blocked;
                        }
                        else
                        {
                            enemy.cell = target;
                            evt.kind = EnemyEventKind.Move;
                            evt.toCell = target;
                        }
                        break;
                    }
                    case IntentKind.ShootLine:
                    {
                        var cells = CollectRay(enemy.cell, intent.direction, board);
                        evt.kind = EnemyEventKind.ShootLine;
                        evt.rayCells = cells;
                        if (cells.Contains(state.player.cell))
                            damagePlayer = true;
                        break;
                    }
                    case IntentKind.CorePulse:
                    {
                        // 沿脉冲轴向双向射线（截断于墙）
                        var axis = intent.direction.x != 0 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
                        var cells = new List<Vector2Int>();
                        cells.AddRange(CollectRay(enemy.cell, axis, board));
                        cells.AddRange(CollectRay(enemy.cell, -axis, board));
                        evt.kind = EnemyEventKind.CorePulse;
                        evt.rayCells = cells;
                        if (cells.Contains(state.player.cell))
                            damagePlayer = true;
                        break;
                    }
                    case IntentKind.Aim:
                        evt.kind = EnemyEventKind.Aim;
                        break;
                    default:
                        evt.kind = EnemyEventKind.Wait;
                        break;
                }

                if (damagePlayer)
                {
                    state.player.hp -= cfg.enemyDamage;
                    evt.playerDamaged = true;
                }
                p.enemyEvents.Add(evt);

                if (state.player.hp <= 0)
                    return; // 玩家死亡，停止剩余敌人事件
            }
        }

        private static bool CellOccupiedByEnemy(Vector2Int cell, GameState state)
        {
            foreach (var e in state.enemies)
                if (e.hp > 0 && e.cell == cell)
                    return true;
            return false;
        }

        /// <summary>从 origin 沿 dir 的射线格（不含 origin），被墙截断。</summary>
        public static List<Vector2Int> CollectRay(Vector2Int origin, Vector2Int dir, BoardModel board)
        {
            var cells = new List<Vector2Int>(8);
            if (dir == Vector2Int.zero)
                return cells;
            var c = origin + dir;
            while (board.IsFloor(c))
            {
                cells.Add(c);
                c += dir;
            }
            return cells;
        }
    }
}
