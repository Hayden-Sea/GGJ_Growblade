using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    public enum TurnPhase
    {
        Title, LoadingLevel, AwaitInput, PresentingPlayer, Growing, PresentingGrowth, PresentingEnemies, Paused, LevelComplete, Defeat,
    }

    /// <summary>拍事务（文档 10.3-10.4）：玩家阶段与敌人阶段之间的可暂停成长点。</summary>
    public sealed class PendingBeat
    {
        public int sessionId;
        public int beatId;
        public GameState state;
        public ActionPresentation presentation;
        public bool hasGrowthRequest;
        public GrowthRequest growthRequest;
        public bool enemyExecuted;
        public bool resumed;
    }

    /// <summary>单一输入门：玩家播放 → 成长暂停 → 敌人阶段恢复（文档 10.2 / 10.5）。</summary>
    public class TurnDirector : MonoBehaviour
    {
        public TurnPhase Phase { get; private set; } = TurnPhase.Title;

        public event Action<TurnPhase> OnPhaseChanged;
        public event Action OnStateRefresh;

        private RunController _run;
        private BeatSimulator _sim;
        private ActionPresenter _presenter;
        private InputReader _input;
        private HUDController _hud;
        private AudioService _audio;
        private GameConfig _cfg;

        private PendingBeat _pending;
        private bool _previewActive;
        private bool _paused;
        private TurnPhase _pausedFromPhase = TurnPhase.AwaitInput;

        private Func<ActorView> _getPlayerView;
        private Func<Transform> _getSwordPivot;
        private Func<SwordView> _getSwordView;
        private Func<Dictionary<int, ActorView>> _getEnemyViews;
        private Func<Dictionary<int, IntentView>> _getIntentViews;

        public void Init(GameConfig cfg, RunController run, ActionPresenter presenter, InputReader input,
            HUDController hud, AudioService audio,
            Func<ActorView> getPlayerView, Func<Transform> getSwordPivot, Func<SwordView> getSwordView,
            Func<Dictionary<int, ActorView>> getEnemyViews, Func<Dictionary<int, IntentView>> getIntentViews)
        {
            _cfg = cfg;
            _run = run;
            _sim = run.Simulator;
            _presenter = presenter;
            _input = input;
            _hud = hud;
            _audio = audio;

            _getPlayerView = getPlayerView;
            _getSwordPivot = getSwordPivot;
            _getSwordView = getSwordView;
            _getEnemyViews = getEnemyViews;
            _getIntentViews = getIntentViews;

            _input.OnCommand += HandleCommand;
            _input.OnPauseToggle += TogglePause;
            // v1.4：成长只由战斗画面左键确认；Space / Enter 不再确认（文档 5.4）
        }

        public void SetPhase(TurnPhase phase)
        {
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        public void EnterTitle() => SetPhase(TurnPhase.Title);
        public void BeginAwaitInput() => SetPhase(TurnPhase.AwaitInput);

        /// <summary>换关 / 重试 / 返回选择：取消拍事务与全部回调（文档 10.5）。</summary>
        public void CancelTransaction()
        {
            _pending = null;
            _previewActive = false;
            _presenter.StopAllCoroutines();
            _presenter.ClearPreview();
        }

        // ---- 输入 ----

        private void HandleCommand(PlayerAction action, bool preview)
        {
            if (Phase != TurnPhase.AwaitInput)
                return;

            if (preview)
            {
                ShowPreview(action);
                return;
            }

            ClearPreview();
            SubmitAction(action);
        }

        public void SubmitAction(PlayerAction action)
        {
            if (Phase != TurnPhase.AwaitInput)
                return;
            var current = _run.Current;
            if (current == null)
                return;

            var result = _sim.BeginPlayerAction(current, action, _run.Board);
            if (!result.isValid)
            {
                if (action.kind == PlayerActionKind.Move)
                {
                    int session = _run.SessionId;
                    float progress = MovementCollision.BlockedMoveProgress(current, action, _run.Board, _cfg);
                    SetPhase(TurnPhase.PresentingPlayer);
                    _presenter.PlayBlockedMove(current.player.cell, action.moveDirection, progress, session, () =>
                    {
                        if (_run.SessionId == session) BeginAwaitInput();
                    });
                    return;
                }
                _audio?.Play(SfxId.Click);
                _hud?.ShowInvalid(DescribeReason(result.invalidReason));
                return;
            }

            _pending = new PendingBeat
            {
                sessionId = run_SessionId(),
                beatId = result.beatId,
                state = result.state,
                presentation = result.presentation,
                hasGrowthRequest = result.hasGrowthRequest,
                growthRequest = result.growthRequest,
            };
            SetPhase(TurnPhase.PresentingPlayer);
            _presenter.PlayPlayerPhase(result.presentation, _pending.sessionId, () => AfterPlayerPhase(_pending));
        }

        private int run_SessionId() => _run.SessionId;

        private void AfterPlayerPhase(PendingBeat pending)
        {
            if (pending == null || pending.sessionId != _run.SessionId)
                return;
            _run.ApplyPlayerStage(pending.state, pending.presentation);
            OnStateRefresh?.Invoke();
            ContinueAfterPlayer(pending);
        }

        private void ContinueAfterPlayer(PendingBeat pending)
        {
            if (pending.sessionId != _run.SessionId || pending.beatId != pending.state.beatIndex)
                return;

            bool newFruit = pending.hasGrowthRequest && pending.growthRequest.source != GrowthSource.StoredCredit;
            if (newFruit)
            {
                OpenGrowth(pending); // 新果即使无候选也必须打开一次（暂存兜底）
                return;
            }
            if (_run.Current.growthCredits > 0 && (_run.Current.mandatoryGrowthCredits > 0 || _run.HasPlaceableCandidatesNow()))
            {
                OpenGrowth(pending); // 暂存点在归位后自动重开
                return;
            }
            ResumeEnemies(pending);
        }

        private void OpenGrowth(PendingBeat pending)
        {
            _run.OpenGrowth();
            SetPhase(TurnPhase.Growing);
        }

        /// <summary>确认当前悬停候选（仅由战斗画面左键调用，文档 10.4）：
        /// 纯模拟 → PresentingGrowth 伸长表现 → 完成后原子提交一根 / 一点 / 命中结果。</summary>
        public bool ConfirmGrowth()
        {
            if (Phase != TurnPhase.Growing)
                return false;
            var pending = _pending;
            if (pending == null)
                return false;

            var result = _run.ConfirmGrowth();
            if (result == null || !result.valid)
            {
                _hud?.ShowInvalid(result?.invalidReason ?? "该位置无法生长");
                if (result != null && !result.valid && result.invalidReason == "候选已过期")
                {
                    _run.CloseGrowth();
                    _run.OpenGrowth(); // 重新枚举候选，保持 Growing
                }
                return false;
            }

            SetPhase(TurnPhase.PresentingGrowth);
            _presenter.PlayGrowth(result, pending.sessionId, () => AfterGrowth(pending, result));
            return true;
        }

        private void AfterGrowth(PendingBeat pending, GrowthTransactionResult result)
        {
            if (pending == null || pending.sessionId != _run.SessionId)
                return;
            _run.ApplyGrowth(result);
            pending.state = result.nextState; // 敌人续拍使用生长后的工作状态
            OnStateRefresh?.Invoke();

            if (_run.Current.growthCredits > 0)
            {
                OpenGrowth(pending); // 仍有成长点：刷新候选继续选择（无位置时显示暂存）
                return;
            }
            ResumeEnemies(pending);
        }

        /// <summary>仅当前无可放置候选时允许暂存并继续。</summary>
        public void StoreCredit()
        {
            if (Phase != TurnPhase.Growing || _run.Growth.HasCandidates)
                return;
            if (_run.Current.mandatoryGrowthCredits > 0)
            {
                _hud?.ShowInvalid($"还需完成 {_run.Current.mandatoryGrowthCredits} 次生长");
                return;
            }
            var pending = _pending;
            _run.CloseGrowth();
            ResumeEnemies(pending);
        }

        private void ResumeEnemies(PendingBeat pending)
        {
            if (pending == null || pending.sessionId != _run.SessionId)
                return;
            if (pending.resumed)
                return; // TryMarkResumingOnce：同一次恢复不重复执行
            pending.resumed = true;

            if (BeatSimulator.CoreObjectiveCompleted(pending.state))
                _run.ResolveDefeatedCore();

            // 敌人阶段模拟：执行原意图、移除死亡、计划下一拍意图（文档 10.4 ResumeSameBeat）
            var enemyResult = _sim.ResumeEnemyPhase(pending.state, _run.Board, pending.presentation);
            pending.state = enemyResult.state;

            SetPhase(TurnPhase.PresentingEnemies);
            _presenter.PlayEnemyPhase(pending.presentation, pending.sessionId, () => AfterEnemies(pending));
        }

        private void AfterEnemies(PendingBeat pending)
        {
            if (pending == null || pending.sessionId != _run.SessionId)
                return;
            _run.ApplyEnemyStage(pending.state);
            OnStateRefresh?.Invoke();

            if (!_run.SessionActive)
            {
                // 通关 / 失败在 ApplyEnemyStage 内判定完成，切到对应终态
                SetPhase(_run.LastOutcome == Outcome.Victory ? TurnPhase.LevelComplete : TurnPhase.Defeat);
                return;
            }
            SetPhase(TurnPhase.AwaitInput);
        }

        // ---- 预览 ----

        private void ShowPreview(PlayerAction action)
        {
            var result = _sim.BeginPlayerAction(_run.Current, action, _run.Board);
            _presenter.ShowPreview(result.presentation, _run.Current.player.cell);
            if (!result.isValid)
                _hud?.ShowInvalid(DescribeReason(result.invalidReason));
            _previewActive = true;
        }

        public void ClearPreview()
        {
            if (_previewActive)
            {
                _presenter.ClearPreview();
                _previewActive = false;
            }
        }

        // ---- 暂停 ----

        private bool _pausedFromIsSet;

        public void TogglePause()
        {
            if (Phase == TurnPhase.Title || Phase == TurnPhase.LevelComplete || Phase == TurnPhase.Defeat)
                return;
            if (!_paused)
            {
                _pausedFromPhase = Phase; // 精确记录，恢复回原阶段（文档 10.5）
                _pausedFromIsSet = true;
                if (Phase == TurnPhase.Growing)
                    _run.Growth.ClearSelection(); // 暂停清空悬停，恢复后须重新悬停（文档 4.5）
            }
            _paused = !_paused;
            _presenter.paused = _paused;
            _hud?.ShowPause(_paused);
            SetPhase(_paused ? TurnPhase.Paused : (_pausedFromIsSet ? _pausedFromPhase : TurnPhase.AwaitInput));
        }

        public void ForceResumeFromPause()
        {
            if (_paused)
                TogglePause();
        }

        // ---- 成长 ----

        public void ConfirmGrowthFromInput()
        {
            if (Phase == TurnPhase.Growing)
                ConfirmGrowth();
        }

        public void StoreCreditFromInput()
        {
            if (Phase == TurnPhase.Growing)
                StoreCredit();
        }

        private static string DescribeReason(ActionInvalidReason reason)
        {
            switch (reason)
            {
                case ActionInvalidReason.BodyHitsWall: return "身体会撞墙";
                case ActionInvalidReason.BladeHitsWall: return "剑会被墙挡住";
                case ActionInvalidReason.TargetOccupied: return "先把目标格清开";
                case ActionInvalidReason.OutsideBoard: return "超出地图";
                default: return "未知";
            }
        }
    }
}
