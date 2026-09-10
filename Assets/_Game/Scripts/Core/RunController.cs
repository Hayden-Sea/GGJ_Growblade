using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>独立关载入、短剑重置、果 / 成长账本、关卡目标与结果（文档 9 / 10.2）。</summary>
    public class RunController : MonoBehaviour
    {
        private const string UnlockedStageCountKey = "sword_will_grow_unlocked_stage_count";
        public GameState Current { get; private set; }
        public BoardModel Board { get; private set; }
        public RoomDefinition RoomDef { get; private set; }
        public bool RoomCleared { get; private set; }
        public bool SessionActive { get; private set; }

        public event Action<int, GameState, RoomDefinition> OnRoomLoaded;
        public event Action OnRoomClearedChanged;
        public event Action<Outcome, string> OnRunEnded;
        public event Action<SwordState> OnSwordChanged;
        public event Action OnIntentsChanged;
        public event Action<int> OnBeatCommitted;
        public event Action<string> OnTutorialToast;
        public event Action OnGrowthOpened;
        public event Action OnGrowthClosed;

        private GameConfig _cfg;
        private RunDefinition _runDef;
        private BeatSimulator _sim;
        private GrowthController _growth;

        public GrowthController Growth => _growth;
        public int SessionId { get; private set; } = 1;
        public int TotalBeats { get; private set; }
        public int KnockbackKills { get; private set; }
        public float RunStartTime { get; private set; }
        public int StageIndex => Current?.roomIndex ?? 0;
        public int StageCount => _runDef != null ? _runDef.stages.Count : 0;
        public SwordState Sword => Current?.sword;
        public bool HasNextStage => StageIndex + 1 < StageCount;
        public int UnlockedStageCount => ProgressionRules.NormalizeUnlockedCount(PlayerPrefs.GetInt(UnlockedStageCountKey, 1), StageCount);

        public void Init(GameConfig cfg, RunDefinition runDef)
        {
            _cfg = cfg;
            _runDef = runDef;
            _sim = new BeatSimulator(cfg);
            _growth = new GrowthController(cfg);
        }

        public BeatSimulator Simulator => _sim;

        // ---- 统一关载入入口（文档 9.2）----

        public void LoadLevelByIndex(int index, string reason)
        {
            if (_runDef == null || index < 0 || index >= _runDef.stages.Count || _runDef.stages[index].room == null)
            {
                Debug.LogError($"[RunController] 关卡 {index} 缺失，reason={reason}");
                return;
            }
            LoadLevel(_runDef.stages[index].room, index, reason);
        }

        public void RetryCurrentLevel()
        {
            if (RoomDef == null)
                return;
            LoadLevel(RoomDef, Current?.roomIndex ?? 0, "retry");
        }

        public void LoadNextLevel()
        {
            if (!HasNextStage)
                return;
            LoadLevelByIndex(StageIndex + 1, "next");
        }

        /// <summary>所有入口（标题 / 选择 / 下一关 / 重试 / 调试 / 编辑器试玩）统一走这里：
        /// 每关都创建初始短剑、HP4、0 拍、完整果和敌人（文档 9.2）。</summary>
        public void LoadLevel(RoomDefinition room, int index, string reason)
        {
            if (room == null)
            {
                Debug.LogError($"[RunController] LoadLevel({reason}) 房间为空");
                return;
            }

            SessionId++;
            TotalBeats = 0;
            KnockbackKills = 0;
            RunStartTime = Time.time;
            SessionActive = true;
            if (GrowthOpen)
                CloseGrowth(); // 换关 / 重试取消候选与成长界面（文档 10.5）
            RoomDef = room;
            Board = room.BuildBoard();
            RoomCleared = false;
            var sword = SwordState.CreateInitial(); // 每关短剑开局，无跨关继承（R09）

            Vector2Int? coreCell = room.hasCore ? room.coreCell : (Vector2Int?)null;
            var spawnBlockers = new List<EnemySpawnDef>(room.enemySpawns);
            foreach (var cell in room.boxSpawns)
                spawnBlockers.Add(new EnemySpawnDef { cell = cell, kind = EnemyKind.Box });
            bool ok = SpawnValidator.TryPickSpawn(Board, room.playerSpawns, sword, spawnBlockers, coreCell, _cfg,
                out var spawn, out var failReason);
            if (!ok)
            {
                Debug.LogError($"[RunController] 房间 {room.roomId} 无合法出生点（{reason}）：{failReason}");
                return;
            }

            Current = new GameState
            {
                sessionId = SessionId,
                roomIndex = index,
                beatIndex = 0,
                player = new PlayerState { cell = spawn.cell, facing = spawn.facing, hp = room.ResolveInitialPlayerHp(_cfg) },
                sword = sword,
                enemies = new List<EnemyState>(),
                growthCredits = 0,
                mandatoryGrowthCredits = 0,
                runSeed = 0,
            };

            int actorId = 0;
            foreach (var def in room.enemySpawns)
            {
                Current.enemies.Add(new EnemyState
                {
                    actorId = actorId++,
                    kind = def.kind,
                    cell = def.cell,
                    hp = _cfg.HpFor(def.kind),
                });
            }
            int boxActorId = 200;
            foreach (var cell in room.boxSpawns)
            {
                Current.enemies.Add(new EnemyState
                {
                    actorId = boxActorId++, kind = EnemyKind.Box, cell = cell,
                    hp = _cfg.HpFor(EnemyKind.Box), intent = EnemyIntent.Wait(false),
                });
            }
            if (room.hasCore)
            {
                Current.enemies.Add(new EnemyState
                {
                    actorId = 100,
                    kind = EnemyKind.Core,
                    cell = room.coreCell,
                    hp = _cfg.coreHp,
                });
            }
            foreach (var f in room.fruitSpawns)
                Current.fruits.Add(new FruitState { fruitId = f.fruitId, cell = f.cell, growthValue = Mathf.Max(1, f.growthValue), consumed = false });

            // “直接离开”关不需要等待任何事件：出口加载时即为可用状态。
            RoomCleared = room.IsExitOpenByDefault;

            EnemyPlanner.PlanNextIntents(Current, Board, _cfg);

            OnRoomLoaded?.Invoke(index, Current, room);
            OnIntentsChanged?.Invoke();

            MaybeTutorial("intro", index == 0, "你行动一拍，敌人行动一拍。");
            MaybeTutorial("fruit", room.fruitSpawns.Count > 0, "碰到果可以生长。用剑砍果时，先回弹再生长。");
        }

        /// <summary>调试/测试：直接载入某阶段（v1.3 每关短剑）。</summary>
        public void DebugLoadStage(int index) => LoadLevelByIndex(index, "debug");

        public bool IsStageUnlocked(int index) => index >= 0 && index < UnlockedStageCount;

        public void UnlockAllStages()
        {
            SaveUnlockedStageCount(StageCount);
        }

        // ---- 阶段提交 ----

        /// <summary>玩家阶段提交：果消费与成长点记账已在模拟工作状态中发生一次。</summary>
        public void ApplyPlayerStage(GameState state, ActionPresentation presentation)
        {
            Current = state;
            // 核心现在属于“需要清除的敌人”，死亡表现完成后移除，但不再立即通关。
            BeatSimulator.RemoveDeadCore(Current);
            TotalBeats++;
            KnockbackKills += presentation.knockbackWallKills;
            OnBeatCommitted?.Invoke(Current.beatIndex);

            RefreshExitObjectiveClearState();

            if (!_tutorialShown.Contains("static"))
            {
                var segments = SwordGeometry.GetSegments(Current.sword);
                Vector2 pivot = BoardModel.CellCenter(Current.player.cell);
                foreach (var e in Current.enemies)
                {
                    if (e.kind == EnemyKind.Core) continue;
                    Vector2 c = BoardModel.CellCenter(e.cell);
                    foreach (var seg in segments)
                    {
                        Vector2 a = SwordGeometry.HalfToWorld(seg.aHalf, pivot, Current.player.facing);
                        Vector2 b = SwordGeometry.HalfToWorld(seg.bHalf, pivot, Current.player.facing);
                        if (Geometry2D.PointSegmentDistanceSquared(c, a, b) <= 0.01f)
                        {
                            MaybeTutorial("static", true, "只有你动剑时才会攻击。");
                            break;
                        }
                    }
                }
            }

            if (!_tutorialShown.Contains("wallkill") && presentation.knockbackWallKills > 0)
                MaybeTutorial("wallkill", true, "把敌人推向墙会追加 1 点伤害。");
        }

        /// <summary>敌人阶段提交：不重复计拍；判定失败 / 通关（文档 9.1）。</summary>
        public void ApplyEnemyStage(GameState state)
        {
            Current = state;

            if (Current.player.hp <= 0)
            {
                SessionActive = false;
                LastOutcome = Outcome.Defeat;
                OnRunEnded?.Invoke(Outcome.Defeat, BuildStats());
                return;
            }
            if (ExitObjectiveCompleted())
            {
                CompleteLevel();
            }
        }

        public Outcome LastOutcome { get; private set; } = Outcome.Continue;

        public void ResolveDefeatedCore()
        {
            BeatSimulator.RemoveDeadCore(Current);
            RefreshExitObjectiveClearState();
        }

        private void CompleteLevel()
        {
            SaveUnlockedStageCount(ProgressionRules.UnlockAfterVictory(UnlockedStageCount, StageIndex, StageCount));
            SessionActive = false;
            LastOutcome = Outcome.Victory;
            OnRunEnded?.Invoke(Outcome.Victory, BuildStats());
        }

        private void SaveUnlockedStageCount(int count)
        {
            PlayerPrefs.SetInt(UnlockedStageCountKey, ProgressionRules.NormalizeUnlockedCount(count, StageCount));
            PlayerPrefs.Save();
        }

        /// <summary>返回关卡选择等非战斗流程：结束当前关会话。</summary>
        public void EndSession()
        {
            SessionActive = false;
        }

        public bool ExitObjectiveCompleted()
        {
            return RoomDef != null && Current != null && Current.player.hp > 0 && IsExitObjectiveCleared(RoomDef, Current) &&
                   Current.player.cell == RoomDef.exitCell;
        }

        /// <summary>“房间已清理”只属于清敌后前往出口的目标；核心关清空守卫仍须摧毁核心。</summary>
        public static bool IsExitObjectiveCleared(RoomDefinition room, GameState state)
        {
            if (room == null || state == null) return false;
            bool enemiesCleared = !state.HasAliveClearableEnemies();
            bool platesActive = state.AreAllBoxesOn(room.pressurePlateCells);
            if (room.objective == RoomDefinition.LevelObjective.ReachExit)
                return true;
            if (room.objective == RoomDefinition.LevelObjective.ClearEnemiesAndExit)
                return enemiesCleared;
            if (room.objective == RoomDefinition.LevelObjective.PushBoxesToPlatesAndExit)
                return platesActive;
            if (room.objective == RoomDefinition.LevelObjective.ClearEnemiesAndPlatesAndExit)
                return enemiesCleared && platesActive;
            return false;
        }

        private void RefreshExitObjectiveClearState()
        {
            bool cleared = IsExitObjectiveCleared(RoomDef, Current);
            if (RoomCleared == cleared) return;
            RoomCleared = cleared;
            OnRoomClearedChanged?.Invoke();
        }

        /// <summary>当前站位下是否存在可放置候选（文档 5.5：暂存点归位后自动重开的条件）。</summary>
        public bool HasPlaceableCandidatesNow()
        {
            if (Current == null || Board == null)
                return false;
            var cell = Current.player.cell;
            var facing = Current.player.facing;
            var coreCell = RoomDef.hasCore ? RoomDef.coreCell : (Vector2Int?)null;
            foreach (var c in GrowthOptions.GetGraphCandidates(Current.sword, _cfg))
                if (GrowthPlacement.Filter(Current.sword, c.edge, cell, facing, Board, Current, coreCell, _cfg).ok)
                    return true;
            return false;
        }

        // ---- 成长（果 / 暂存点触发，文档 5.5）----

        public bool GrowthOpen { get; private set; }
        private int _nextGrowthActionId = 1;

        public void OpenGrowth()
        {
            if (GrowthOpen || Current == null)
                return;
            GrowthOpen = true;
            var cell = Current.player.cell;
            var facing = Current.player.facing;
            var coreCell = RoomDef != null && RoomDef.hasCore ? RoomDef.coreCell : (Vector2Int?)null;
            _growth.Open(Current.sword, Current.swordVersion, (s, edge) =>
                GrowthPlacement.Filter(s, edge, cell, facing, Board, Current, coreCell, _cfg).ok);
            OnGrowthOpened?.Invoke();
        }

        public void CloseGrowth()
        {
            if (!GrowthOpen)
                return;
            GrowthOpen = false;
            OnGrowthClosed?.Invoke();
        }

        /// <summary>模拟确认当前可见候选（文档 10.3/10.4）：不提交、不扣点，
        /// 返回独立事务结果；提交在伸长表现完成后由 ApplyGrowth 原子执行一次。</summary>
        public GrowthTransactionResult ConfirmGrowth()
        {
            if (!GrowthOpen || Current == null || !_growth.HasSelection)
                return GrowthTransactionResult.Invalid("没有可生长的候选");
            if (_growth.BaseSwordVersion != Current.swordVersion)
                return GrowthTransactionResult.Invalid("候选已过期");

            var coreCell = RoomDef != null && RoomDef.hasCore ? RoomDef.coreCell : (Vector2Int?)null;
            var result = SimulateConfirm(Current, Board, coreCell, _cfg,
                _nextGrowthActionId++, _growth.Selected);
            if (result.valid)
                CloseGrowth(); // 呈现期间收起候选覆盖层；提交后若仍有成长点再重开
            return result;
        }

        /// <summary>纯模拟一次确认：放置校验 → 伸长攻击命中 → 加边扣点（文档 4.7 / 5.5）。</summary>
        public static GrowthTransactionResult SimulateConfirm(GameState current, BoardModel board, Vector2Int? coreCell,
            GameConfig cfg, int growthActionId, GrowthCandidate selected)
        {
            if (current == null || board == null || cfg == null)
                return GrowthTransactionResult.Invalid("内部状态缺失");
            if (current.growthCredits <= 0)
                return GrowthTransactionResult.Invalid("没有可用成长点");

            var (ok, reason) = GrowthPlacement.Filter(current.sword, selected.edge,
                current.player.cell, current.player.facing, board, current, coreCell, cfg);
            if (!ok)
                return GrowthTransactionResult.Invalid(reason ?? "位置非法");

            var working = current.Clone();
            var hits = GrowthSimulator.Simulate(working, selected.anchor, selected.newNode,
                current.player.facing, current.player.cell, board, cfg, growthActionId);

            // 原子变更：加一根、growthCount+1、growthCredits-1、swordVersion+1。
            // 多生长果的点优先消耗，确保它不能被暂存跳过。
            working.sword.edges.Add(BladeSegment.Canonical(selected.anchor, selected.newNode));
            working.sword.growthCount++;
            working.growthCredits--;
            if (working.mandatoryGrowthCredits > 0)
                working.mandatoryGrowthCredits--;
            working.swordVersion++;

            bool coreKilled = false;
            foreach (var h in hits)
                if (h.isCore && h.died)
                {
                    coreKilled = true;
                    break;
                }

            return new GrowthTransactionResult
            {
                valid = true,
                sessionId = working.sessionId,
                beatId = working.beatIndex,
                growthActionId = growthActionId,
                sourceSwordVersion = current.swordVersion,
                anchorHalf = selected.anchor,
                newNodeHalf = selected.newNode,
                edgeKey = selected.edgeKey,
                nextState = working,
                hits = hits,
                coreKilledByThisGrowth = coreKilled,
            };
        }

        /// <summary>伸长表现完成后原子提交一次：剑 / 点数 / 敌人结果同时生效（文档 5.5）。</summary>
        public void ApplyGrowth(GrowthTransactionResult result)
        {
            if (result == null || !result.valid || Current == null)
                return;
            Current = result.nextState;
            BeatSimulator.RemoveDeadCore(Current);
            OnSwordChanged?.Invoke(Current.sword);

            foreach (var h in result.hits)
                if (h.died && h.wallImpact)
                    KnockbackKills++;

            RefreshExitObjectiveClearState();
            OnIntentsChanged?.Invoke();
        }

        // ---- 查询 ----

        public Vector2Int? ExitCell => RoomDef != null ? RoomDef.exitCell : (Vector2Int?)null;
        public RoomDefinition GetStageRoom(int index) =>
            _runDef != null && index >= 0 && index < StageCount ? _runDef.stages[index].room : null;

        public List<EnemyIntentSnapshot> CurrentIntents()
        {
            var list = new List<EnemyIntentSnapshot>();
            if (Current != null)
                foreach (var e in Current.enemies)
                    list.Add(GameState.Snapshot(e));
            return list;
        }

        private string BuildStats()
        {
            float longestLength = Current != null ? SwordGeometry.MaxReachRadius(Current.sword) : 0f;
            return $"行动了 {TotalBeats} 拍\n剑身最长长度 {longestLength:0.0} 格";
        }

        private readonly HashSet<string> _tutorialShown = new HashSet<string>();

        private void MaybeTutorial(string key, bool condition, string message)
        {
            if (!condition || _tutorialShown.Contains(key))
                return;
            _tutorialShown.Add(key);
            OnTutorialToast?.Invoke(message);
        }
    }
}
