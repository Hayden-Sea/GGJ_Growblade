using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SwordGame
{
    /// <summary>组合根：读取配置、建立服务、开始新局（文档 10.3）。场景中只需挂在一个空物体上。</summary>
    public class GameBootstrap : MonoBehaviour
    {
        public static bool SkipPresentation;
        public static float SpeedMultiplier = 1f;

        public GameConfig Config { get; private set; }
        public RunController Run { get; private set; }
        public TurnDirector Director { get; private set; }
        public ActionPresenter Presenter { get; private set; }
        public InputReader Input { get; private set; }
        public HUDController Hud { get; private set; }
        public AudioService Audio { get; private set; }

        private Transform _cameraTransform;
        private Transform _roomRoot;
        private Transform _actorRoot;
        private Transform _vfxRoot;
        private RoomView _roomView;
        private WorldGrowthOverlay _growthOverlay;
        private bool _growthClickArmed;
        private int _lastHoverIndex = -1;

        private ActorView _playerView;
        private Transform _swordPivot;
        private SwordView _swordView;
        private readonly Dictionary<int, ActorView> _enemyViews = new Dictionary<int, ActorView>();
        private readonly Dictionary<int, IntentView> _intentViews = new Dictionary<int, IntentView>();

        private void Awake()
        {
            Config = Resources.Load<GameConfig>("CFG_Game_Default");
            if (Config == null)
            {
                Debug.LogError("[GameBootstrap] 缺少 Resources/CFG_Game_Default 资产");
                return;
            }
            var runDef = Resources.Load<RunDefinition>("RUN_Default");
            if (runDef == null)
            {
                Debug.LogError("[GameBootstrap] 缺少 Resources/RUN_Default 资产");
                return;
            }

            Application.targetFrameRate = 60;

            // 相机
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 6.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Config.backgroundColor;
            cam.transform.position = new Vector3(6.5f, 5.5f, -10f);
            camGo.AddComponent<GardenBoardFraming>();
            _cameraTransform = camGo.transform;

            // EventSystem
            var esGo = new GameObject("EventSystem");
            esGo.transform.SetParent(transform, false);
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();

            // 根节点
            _roomRoot = NewChild("RoomRoot");
            _actorRoot = NewChild("ActorRoot");
            _vfxRoot = NewChild("VfxRoot");
            _roomView = _roomRoot.gameObject.AddComponent<RoomView>();

            // 服务
            Audio = gameObject.AddComponent<AudioService>();
            Audio.InitDefault();

            Presenter = gameObject.AddComponent<ActionPresenter>();
            Presenter.Init(Config, Audio, _vfxRoot, _cameraTransform);
            Presenter.SetSwordStateProvider(() => Run?.Sword);

            Run = gameObject.AddComponent<RunController>();
            Run.Init(Config, runDef);

            Input = gameObject.AddComponent<InputReader>();

            Hud = gameObject.AddComponent<HUDController>();
            Hud.Build(Config, Audio);

            Director = gameObject.AddComponent<TurnDirector>();
            Director.Init(Config, Run, Presenter, Input, Hud, Audio,
                () => _playerView, () => _swordPivot, () => _swordView,
                () => _enemyViews, () => _intentViews);

            // HUD 事件
            Hud.OnStartClicked += OnTitleStart;
            Hud.OnLevelSelected += SelectLevel;
            Hud.OnNextLevel += NextLevel;
            Hud.OnRestartClicked += RetryLevel;
            Hud.OnOpenLevelSelect += OpenLevelSelect;
            Hud.OnResumeClicked += () => Director.ForceResumeFromPause();
            Hud.OnPauseToggleClicked += () => Director.TogglePause();
            Hud.OnMoveClicked += dir => Input.EmitMove(dir);
            Hud.OnRotateClicked += q => Input.EmitRotate(q);
            Hud.OnWaitClicked += Input.EmitWait;
            Hud.OnStoreCredit += () => Director.StoreCreditFromInput();
            Hud.OnFirstLevelTutorialClosed += BeginFirstLevelAfterTutorial;
            Hud.OnQuitGameClicked += QuitGame;
            Hud.OnUnlockAllConfirmed += UnlockAllLevels;

            // 运行事件
            Run.OnRoomLoaded += OnRoomLoaded;
            Run.OnRoomClearedChanged += OnRoomCleared;
            Run.OnRunEnded += OnRunEnded;
            Run.OnSwordChanged += OnSwordChanged;
            Run.OnIntentsChanged += RefreshIntents;
            Run.OnTutorialToast += msg => Hud.ShowToast(msg);
            Run.OnGrowthOpened += OnGrowthOpened;
            Run.OnGrowthClosed += OnGrowthClosed;

            Director.OnStateRefresh += OnBeatRefreshed;
            Director.OnPhaseChanged += OnPhaseChanged;

            // 世界内成长候选覆盖层（文档 11.4）：战斗画面实际剑位置，主相机可见
            _growthOverlay = gameObject.AddComponent<WorldGrowthOverlay>();
            _growthOverlay.Setup(_vfxRoot);

            // 成长提示文本
            Run.Growth.OnStatsChanged += s => Hud.SetGrowthHint(s);

            // 编辑器试玩桥（文档 19.5）：一次性关卡覆盖，读取后即清除
            var overrideRoom = PlaytestBridge.ConsumeRoomOverride();
            if (overrideRoom != null)
            {
                Run.LoadLevel(overrideRoom, 0, "playtest");
                Hud.ShowGame();
                Director.BeginAwaitInput();
                return;
            }
        }

        private Transform NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void Update()
        {
            if (Presenter != null)
            {
                Presenter.skipPresentation = SkipPresentation;
                Presenter.speedMultiplier = Mathf.Max(0.05f, SpeedMultiplier);
            }

            // 成长中：主相机鼠标悬停选候选，左键确认（文档 4.5 / 5.4 / 10.4）
            if (Director != null && Director.Phase == TurnPhase.Growing && Run.Current != null)
            {
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                Vector2? local = overUI ? null : ScreenToSwordLocal(UnityEngine.Input.mousePosition);

                int selectedBefore = Run.Growth.SelectedIndex;
                if (local.HasValue)
                    Run.Growth.PickCandidate(local.Value, Run.Current.sword, Config);
                else
                    Run.Growth.ClearSelection();
                int selectedNow = Run.Growth.SelectedIndex;

                _growthOverlay.SetHover(selectedNow);
                if (selectedNow != _lastHoverIndex)
                {
                    _lastHoverIndex = selectedNow;
                    UpdateHitPreview(selectedNow);
                }

                // 状态门：进入 Growing 后须先观察到 MouseUp；同帧刚变化的候选不提交（防点未见结果）
                if (UnityEngine.Input.GetMouseButtonUp(0))
                    _growthClickArmed = true;
                if (UnityEngine.Input.GetMouseButtonDown(0) && _growthClickArmed && !overUI
                    && selectedNow >= 0 && selectedNow == selectedBefore)
                {
                    _growthClickArmed = false;
                    Director.ConfirmGrowthFromInput();
                }
            }
        }

        /// <summary>屏幕坐标 → 剑局部坐标（格单位）：射线与棋盘平面 z=0 求交，
        /// 减玩家轴心后按朝向逆旋（文档 5.4 鼠标映射）。</summary>
        private Vector2? ScreenToSwordLocal(Vector3 screenPos)
        {
            var cam = Camera.main;
            if (cam == null || Run.Current == null)
                return null;
            if (!cam.pixelRect.Contains((Vector2)screenPos))
                return null;
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out float enter))
                return null;
            Vector2 world = ray.GetPoint(enter);
            Vector2 rel = world - BoardModel.CellCenter(Run.Current.player.cell);
            float ang = -Run.Current.player.facing * Mathf.PI / 2f;
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            return new Vector2(rel.x * c - rel.y * s, rel.x * s + rel.y * c);
        }

        /// <summary>悬停变化时用同一求解器预演命中（预览与正式执行共用 GrowthSimulator）。</summary>
        private void UpdateHitPreview(int index)
        {
            if (index < 0 || Run.Current == null || !Run.Growth.HasSelection)
            {
                _growthOverlay.ShowHitPreview(null);
                return;
            }
            var cand = Run.Growth.Selected;
            var hits = GrowthSimulator.Simulate(Run.Current.Clone(), cand.anchor, cand.newNode,
                Run.Current.player.facing, Run.Current.player.cell, Run.Board, Config, 0);
            _growthOverlay.ShowHitPreview(hits);
        }

        private void OnPhaseChanged(TurnPhase phase)
        {
            // 成长选择与伸长表现期间禁用战斗行动按钮；暂停 / 暂存保留（文档 10.5）
            Hud.SetCombatInputEnabled(phase != TurnPhase.Growing && phase != TurnPhase.PresentingGrowth);
        }

        // ---- 流程 ----

        private void OnTitleStart()
        {
            Hud.ShowLevelSelect(true, LevelList(), Run.UnlockedStageCount);
            Director.EnterTitle();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("[GameBootstrap] 已请求退出游戏（编辑器内不退出）。");
#else
            Application.Quit();
#endif
        }

        private System.Collections.Generic.IReadOnlyList<RoomDefinition> LevelList()
        {
            var list = new System.Collections.Generic.List<RoomDefinition>();
            for (int i = 0; i < Run.StageCount; i++)
                list.Add(Run.GetStageRoom(i));
            return list;
        }

        public void SelectLevel(int index)
        {
            if (!Run.IsStageUnlocked(index))
                return;
            Hud.ShowGame();
            Director.CancelTransaction();
            Run.LoadLevelByIndex(index, "select");
            if (index == 0)
                Hud.ShowFirstLevelTutorial();
            else
                Director.BeginAwaitInput();
        }

        private void BeginFirstLevelAfterTutorial()
        {
            if (Run.Current != null && Run.StageIndex == 0)
                Director.BeginAwaitInput();
        }

        public void NextLevel()
        {
            if (!Run.HasNextStage)
                return;
            Hud.ShowGame();
            Director.CancelTransaction();
            Run.LoadNextLevel();
            Director.BeginAwaitInput();
        }

        private void RetryLevel()
        {
            Hud.ShowGame();
            Director.CancelTransaction();
            Director.ForceResumeFromPause();
            Run.RetryCurrentLevel();
            Director.BeginAwaitInput();
        }

        private void OpenLevelSelect()
        {
            Audio?.StopResultCue();
            Director.CancelTransaction();
            Director.ForceResumeFromPause();
            Run.EndSession();
            Director.EnterTitle();
            Hud.ShowLevelSelect(true, LevelList(), Run.UnlockedStageCount);
        }

        private void UnlockAllLevels()
        {
            Run.UnlockAllStages();
            Hud.ShowLevelSelect(true, LevelList(), Run.UnlockedStageCount);
        }

        private void OnRunEnded(Outcome outcome, string stats)
        {
            Audio?.Play(outcome == Outcome.Victory ? SfxId.Victory : SfxId.Defeat);
            Hud.ShowResult(outcome == Outcome.Victory, stats, Run.HasNextStage, Run.StageIndex == Run.StageCount - 1);
        }

        // ---- 房间 ----

        private void OnRoomLoaded(int index, GameState state, RoomDefinition room)
        {
            Audio?.StopResultCue();
            _roomView.Build(room, Run.Board, Config);
            _roomView.SetExitLit(room.IsExitOpenByDefault);
            _roomView.SetExitVisible(!room.RequiresPlates);

            _cameraTransform.position = new Vector3(room.width / 2f, room.height / 2f, -10f);
            _cameraTransform.GetComponent<GardenBoardFraming>().SetBoard(room.width, room.height);
            Presenter.SetCameraBase(new Vector3(room.width / 2f, room.height / 2f, -10f));

            // 玩家视图
            if (_playerView == null)
            {
                var playerGo = new GameObject("Player");
                playerGo.transform.SetParent(_actorRoot, false);
                _playerView = playerGo.AddComponent<ActorView>();
                _swordPivot = new GameObject("SwordPivot").transform;
                _swordPivot.SetParent(playerGo.transform, false);
                var swordGo = new GameObject("Sword");
                swordGo.transform.SetParent(_swordPivot, false);
                _swordView = swordGo.AddComponent<SwordView>();
                var hiltGo = new GameObject("Hilt");
                hiltGo.transform.SetParent(_swordPivot, false);
                var hilt = hiltGo.AddComponent<SpriteRenderer>();
                hilt.sprite = GardenTheme.Plate(GardenTheme.Gold);
                hilt.transform.localPosition = new Vector3(0.28f, 0f, 0f);
                hilt.transform.localScale = new Vector3(0.14f, 0.38f, 1f) / hilt.sprite.bounds.size.x;
                hilt.color = Color.white;
                hilt.sortingOrder = 11;
            }
            var bodySprite = Config.playerSprite != null ? Config.playerSprite : SpriteFactory.Circle();
            _playerView.Init(-1, bodySprite, Color.white, 0.7f, 2);
            _playerView.gameObject.SetActive(true);
            _playerView.SnapTo(BoardModel.CellCenter(state.player.cell));
            _swordPivot.localRotation = Quaternion.Euler(0f, 0f, state.player.facing * 90f);
            _swordView.Rebuild(state.sword, Config);

            // 敌人/意图视图
            ClearEnemyViews();
            foreach (var e in state.enemies)
            {
                var go = new GameObject($"Enemy_{e.actorId}_{e.kind}");
                go.transform.SetParent(_actorRoot, false);
                var view = go.AddComponent<ActorView>();
                Sprite sprite = e.kind == EnemyKind.Box ? (Config.boxSprite != null ? Config.boxSprite : GardenTheme.Plate(new Color(.72f, .49f, .25f), false))
                    : e.kind == EnemyKind.Core ? Config.coreSprite
                    : e.kind == EnemyKind.Archer ? Config.archerSprite
                    : Config.chargerSprite;
                if (sprite == null) sprite = SpriteFactory.Circle();
                float diameter = e.kind == EnemyKind.Core ? 0.86f : e.kind == EnemyKind.Box ? 0.85f : 0.7f;
                view.Init(e.actorId, sprite, Color.white, diameter, 2,
                    e.kind == EnemyKind.Box ? 0.85f : 0.88f);
                view.SnapTo(BoardModel.CellCenter(e.cell));
                _enemyViews[e.actorId] = view;

                if (e.kind != EnemyKind.Box)
                {
                    var intentGo = new GameObject($"Intent_{e.actorId}");
                    intentGo.transform.SetParent(_vfxRoot, false);
                    var iv = intentGo.AddComponent<IntentView>();
                    iv.Init(Config);
                    _intentViews[e.actorId] = iv;
                }
            }
            RefreshIntents();
            RefreshFruitViews(state);

            Presenter.SetViews(_playerView, _swordPivot, _swordView, _enemyViews, _intentViews);

            Hud.SetHearts(state.player.hp, room.ResolveInitialPlayerHp(Config));
            Hud.SetRoomInfo($"{index + 1}/{Run.StageCount}", room.goalText,
                $"总节数 {state.sword.edges.Count} · 果 {CountRemainingFruits()} · 成长点 {state.growthCredits}");
        }

        private readonly Dictionary<string, GameObject> _fruitViews = new Dictionary<string, GameObject>();

        private void RefreshFruitViews(GameState state)
        {
            foreach (var kv in _fruitViews)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _fruitViews.Clear();
            foreach (var f in state.fruits)
            {
                var go = new GameObject("Fruit_" + f.fruitId);
                go.transform.SetParent(_vfxRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Config.fruitSprite != null ? Config.fruitSprite : GardenTheme.Icon(4);
                sr.color = Color.white;
                sr.sortingOrder = -2;
                go.transform.localScale = Vector3.one * ((Config.fruitRadius * 2f) / sr.sprite.bounds.size.x);
                go.transform.position = BoardModel.CellCenter(f.cell);
                if (f.ResolvedGrowthValue >= 2)
                    AddFruitGrowthValueLabel(go.transform, f.ResolvedGrowthValue);
                go.SetActive(!f.consumed);
                _fruitViews[f.fruitId] = go;
            }
        }

        private void AddFruitGrowthValueLabel(Transform fruit, int value)
        {
            // 徽章独立于果实的 Sprite 缩放：把数量置于果实正上方，果本体和数字互不遮挡。
            float fruitScale = Mathf.Max(.001f, fruit.localScale.x);
            var badge = new GameObject("成长次数徽章");
            badge.transform.SetParent(fruit, false);
            badge.transform.localScale = Vector3.one / fruitScale;
            badge.transform.localPosition = new Vector3(0f, .36f / fruitScale, -.1f);

            MakeFruitBadgeLayer(badge.transform, "描边", GardenTheme.Ink, .50f, -1);
            MakeFruitBadgeLayer(badge.transform, "底色", GardenTheme.Paper, .40f, 0);

            var label = new GameObject("成长次数");
            label.transform.SetParent(badge.transform, false);
            label.transform.localPosition = new Vector3(0f, -.012f, -.1f);
            // TMP 的世界字形基准远小于 Sprite；0.78 时“2”实宽约 0.2 格，足以在 640px Game View 识别。
            label.transform.localScale = Vector3.one * .78f;
            var text = label.AddComponent<TextMeshPro>();
            text.text = value.ToString();
            if (Config.uiFont != null) text.font = Config.uiFont;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = false;
            text.fontStyle = FontStyles.Bold;
            text.fontSize = 4f;
            text.color = GardenTheme.Ink;
            text.rectTransform.sizeDelta = new Vector2(3f, 3f);
            text.renderer.sortingOrder = 1;
        }

        private static SpriteRenderer MakeFruitBadgeLayer(Transform parent, string name, Color color, float diameter, int order)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(parent, false);
            var renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteFactory.Circle();
            renderer.color = color;
            renderer.sortingOrder = order;
            float baseSize = renderer.sprite.bounds.size.x;
            layer.transform.localScale = Vector3.one * (diameter / baseSize);
            return renderer;
        }

        private void OnBeatRefreshed()
        {
            var state = Run.Current;
            if (state == null) return;

            Hud.SetHearts(state.player.hp, Run.RoomDef.ResolveInitialPlayerHp(Config));
            bool isExitObjective = Run.RoomDef != null;
            Hud.SetRoomInfo($"{Run.StageIndex + 1}/{Run.StageCount}",
                Run.RoomDef.IsExitOpenByDefault ? Run.RoomDef.goalText :
                isExitObjective && Run.RoomCleared
                    ? (Run.RoomDef.objective == RoomDefinition.LevelObjective.PushBoxesToPlatesAndExit ? "机关已启动" :
                       Run.RoomDef.objective == RoomDefinition.LevelObjective.ClearEnemiesAndPlatesAndExit ? "敌人已清除，机关已启动" : "房间已清理")
                    : Run.RoomDef.goalText,
                $"总节数 {state.sword.edges.Count} · 果 {CountRemainingFruits()} · 成长点 {state.growthCredits}");

            // 移除死亡敌人视图
            var dead = new List<int>();
            foreach (var kv in _enemyViews)
                if (state.enemies.Find(e => e.actorId == kv.Key) == null)
                    dead.Add(kv.Key);
            foreach (var id in dead)
            {
                if (_enemyViews.TryGetValue(id, out var v) && v != null)
                    Destroy(v.gameObject);
                _enemyViews.Remove(id);
                if (_intentViews.TryGetValue(id, out var iv) && iv != null)
                    Destroy(iv.gameObject);
                _intentViews.Remove(id);
            }

            // 同步存活敌人位置（击退/移动动画终点即逻辑格）
            foreach (var e in state.enemies)
                if (_enemyViews.TryGetValue(e.actorId, out var view) && view != null)
                    view.SnapTo(BoardModel.CellCenter(e.cell));

            // 同步果显示（被砍到 / 拾取的果在回弹归位后隐藏）
            foreach (var f in state.fruits)
                if (_fruitViews.TryGetValue(f.fruitId, out var fv) && fv != null)
                    fv.SetActive(!f.consumed);

            _playerView.SnapTo(BoardModel.CellCenter(state.player.cell));
            _swordPivot.localRotation = Quaternion.Euler(0f, 0f, state.player.facing * 90f);
            _swordView.Rebuild(state.sword, Config);

            RefreshIntents();

            if (isExitObjective && Run.RoomCleared && !Run.RoomDef.IsExitOpenByDefault)
            {
                _roomView.SetExitVisible(true);
                _roomView.SetExitLit(true);
                if (state.player.cell != Run.ExitCell)
                    Hud.ShowToast(Run.RoomDef.objective == RoomDefinition.LevelObjective.PushBoxesToPlatesAndExit
                        ? "机关已启动：出口出现了。" :
                        Run.RoomDef.objective == RoomDefinition.LevelObjective.ClearEnemiesAndPlatesAndExit
                            ? "敌人已清除，机关已启动：出口出现了。" : "房间已清理：前往出口继续。", 2f);
            }
            else if (Run.RoomDef.RequiresPlates)
            {
                _roomView.SetExitVisible(false);
            }
        }

        private int CountRemainingFruits()
        {
            int n = 0;
            if (Run.Current != null)
                foreach (var f in Run.Current.fruits)
                    if (!f.consumed)
                        n++;
            return n;
        }

        private void OnRoomCleared()
        {
            if (Run.RoomDef != null)
            {
                _roomView.SetExitVisible(Run.RoomCleared || !Run.RoomDef.RequiresPlates);
                _roomView.SetExitLit(Run.RoomCleared);
            }
        }

        private void RefreshIntents()
        {
            var snapshots = Run.CurrentIntents();
            foreach (var snap in snapshots)
            {
                // 被成长打断的敌人：本拍意图置灰（文档 4.7）
                bool cancelled = false;
                var enemy = Run.Current != null
                    ? Run.Current.enemies.Find(e => e.actorId == snap.actorId)
                    : null;
                if (enemy != null)
                    cancelled = enemy.interruptedThisBeat;
                if (_intentViews.TryGetValue(snap.actorId, out var iv) && iv != null)
                    iv.Show(snap, Run.Board, Config, cancelledByPreview: cancelled);
            }
        }

        private void OnSwordChanged(SwordState sword)
        {
            if (_swordView != null)
                _swordView.Rebuild(sword, Config);
        }

        private void OnGrowthOpened()
        {
            var cur = Run.Current;
            if (cur != null)
                _growthOverlay.Show(cur.player.cell, cur.player.facing, Run.Growth.Candidates);
            _growthClickArmed = false; // 连续下一根需先观察到鼠标松开（文档 4.5）
            _lastHoverIndex = -1;
            Hud.ShowGrowthHint(true);
            Hud.SetGrowthNoCandidates(!Run.Growth.HasCandidates,
                Run.Current != null && Run.Current.mandatoryGrowthCredits > 0,
                Run.Current != null ? Run.Current.mandatoryGrowthCredits : 0);
        }

        private void OnGrowthClosed()
        {
            _growthOverlay.Hide();
            _lastHoverIndex = -1;
            Hud.ShowGrowthHint(false);
        }

        private void ClearEnemyViews()
        {
            foreach (var kv in _enemyViews)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _enemyViews.Clear();
            foreach (var kv in _intentViews)
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            _intentViews.Clear();
        }
    }
}
