using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public enum GrowthSource { BodyPickup, SwordHit, StoredCredit }

    /// <summary>果 / 暂存点触发的成长请求（文档 10.3）。SwordHit 时必须已完成回弹。</summary>
    public sealed class GrowthRequest
    {
        public string fruitId;      // 暂存点恢复时为 null
        public GrowthSource source;
        public int credits;
    }

    /// <summary>玩家阶段结果：状态快照 + 表现事件 + 可选成长请求。敌人阶段由确认 / 暂存后恢复。</summary>
    public sealed class PlayerStageResult
    {
        public bool isValid;
        public ActionInvalidReason invalidReason;
        public GameState state;                 // 玩家阶段结束工作快照（beatIndex 已 +1，果 / 点数已记账）
        public ActionPresentation presentation;
        public bool hasGrowthRequest;
        public GrowthRequest growthRequest;
        public int beatId => state?.beatIndex ?? -1;
    }

    public sealed class EnemyStageResult
    {
        public GameState state;
        public ActionPresentation presentation;
    }

    /// <summary>分段拍事务模拟器（文档 4.7 / 10.4）：
    /// BeginPlayerAction 纯模拟玩家阶段；确认 / 暂存成长后 ResumeEnemyPhase 续完同一拍。</summary>
    public sealed class BeatSimulator
    {
        private readonly GameConfig cfg;
        private readonly TrajectorySolver solver;
        private readonly List<HitCandidate> candidateScratch = new List<HitCandidate>(8);
        private readonly List<HitCandidate> filteredScratch = new List<HitCandidate>(8);
        private readonly HashSet<Vector2Int> reservedScratch = new HashSet<Vector2Int>();

        public BeatSimulator(GameConfig config)
        {
            cfg = config;
            solver = new TrajectorySolver(config);
        }

        // ---- 玩家阶段 ----

        public PlayerStageResult BeginPlayerAction(GameState current, PlayerAction action, BoardModel board)
        {
            var working = current.Clone();
            var p = new ActionPresentation { action = action };

            var invalid = ValidateAction(working, action, board);
            if (invalid != ActionInvalidReason.None)
            {
                if (action.kind == PlayerActionKind.Move && invalid != ActionInvalidReason.TargetOccupied)
                {
                    // 碰撞平移与旋剑撞墙统一为“有效但归位”的一拍：播放出程/回弹，随后敌人照常行动。
                    p.isValid = true;
                    p.invalidReason = invalid;
                    p.playerFromCell = working.player.cell;
                    p.playerToCell = working.player.cell;
                    p.startFacing = p.endFacing = working.player.facing;
                    p.stopReason = StopReason.Wall;
                    p.rebound = true;
                    p.blockedMoveProgress = MovementCollision.BlockedMoveProgress(working, action, board, cfg);
                    p.stopU = p.blockedMoveProgress;
                    working.beatIndex++;
                    p.resultingBeatIndex = working.beatIndex;
                    return new PlayerStageResult { isValid = true, state = working, presentation = p };
                }
                return new PlayerStageResult
                {
                    isValid = false,
                    invalidReason = invalid,
                    state = current,
                    presentation = new ActionPresentation { isValid = false, invalidReason = invalid, action = action },
                };
            }

            ContactResult wall = ContactResult.None;
            var fruit = default(FruitContactResult);
            bool hasFruit = false;

            if (action.kind == PlayerActionKind.Rotate)
            {
                wall = solver.FindFirstWallContact(working, board, action);
                fruit = FindFirstFruitContact(working, action, board);
                hasFruit = fruit.has;
            }
            else if (action.kind == PlayerActionKind.Move)
            {
                // 完整路径墙验证已在 ValidateAction 完成；这里只找果
                fruit = FindFirstFruitContact(working, action, board);
                hasFruit = fruit.has;
            }

            // 截断裁决（文档 4.5）：较早者截断；误差内墙优先
            var stopReason = StopReason.None;
            float stopU = 1f;
            Vector2Int wallCell = Vector2Int.zero;
            int wallSeg = -1;
            Vector2 wallPoint = Vector2.zero;
            if (wall.hasContact)
            {
                stopReason = StopReason.Wall;
                stopU = Mathf.Clamp01(wall.u);
                wallCell = wall.wallCell;
                wallSeg = wall.segmentIndex;
                wallPoint = wall.contactPoint;
            }
            if (hasFruit && (stopReason == StopReason.None || fruit.u < stopU - cfg.contactTolerance))
            {
                stopReason = StopReason.Fruit;
                stopU = Mathf.Clamp01(fruit.u);
            }

            // 出程内敌人命中。旋剑的第一名接触敌人同墙体一样截断并回弹；
            // 平移仍保留前进攻击，维持“向前移动、剑身碰怪”的基础操作。
            solver.FindEnemyContacts(working, action, candidateScratch);
            int blockingEnemyId = -1;
            if (action.kind == PlayerActionKind.Rotate)
            {
                foreach (var c in candidateScratch)
                {
                    if (stopReason != StopReason.None && c.u >= stopU - cfg.contactTolerance)
                    {
                        // 同一接触进度有多个怪物时，以 actorId 保证结果不受列表顺序影响。
                        // 墙与果仍保持同误差范围内的既有优先级。
                        if (stopReason == StopReason.Enemy &&
                            Mathf.Abs(c.u - stopU) <= cfg.contactTolerance &&
                            c.actorId < blockingEnemyId)
                            blockingEnemyId = c.actorId;
                        continue;
                    }
                    if (stopReason == StopReason.None || c.u < stopU - cfg.contactTolerance ||
                        (Mathf.Abs(c.u - stopU) <= cfg.contactTolerance && c.actorId < blockingEnemyId))
                    {
                        stopReason = StopReason.Enemy;
                        stopU = Mathf.Clamp01(c.u);
                        blockingEnemyId = c.actorId;
                    }
                }
            }
            filteredScratch.Clear();
            foreach (var c in candidateScratch)
            {
                bool isBlockingEnemy = stopReason == StopReason.Enemy && c.actorId == blockingEnemyId;
                if (stopReason != StopReason.None && c.u >= stopU - cfg.contactTolerance && !isBlockingEnemy)
                    continue;
                filteredScratch.Add(c);
            }
            filteredScratch.Sort((a, b) =>
            {
                int byU = a.u.CompareTo(b.u);
                return byU != 0 ? byU : a.actorId.CompareTo(b.actorId);
            });

            reservedScratch.Clear();
            reservedScratch.Add(working.player.cell);
            if (action.kind == PlayerActionKind.Move)
                reservedScratch.Add(working.player.cell + action.moveDirection);
            bool ccw = action.quarterTurns > 0;

            foreach (var c in filteredScratch)
            {
                var enemy = FindEnemy(working, c.actorId);
                if (enemy == null || enemy.hp <= 0)
                    continue;

                var hit = new HitEvent
                {
                    actorId = enemy.actorId,
                    kind = enemy.kind,
                    u = c.u,
                    segmentIndex = c.segmentIndex,
                    contactPoint = c.contactPoint,
                    hpBefore = enemy.hp,
                    hpAfter = enemy.kind == EnemyKind.Box ? enemy.hp : enemy.hp - cfg.bladeDamage,
                    knockFrom = enemy.cell,
                    knockTo = enemy.cell,
                    isCore = enemy.kind == EnemyKind.Core,
                };
                enemy.hp = hit.hpAfter;
                if (enemy.kind != EnemyKind.Core && enemy.kind != EnemyKind.Box)
                    enemy.interruptedThisBeat = true;

                p.hits.Add(hit);

                if (hit.hpAfter > 0 && enemy.kind != EnemyKind.Core)
                {
                    Vector2Int dir;
                    if (action.kind == PlayerActionKind.Move)
                        dir = action.moveDirection;
                    else
                        dir = CombatRules.QuantizeTangent(
                            BoardModel.CellCenter(enemy.cell) - BoardModel.CellCenter(working.player.cell), ccw);
                    if (dir == Vector2Int.zero)
                    {
                        Debug.LogError($"[BeatSimulator] 非法占位：敌人 {enemy.actorId} 与玩家同格，无击退方向。");
                    }
                    else
                    {
                        CombatRules.ResolveKnockback(enemy, dir, board, cfg, working, reservedScratch, p, hit);
                    }
                }

                var resolved = p.hits[p.hits.Count - 1];
                if (resolved.died && resolved.wallImpact)
                    p.knockbackWallKills++;
            }

            // ---- 姿态提交与果触发 ----
            GrowthRequest growth = null;
            p.stopReason = stopReason; // 表现契约：墙 / 果截断原因
            p.stopU = stopU;
            p.playerFromCell = working.player.cell;
            p.startFacing = working.player.facing;
            p.endFacing = working.player.facing;
            p.playerToCell = working.player.cell;

            if (action.kind == PlayerActionKind.Move)
            {
                if (stopReason == StopReason.Fruit)
                {
                    // 平移砍果：人与剑整体退回出发格，格位与朝向保持动作开始值（文档 4.4）
                    p.rebound = true;
                    ConsumeFruit(working, fruit.fruitId);
                    growth = new GrowthRequest { fruitId = fruit.fruitId, source = GrowthSource.SwordHit, credits = working.growthCredits };
                }
                else
                {
                    working.player.cell += action.moveDirection;
                    p.playerToCell = working.player.cell;
                    // 身体拾取：合法到达果所在格（进度 1 事件）
                    var f = FindUnconsumedFruitAt(working, working.player.cell);
                    if (f != null)
                    {
                        ConsumeFruit(working, f.fruitId);
                        p.bodyPickup = true;
                        // 身体踏果是一次完整的合法移动：角色停在目标果格。
                        // 只有剑身在平移出程中先触果，才会截断并整体回弹。
                        p.fruitId = f.fruitId;
                        p.fruitContactPoint = BoardModel.CellCenter(f.cell);
                        growth = new GrowthRequest { fruitId = f.fruitId, source = GrowthSource.BodyPickup, credits = working.growthCredits };
                    }
                }
            }
            else if (action.kind == PlayerActionKind.Rotate)
            {
                p.rotateBlocked = stopReason != StopReason.None;
                p.wallU = (stopReason == StopReason.Wall || stopReason == StopReason.Enemy) ? stopU : 1f;
                if (stopReason != StopReason.None)
                {
                    p.rebound = true;
                    if (stopReason == StopReason.Fruit)
                    {
                        ConsumeFruit(working, fruit.fruitId);
                        growth = new GrowthRequest { fruitId = fruit.fruitId, source = GrowthSource.SwordHit, credits = working.growthCredits };
                    }
                    if (stopU <= cfg.contactTolerance)
                        p.isZeroSweep = true;
                }
                else
                {
                    working.player.facing = ((working.player.facing + action.quarterTurns) % 4 + 4) % 4;
                }
                p.endFacing = working.player.facing;
            }

            if (stopReason == StopReason.Wall || (action.kind == PlayerActionKind.Rotate && p.rotateBlocked))
            {
                p.wallSegmentIndex = wallSeg;
                p.wallCell = wallCell;
                p.wallContactPoint = wallPoint;
            }
            if (stopReason == StopReason.Fruit)
            {
                p.stopU = stopU;
                p.fruitId = fruit.fruitId;
                p.fruitContactPoint = fruit.contactPoint;
            }
            p.wallU = (stopReason == StopReason.Wall || stopReason == StopReason.Enemy) ? stopU : p.wallU;

            working.beatIndex += 1;
            p.resultingBeatIndex = working.beatIndex;
            p.isValid = true;

            if (growth != null)
                growth.credits = working.growthCredits;

            return new PlayerStageResult
            {
                isValid = true,
                state = working,
                presentation = p,
                hasGrowthRequest = growth != null,
                growthRequest = growth,
            };
        }

        // ---- 敌人阶段（确认 / 暂存后恢复同一拍）----

        public EnemyStageResult ResumeEnemyPhase(GameState working, BoardModel board, ActionPresentation p)
        {
            EnemyPlanner.ExecuteIntents(working, board, cfg, p);
            working.enemies.RemoveAll(e => e.hp <= 0);
            EnemyPlanner.PlanNextIntents(working, board, cfg);
            return new EnemyStageResult { state = working, presentation = p };
        }

        /// <summary>出程已击毁核心：先完成果 / 成长，再通关（R08）。</summary>
        public static bool CoreObjectiveCompleted(GameState state)
        {
            var core = state.GetCore();
            return core != null && core.hp <= 0;
        }

        public static void RemoveDeadCore(GameState state)
        {
            state.enemies.RemoveAll(e => e.kind == EnemyKind.Core && e.hp <= 0);
        }

        // ---- 内部 ----

        private struct FruitContactResult
        {
            public bool has;
            public float u;
            public string fruitId;
            public int segmentIndex;
            public Vector2 contactPoint;
        }

        private FruitContactResult FindFirstFruitContact(GameState working, PlayerAction action, BoardModel board)
        {
            var best = new FruitContactResult { has = false, u = float.MaxValue };
            foreach (var f in working.fruits)
            {
                if (f.consumed)
                    continue;
                float radius = cfg.bladeRadius + cfg.fruitRadius;
                if (solver.FindFirstCircleContact(working, action, BoardModel.CellCenter(f.cell), radius,
                        out float u, out int seg, out Vector2 pt))
                {
                    if (!best.has || u < best.u - cfg.contactTolerance ||
                        (Mathf.Abs(u - best.u) <= cfg.contactTolerance && string.CompareOrdinal(f.fruitId, best.fruitId) < 0))
                    {
                        best = new FruitContactResult { has = true, u = u, fruitId = f.fruitId, segmentIndex = seg, contactPoint = pt };
                    }
                }
            }
            return best;
        }

        private FruitState FindUnconsumedFruitAt(GameState state, Vector2Int cell)
        {
            foreach (var f in state.fruits)
                if (!f.consumed && f.cell == cell)
                    return f;
            return null;
        }

        private static void ConsumeFruit(GameState working, string fruitId)
        {
            foreach (var f in working.fruits)
            {
                if (!f.consumed && f.fruitId == fruitId)
                {
                    int value = f.ResolvedGrowthValue;
                    f.consumed = true;
                    working.growthCredits += value;
                    if (value >= 2)
                        working.mandatoryGrowthCredits += value;
                    return;
                }
            }
        }

        private ActionInvalidReason ValidateAction(GameState working, PlayerAction action, BoardModel board)
        {
            switch (action.kind)
            {
                case PlayerActionKind.Wait:
                case PlayerActionKind.Rotate:
                    return ActionInvalidReason.None;

                case PlayerActionKind.Move:
                    var target = working.player.cell + action.moveDirection;
                    if (!board.InBounds(target))
                        return ActionInvalidReason.OutsideBoard;
                    if (board.IsWall(target))
                        return ActionInvalidReason.BodyHitsWall;
                    foreach (var e in working.enemies)
                        if (e.hp > 0 && e.cell == target)
                            return ActionInvalidReason.TargetOccupied;
                    if (solver.BodyPathHitsWall(working.player.cell, target, board))
                        return ActionInvalidReason.BodyHitsWall;
                    // 完整平移路径剑碰墙则整次无效：即使墙前有果也不触发（文档 6.5）
                    var wall = solver.FindFirstWallContact(working, board, action);
                    if (wall.hasContact)
                        return ActionInvalidReason.BladeHitsWall;
                    return ActionInvalidReason.None;

                default:
                    return ActionInvalidReason.None;
            }
        }

        private EnemyState FindEnemy(GameState state, int actorId)
        {
            foreach (var e in state.enemies)
                if (e.actorId == actorId)
                    return e;
            return null;
        }

        private static PlayerStageResult InvalidResult(ActionInvalidReason reason, GameState current)
        {
            return new PlayerStageResult
            {
                isValid = false,
                invalidReason = reason,
                state = current,
                presentation = new ActionPresentation { isValid = false, invalidReason = reason },
            };
        }
    }
}
