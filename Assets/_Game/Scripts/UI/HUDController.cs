using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using G = SwordGame.GardenTheme;

namespace SwordGame
{
    /// <summary>Garden sketch UI. Existing game callbacks and world-growth input ownership are preserved.</summary>
    public class HUDController : MonoBehaviour
    {
        private GameConfig _cfg;
        private AudioService _audio;
        private Transform _canvasRoot;
        public Action OnStartClicked, OnRestartClicked, OnResumeClicked, OnOpenLevelSelect, OnNextLevel, OnStoreCredit, OnWaitClicked, OnPauseToggleClicked, OnFirstLevelTutorialClosed, OnQuitGameClicked, OnUnlockAllConfirmed, OnLanguageChanged;
        public Action<int> OnLevelSelected, OnRotateClicked;
        public Action<Vector2Int> OnMoveClicked;
        private GameObject _titlePanel, _hudPanel, _pausePanel, _resultPanel, _levelSelectPanel, _firstLevelTutorialPanel, _unlockAllConfirmPanel;
        private GameObject _storeButton, _nextButton, _growthStrip, _toastStrip, _invalidStrip;
        private TextMeshProUGUI _roomText, _goalText, _growthText, _toastText, _invalidText, _resultTitle, _resultStats, _growthHint, _resultCaption;
        private Image[] _hearts;
        private Transform _heartParent;
        private readonly List<Button> _combatButtons = new List<Button>();
        private Coroutine _toastRoutine, _invalidRoutine;
        private IReadOnlyList<RoomDefinition> _levels;
        private int _levelPage;
        private int _unlockedStageCount;
        private GameObject _settingsPanel;
        private GameObject _languageDropdown;
        private TextMeshProUGUI _languageCaption;
        private TMP_FontAsset _koreanFont;
        private readonly Dictionary<TextMeshProUGUI, string> _localizedTexts = new Dictionary<TextMeshProUGUI, string>();
        private string _lastRoomText, _lastGoal, _lastGrowthText;

        public void Build(GameConfig cfg, AudioService audio)
        {
            _cfg = cfg; _audio = audio;
            GameLocalization.Load();
            InitializeFontFallbacks();
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = go.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1280, 720);
            scale.matchWidthOrHeight = 0.5f;
            _canvasRoot = go.transform;
            BuildTitle(); BuildLevelSelect(); BuildHud(); BuildGrowth(); BuildPause(); BuildSettings(); BuildResult(); BuildFirstLevelTutorial();
            ShowTitle();
        }

        private void BuildTitle()
        {
            _titlePanel = Panel("TitlePanel", G.Paper, true);
            Decorate(_titlePanel.transform, false);
            var title = Text(_titlePanel.transform, "标题", "剑会变长", 74, new Vector2(-350, 126), new Vector2(410, 115), G.Ink, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            Text(_titlePanel.transform, "副标题", "剑会长大。\n路，要想好。", 28, new Vector2(-350, 18), new Vector2(400, 85), G.Ink, TextAlignmentOptions.Left);
            Button(_titlePanel.transform, "开始按钮", "开始冒险", new Vector2(-395, -65), new Vector2(300, 58), () => OnStartClicked?.Invoke(), G.Gold);
            Button(_titlePanel.transform, "设置按钮", "设置", new Vector2(-395, -123), new Vector2(300, 46), () => ShowSettings(false), G.Cream);
            Button(_titlePanel.transform, "退出按钮", "退出游戏", new Vector2(-395, -175), new Vector2(300, 42), () => OnQuitGameClicked?.Invoke(), G.Cream);
            Text(_titlePanel.transform, "作者", "by 海蛋", 15, new Vector2(0, 22), new Vector2(240, 28), G.Muted, TextAlignmentOptions.Center, new Vector2(.5f, 0));
            BuildTitleGarden(_titlePanel.transform);
        }

        private void BuildTitleGarden(Transform parent)
        {
            var card = Card(parent, "冒险小景", new Vector2(260, 12), new Vector2(535, 462), G.Cream);
            card.localRotation = Quaternion.Euler(0, 0, -2);
            Text(card, "SceneLabel", "一把剑，很多种可能", 19, new Vector2(0, 183), new Vector2(440, 35), G.Muted);
            var board = Card(card, "Garden", new Vector2(0, -8), new Vector2(450, 290), G.Sage, false);
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 7; x++)
                Picture(board, "Tile", G.Tile(false, (x + y) % 3), new Vector2((x - 3) * 60, (y - 1.5f) * 60), new Vector2(60, 60));
            Picture(board, "Knight", G.Icon(0), new Vector2(-162, -6), new Vector2(73, 95));
            // The title illustration uses the same straight, half-tile visual vocabulary.
            var sword = Card(board, "HeroBlade", new Vector2(-15, -6), new Vector2(228, 19), G.Cream, false);
            Card(board, "Branch", new Vector2(75, 27), new Vector2(19,  80f), G.Cream, false);
            Picture(board, "Fruit", G.Icon(4), new Vector2(75,  96f), new Vector2(47, 58));
            Picture(board, "Enemy", G.Icon(1), new Vector2(160, -58), new Vector2(66, 72));
            Picture(board, "Flower", G.Icon(7), new Vector2(-175, 92), new Vector2(34, 45));
            Text(card, "SceneCaption", "拿起短剑，走进这座小小花园。", 17, new Vector2(0, -189), new Vector2(455, 32), G.Muted);
        }

        private void BuildLevelSelect()
        {
            _levelSelectPanel = Panel("LevelSelectPanel", G.Paper, true);
            Decorate(_levelSelectPanel.transform, false);
            Text(_levelSelectPanel.transform, "标题", "选择一段冒险", 42, new Vector2(0, 270), new Vector2(820, 70), G.Ink).fontStyle = FontStyles.Bold;
            Text(_levelSelectPanel.transform, "说明", "每一关，都从一把短剑开始。", 19, new Vector2(0, 222), new Vector2(900, 35), G.Muted);
            Button(_levelSelectPanel.transform, "返回标题", "返回", new Vector2(-530, 287), new Vector2(125, 44), ShowTitle, G.Cream);
            Button(_levelSelectPanel.transform, "解锁全部", "解锁全部", new Vector2(510, 287), new Vector2(145, 44), ShowUnlockAllConfirm, G.Gold);
            BuildUnlockAllConfirm();
        }

        public void ShowLevelSelect(bool show, IReadOnlyList<RoomDefinition> levels, int unlockedStageCount)
        {
            if (!show) { _levelSelectPanel.SetActive(false); return; }
            _levels = levels; _levelPage = 0; _unlockedStageCount = Mathf.Clamp(unlockedStageCount, 0, levels != null ? levels.Count : 0);
            RenderLevelPage();
            _levelSelectPanel.SetActive(true);
            _titlePanel.SetActive(false); _hudPanel.SetActive(false); _pausePanel.SetActive(false); _resultPanel.SetActive(false);
        }

        private void RenderLevelPage()
        {
            var old = _levelSelectPanel.transform.Find("列表");
            if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
            var list = Rect(_levelSelectPanel.transform, "列表", Vector2.zero, new Vector2(1120, 530));
            int count = _levels != null ? _levels.Count : 0;
            int pages = Mathf.Max(1, (count + 8) / 9);
            _levelPage = Mathf.Clamp(_levelPage, 0, pages - 1);
            for (int slot = 0; slot < 9; slot++)
            {
                int index = _levelPage * 9 + slot;
                if (index >= count) break;
                var room = _levels[index];
                if (room == null) continue;
                int captured = index;
                bool unlocked = index < _unlockedStageCount;
                var pos = new Vector2((slot % 3 - 1) * 356, 138 - (slot / 3) * 140);
                var button = Button(list, "关_" + index, "", pos, new Vector2(330, 121), () => { if (unlocked) OnLevelSelected?.Invoke(captured); }, unlocked ? G.Cream : G.Alpha(G.Cream, .62f));
                button.GetComponent<Button>().interactable = unlocked;
                var tr = button.transform;
                Text(tr, "编号", (index + 1).ToString("00"), 37, new Vector2(-111, 13), new Vector2(65, 55), unlocked ? G.Moss : G.Muted).fontStyle = FontStyles.Bold;
                Text(tr, "名字", unlocked ? FriendlyName(room.roomId) : "尚未解锁", 21, new Vector2(34, 22), new Vector2(210, 39), unlocked ? G.Ink : G.Muted, TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
                int fruits = room.fruitSpawns == null ? 0 : room.fruitSpawns.Count;
                Text(tr, "内容", unlocked ? GameLocalization.FruitCount(fruits) : "完成前一关后开放", 15, new Vector2(34, -22), new Vector2(210, 28), G.Muted, TextAlignmentOptions.Left);
                if (unlocked)
                    Picture(tr, "果", G.Icon(4), new Vector2(-111, - 30f), new Vector2(22, 26));
                else
                    Text(tr, "锁", GameLocalization.LockedMark, 21, new Vector2(-111, - 30f), new Vector2(32, 28), G.Muted).fontStyle = FontStyles.Bold;
            }
            Text(list, "页码", (_levelPage + 1) + " / " + pages, 19, new Vector2(0, -263), new Vector2(160, 36), G.Muted);
            var prev = Button(list, "上一页", "上一页", new Vector2(-210, -263), new Vector2(145, 44), () => { _levelPage--; RenderLevelPage(); }, G.Cream);
            var next = Button(list, "下一页", "下一页", new Vector2(210, -263), new Vector2(145, 44), () => { _levelPage++; RenderLevelPage(); }, G.Cream);
            prev.GetComponent<Button>().interactable = _levelPage > 0;
            next.GetComponent<Button>().interactable = _levelPage < pages - 1;
            if (count == 0) Text(list, "空", "还没有关卡，请先在关卡编辑器中添加。", 22, Vector2.zero, new Vector2(800, 80), G.Muted);
        }

        private void BuildUnlockAllConfirm()
        {
            _unlockAllConfirmPanel = Panel("UnlockAllConfirm", G.Alpha(G.Ink, .64f), true);
            var card = Card(_unlockAllConfirmPanel.transform, "解锁确认便笺", Vector2.zero, new Vector2(470, 270), G.Paper);
            var title = Text(card, "标题", "解锁全部关卡？", 30, new Vector2(0, 73), new Vector2(390, 50), G.Ink);
            title.fontStyle = FontStyles.Bold;
            Text(card, "说明", "这会立即开放所有冒险。", 17, new Vector2(0, 26), new Vector2(380, 32), G.Muted);
            Button(card, "取消", "再想想", new Vector2(-105, -70), new Vector2(180, 48), () => _unlockAllConfirmPanel.SetActive(false), G.Cream);
            Button(card, "确认", "全部解锁", new Vector2(105, -70), new Vector2(180, 48), () =>
            {
                _unlockAllConfirmPanel.SetActive(false);
                OnUnlockAllConfirmed?.Invoke();
            }, G.Gold);
            _unlockAllConfirmPanel.SetActive(false);
        }

        private void ShowUnlockAllConfirm()
        {
            _unlockAllConfirmPanel.SetActive(true);
            ClearSelection();
        }
        private static string FriendlyName(string value) => GameLocalization.RoomName(value);

        private void BuildHud()
        {
            _hudPanel = Panel("HudPanel", Color.clear, false);
            var left = Card(_hudPanel.transform, "关卡便笺", new Vector2(130, -258), new Vector2(208, 298), G.Cream, anchor: new Vector2(0, 1));
            _roomText = Text(left, "房间", "01 / 09", 36, new Vector2(0, 67), new Vector2(180, 54), G.Ink);
            _roomText.fontStyle = FontStyles.Bold;
            _heartParent = left;
            BuildHeartSlots(_cfg.playerHp);
            _goalText = Text(left, "目标", "", 20, new Vector2(0, -86), new Vector2(172, 83), G.Ink);
            Picture(_hudPanel.transform, "左侧小花", G.Icon(7), new Vector2(131, -483), new Vector2(56, 76), anchor: new Vector2(0, 1));

            Button(_hudPanel.transform, "暂停按钮", "暂停", new Vector2(- 80f, -45), new Vector2(116, 43), () => OnPauseToggleClicked?.Invoke(), G.Cream, new Vector2(1, 1));
            var right = Card(_hudPanel.transform, "成长手记", new Vector2(-124, -221), new Vector2(198, 235), G.Cream, anchor: new Vector2(1, 1));
            Picture(right, "果图标", G.Icon(4), new Vector2(-63,  70f), new Vector2(32, 40));
            Text(right, "成长标签", "成长手记", 19, new Vector2(26,  70f), new Vector2(123,  30f), G.Ink);
            _growthText = Text(right, "成长", "",  19f, new Vector2(0, -17), new Vector2(164, 124), G.Ink, TextAlignmentOptions.Left);
            var cluster = Rect(_hudPanel.transform, "ActionCluster", new Vector2(-124, 191), new Vector2(190, 192), new Vector2(1, 0));
            CombatButton(cluster, "W", 1, 2, () => OnMoveClicked?.Invoke(new Vector2Int(0, 1)));
            CombatButton(cluster, "A", 0, 1, () => OnMoveClicked?.Invoke(new Vector2Int(-1, 0)));
            CombatButton(cluster, "S", 1, 1, () => OnMoveClicked?.Invoke(new Vector2Int(0, -1)));
            CombatButton(cluster, "D", 2, 1, () => OnMoveClicked?.Invoke(new Vector2Int(1, 0)));
            CombatButton(cluster, "Q", 0, 0, () => OnRotateClicked?.Invoke(1));
            CombatButton(cluster, "待", 1, 0, () => OnWaitClicked?.Invoke());
            CombatButton(cluster, "E", 2, 0, () => OnRotateClicked?.Invoke(-1));
            Text(_hudPanel.transform, "按键提示", "WASD 移动    Q / E 转剑    Space 等待", 15, new Vector2(0, 23), new Vector2(850,  30f), G.Muted, anchor: new Vector2(.5f, 0));
            _toastStrip = Card(_hudPanel.transform, "提示纸条", new Vector2(0, 68), new Vector2(760, 53), G.Cream, false, new Vector2(.5f, 0)).gameObject;
            _toastText = Text(_toastStrip.transform, "提示", "", 18, Vector2.zero, new Vector2(724, 42), G.Ink);
            _toastStrip.SetActive(false);
            _invalidStrip = Card(_hudPanel.transform, "无效纸条", new Vector2(0, -52), new Vector2(640, 46), G.Cream, false, new Vector2(.5f, 1)).gameObject;
            _invalidText = Text(_invalidStrip.transform, "无效反馈", "", 18, Vector2.zero, new Vector2(610, 36), G.Coral);
            _invalidStrip.SetActive(false);
        }

        private void CombatButton(Transform parent, string label, int col, int row, Action action)
        {
            var go = Button(parent, "B_" + label, label, new Vector2((col - 1) *  60f, (row - 1) * 60), new Vector2(53, 51), action, (label == "Q" || label == "E") ? G.Sage : G.Cream);
            _combatButtons.Add(go.GetComponent<Button>());
        }

        private void BuildGrowth()
        {
            _growthStrip = Card(_hudPanel.transform, "成长提示条", new Vector2(0, 70), new Vector2(780, 56), G.Gold, false, new Vector2(.5f, 0)).gameObject;
            _growthHint = Text(_growthStrip.transform, "成长提示", "移动鼠标预览 · 左键生长", 18, Vector2.zero, new Vector2(741,  40f), G.Ink);
            _growthStrip.SetActive(false);
            _storeButton = Button(_hudPanel.transform, "暂存", "空间不足 · 暂存成长", new Vector2(130, 108), new Vector2(213,  50f), () => OnStoreCredit?.Invoke(), G.Gold, new Vector2(0, 0));
            _storeButton.SetActive(false);
        }

        private void BuildPause()
        {
            _pausePanel = Panel("PausePanel", G.Alpha(G.Ink, .64f), true);
            var card = Card(_pausePanel.transform, "暂停便笺", Vector2.zero, new Vector2(520, 626), G.Paper);
            Text(card, "标题", "歇一小会儿", 38, new Vector2(0, 245), new Vector2(430, 65), G.Ink).fontStyle = FontStyles.Bold;
            Text(card, "说明", "花园会在这里等你。", 18, new Vector2(0, 194), new Vector2(400, 35), G.Muted);
            Button(card, "继续", "继续冒险", new Vector2(0, 122), new Vector2(330, 56), () => OnResumeClicked?.Invoke(), G.Gold);
            Button(card, "重试本关", "重试本关", new Vector2(0, 52), new Vector2(330, 52), () => OnRestartClicked?.Invoke(), G.Cream);
            Button(card, "选择关卡", "选择关卡", new Vector2(0, -14), new Vector2(330, 52), () => OnOpenLevelSelect?.Invoke(), G.Cream);
            Button(card, "设置", "设置", new Vector2(0, -80), new Vector2(330, 52), () => ShowSettings(true), G.Cream);
        }

        private void BuildSettings()
        {
            _settingsPanel = Panel("SettingsPanel", G.Alpha(G.Ink, .64f), true);
            var card = Card(_settingsPanel.transform, "设置便笺", Vector2.zero, new Vector2(540, 610), G.Paper);
            Text(card, "标题", "设置选项", 38, new Vector2(0, 245), new Vector2(440, 60), G.Ink).fontStyle = FontStyles.Bold;
            Text(card, "语言标签", "语言", 18, new Vector2(-175, 184), new Vector2(120, 32), G.Muted, TextAlignmentOptions.Left);
            var selector = Button(card, "LanguageSelector", "", new Vector2(0, 140), new Vector2(354, 46), ToggleLanguageDropdown, G.Cream);
            _languageCaption = Text(selector.transform, "CurrentLanguage", "", 19, new Vector2(-16, 0), new Vector2(280, 34), G.Ink);
            _languageCaption.fontStyle = FontStyles.Bold;
            Text(selector.transform, "Arrow", "▼", 15, new Vector2(145, 0), new Vector2(28, 28), G.Muted);

            int languageCount = GameLocalization.SupportedLanguages.Count;
            int visibleRows = UiTextFitPolicy.LanguageDropdownVisibleRows(languageCount);
            float popupHeight = visibleRows * UiTextFitPolicy.LanguageDropdownRowHeight + 8f;
            float popupY = 113f - popupHeight * .5f;
            _languageDropdown = Card(card, "LanguageDropdown", new Vector2(0, popupY), new Vector2(354, popupHeight), G.Cream, true).gameObject;

            var viewport = Rect(_languageDropdown.transform, "Viewport", new Vector2(-7, 0), new Vector2(330, popupHeight - 8f));
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.white; viewportImage.sprite = G.Plate(Color.white, false); viewportImage.raycastTarget = true;
            var mask = viewport.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;

            float contentHeight = UiTextFitPolicy.LanguageDropdownContentHeight(languageCount);
            var content = Rect(viewport, "Content", Vector2.zero, new Vector2(322, contentHeight));
            content.anchorMin = content.anchorMax = new Vector2(.5f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            for (int i = 0; i < GameLocalization.SupportedLanguages.Count; i++)
            {
                var definition = GameLocalization.SupportedLanguages[i];
                var language = definition.language;
                Button(content, "Option_" + language, definition.nativeName,
                    new Vector2(0, -19f - i * UiTextFitPolicy.LanguageDropdownRowHeight),
                    new Vector2(314, 34), () => ChangeLanguage(language), G.Cream, new Vector2(.5f, 1f));
            }

            var scroll = _languageDropdown.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28f;

            var track = Picture(_languageDropdown.transform, "Scrollbar", G.Plate(G.Sage, false), new Vector2(169, 0), new Vector2(7, popupHeight - 14f), sliced: true);
            track.raycastTarget = true;
            var handle = Picture(track.transform, "Handle", G.Plate(G.Moss, false), Vector2.zero, new Vector2(7, Mathf.Max(28f, (popupHeight - 14f) * visibleRows / Mathf.Max(visibleRows, languageCount))), sliced: true);
            handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle; scrollbar.handleRect = handle.rectTransform; scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            _languageDropdown.SetActive(false);
            BuildSlider(card, "音乐", new Vector2(25, -72), _audio != null ? _audio.MusicVolume : .7f, v => _audio?.SetMusicVolume(v));
            BuildSlider(card, "音效", new Vector2(25, -130), _audio != null ? _audio.SfxVolume : .8f, v => _audio?.SetSfxVolume(v));
            var toggleRect = Rect(card, "镜头震动", new Vector2(0, -188), new Vector2(300, 35));
            var toggle = toggleRect.gameObject.AddComponent<Toggle>();
            var box = Picture(toggleRect, "Box", G.Plate(G.Cream), new Vector2(- 80f, 0), new Vector2( 30f,  30f));
            box.raycastTarget = true;
            var mark = Picture(toggleRect, "Check", G.Plate(G.Moss, false), new Vector2(- 80f, 0), new Vector2(18, 18));
            toggle.targetGraphic = box; toggle.graphic = mark;
            toggle.isOn = _audio == null || _audio.ShakeEnabled;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.onValueChanged.AddListener(v => { _audio?.SetShakeEnabled(v); ClearSelection(); });
            Text(toggleRect, "Label", "镜头轻微震动", 18, new Vector2( 30f, 0), new Vector2(200,  30f), G.Ink);
            Button(card, "关闭", "关闭", new Vector2(0, -252), new Vector2(300, 46), CloseSettings, G.Gold);
            _settingsPanel.SetActive(false);
            RefreshLanguageButtons();
        }

        private void ShowSettings(bool fromPause)
        {
            if (_languageDropdown != null) _languageDropdown.SetActive(false);
            _settingsPanel.SetActive(true);
            ClearSelection();
        }

        private void CloseSettings() { if (_languageDropdown != null) _languageDropdown.SetActive(false); _settingsPanel.SetActive(false); ClearSelection(); }

        private void ToggleLanguageDropdown()
        {
            if (_languageDropdown != null) _languageDropdown.SetActive(!_languageDropdown.activeSelf);
        }

        private void ChangeLanguage(GameLanguage language)
        {
            GameLocalization.Set(language);
            if (_languageDropdown != null) _languageDropdown.SetActive(false);
            ApplyLanguage();
            OnLanguageChanged?.Invoke();
        }

        private void ApplyLanguage()
        {
            foreach (var pair in _localizedTexts)
                if (pair.Key != null) pair.Key.text = GameLocalization.Text(pair.Value);
            RefreshLanguageButtons();
            if (_levels != null && _levelSelectPanel.activeSelf) RenderLevelPage();
            if (!string.IsNullOrEmpty(_lastRoomText)) SetRoomInfo(_lastRoomText, _lastGoal, _lastGrowthText);
        }

        private void RefreshLanguageButtons()
        {
            if (_languageCaption != null)
            {
                foreach (var definition in GameLocalization.SupportedLanguages)
                    if (definition.language == GameLocalization.Current) { _languageCaption.text = definition.nativeName; break; }
            }
        }

        private void InitializeFontFallbacks()
        {
            if (_koreanFont == null)
            {
                var source = Resources.Load<Font>("Fonts/NotoSansKR-VF");
                if (source != null) _koreanFont = TMP_FontAsset.CreateFontAsset(source);
            }
            if (_cfg.uiFont != null && _koreanFont != null && !_cfg.uiFont.fallbackFontAssetTable.Contains(_koreanFont))
                _cfg.uiFont.fallbackFontAssetTable.Add(_koreanFont);
        }

        private void BuildFirstLevelTutorial()
        {
            _firstLevelTutorialPanel = Panel("FirstLevelTutorial", G.Alpha(G.Ink, .64f), true);
            var card = Card(_firstLevelTutorialPanel.transform, "新手便笺", Vector2.zero, new Vector2(650, 486), G.Paper);
            Text(card, "小标题", "欢迎来到花园", 17, new Vector2(0, 199), new Vector2(440, 30), G.Muted);
            var title = Text(card, "标题", "剑会变长", 42, new Vector2(0, 151), new Vector2(500, 64), G.Ink);
            title.fontStyle = FontStyles.Bold;
            Text(card, "说明", "每次行动，世界也走一拍。", 18, new Vector2(0, 108), new Vector2(480, 32), G.Muted);

            var moves = Card(card, "移动", new Vector2(-190, 26), new Vector2(250, 112), G.Cream, false);
            Text(moves, "键", "移动  WASD", 20, new Vector2(0, 25), new Vector2(220, 32), G.Ink).fontStyle = FontStyles.Bold;
            Text(moves, "说明", "向前走，剑身会击退敌人。", 14, new Vector2(0, -18), new Vector2(220, 38), G.Muted);

            var turn = Card(card, "转剑", new Vector2(190, 26), new Vector2(250, 112), G.Cream, false);
            Text(turn, "键", "转剑  Q / E", 20, new Vector2(0, 25), new Vector2(220, 32), G.Ink).fontStyle = FontStyles.Bold;
            Text(turn, "说明", "转动 90°；撞墙会回弹。", 14, new Vector2(0, -18), new Vector2(220, 38), G.Muted);

            var grow = Card(card, "生长", new Vector2(0, -100), new Vector2(530, 104), G.Sage, false);
            Text(grow, "标题", "果实会让剑生长", 20, new Vector2(0, 29), new Vector2(480, 32), G.Ink).fontStyle = FontStyles.Bold;
            Text(grow, "生长说明", "碰到果实后，移动鼠标预览，再用左键确认生长。", 14, new Vector2(0, -2), new Vector2(490, 28), G.Ink);
            Text(grow, "等待说明", "Space 等待，经过一拍。", 14, new Vector2(0, -30), new Vector2(490, 26), G.Ink);

            Text(card, "目标", "这一关：拾取果实，清理敌人，前往出口。", 17, new Vector2(0, -175), new Vector2(540, 32), G.Muted);
            Button(card, "开始", "开始冒险", new Vector2(0, -225), new Vector2(300, 55), () =>
            {
                _firstLevelTutorialPanel.SetActive(false);
                OnFirstLevelTutorialClosed?.Invoke();
            }, G.Gold);
            _firstLevelTutorialPanel.SetActive(false);
        }

        private void BuildSlider(Transform parent, string label, Vector2 position, float initial, Action<float> changed)
        {
            var tr = Rect(parent, label, position, new Vector2(280, 32));
            var hitArea = tr.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear; hitArea.raycastTarget = true;
            Text(tr, "Label", label, 18, new Vector2(-205, 0), new Vector2(130,  30f), G.Muted);
            var back = Picture(tr, "Track", G.Plate(G.Sage, false), Vector2.zero, new Vector2(260, 12), sliced: true);
            back.raycastTarget = true;
            var fillArea = Rect(tr, "FillArea", Vector2.zero, new Vector2(250, 10));
            var fill = Picture(fillArea, "Fill", G.Plate(G.Moss, false), Vector2.zero, new Vector2(250, 10), sliced: true);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = Rect(tr, "HandleArea", Vector2.zero, new Vector2(250, 32));
            var knob = Picture(handleArea, "Handle", G.Plate(G.Gold), Vector2.zero, new Vector2(26, 28), sliced: true);
            knob.raycastTarget = true;
            var slider = tr.gameObject.AddComponent<Slider>();
            slider.targetGraphic = knob; slider.fillRect = fill.rectTransform; slider.handleRect = knob.rectTransform;
            slider.minValue = 0; slider.maxValue = 1; slider.value = initial;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v => { changed(v); ClearSelection(); });
        }

        private void BuildResult()
        {
            _resultPanel = Panel("ResultPanel", G.Alpha(G.Ink, .66f), true);
            var card = Card(_resultPanel.transform, "结算便笺", Vector2.zero, new Vector2(660, 530), G.Paper);
            Picture(card, "花", G.Icon(7), new Vector2(252, 165), new Vector2(65,  96f));
            _resultTitle = Text(card, "结果", "又长大了一点", 40, new Vector2(0, 179), new Vector2(520, 72), G.Ink);
            _resultTitle.fontStyle = FontStyles.Bold;
            _resultCaption = Text(card, "说明", "",  18f, new Vector2(0, 122), new Vector2(530,  40f), G.Muted);
            _resultStats = Text(card, "统计", "", 22, new Vector2(0, 36), new Vector2(540, 76), G.Ink);
            _nextButton = Button(card, "下一关", "去下一座花园", new Vector2(0, - 80f), new Vector2(330,  50f + 6), () => OnNextLevel?.Invoke(), G.Gold);
            Button(card, "重试本关", "再试一次", new Vector2(-145, -153), new Vector2(260,  50f), () => OnRestartClicked?.Invoke(), G.Cream);
            Button(card, "选择关卡", "选择关卡", new Vector2(145, -153), new Vector2(260,  50f), () => OnOpenLevelSelect?.Invoke(), G.Cream);
        }

        private GameObject Panel(string name, Color color, bool block)
        {
            var tr = Rect(_canvasRoot, name, Vector2.zero, Vector2.zero);
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            var image = tr.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = block;
            return tr.gameObject;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var tr = (RectTransform)go.transform; tr.SetParent(parent, false);
            tr.anchorMin = tr.anchorMax = anchor ?? new Vector2(.5f, .5f);
            tr.pivot = new Vector2(.5f, .5f); tr.anchoredPosition = pos; tr.sizeDelta = size;
            return tr;
        }
        private RectTransform Card(Transform parent, string name, Vector2 pos, Vector2 size, Color fill, bool shadow = true, Vector2? anchor = null)
        {
            if (shadow) Picture(parent, name + "_shadow", G.Plate(G.Alpha(G.Ink, .17f), false), pos + new Vector2(3, -5), size, anchor, true);
            var face = Picture(parent, name, G.Plate(fill), pos, size, anchor, true);
            return face.rectTransform;
        }
        private Image Picture(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, Vector2? anchor = null, bool sliced = false)
        {
            var tr = Rect(parent, name, pos, size, anchor);
            var img = tr.gameObject.AddComponent<Image>(); img.sprite = sprite; img.color = Color.white;
            img.raycastTarget = false; img.type = sliced ? Image.Type.Sliced : Image.Type.Simple; img.preserveAspect = !sliced;
            return img;
        }
        private TextMeshProUGUI Text(Transform parent, string name, string content, float fontSize, Vector2 pos, Vector2 size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, Vector2? anchor = null)
        {
            var tr = Rect(parent, name, pos, size, anchor);
            var text = tr.gameObject.AddComponent<TextMeshProUGUI>();
            if (_cfg.uiFont != null) text.font = _cfg.uiFont;
            text.text = GameLocalization.Text(content); text.fontSize = fontSize; text.fontSizeMax = fontSize;
            text.fontSizeMin = UiTextFitPolicy.MinimumFontSize(fontSize, size);
            text.enableAutoSizing = true; text.enableWordWrapping = true; text.alignment = align; text.color = color; text.raycastTarget = false;
            // Never silently replace localized copy with "...". Auto-size and wrap first;
            // if a future translation still exceeds its box, keep it visible for QA.
            text.overflowMode = TextOverflowModes.Overflow;
            if (!string.IsNullOrEmpty(content)) _localizedTexts[text] = content;
            return text;
        }
        private GameObject Button(Transform parent, string name, string label, Vector2 pos, Vector2 size, Action clicked, Color fill, Vector2? anchor = null)
        {
            var tr = Card(parent, name, pos, size, fill, false, anchor);
            var img = tr.GetComponent<Image>(); img.raycastTarget = true;
            var btn = tr.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .98f, .88f);
            colors.pressedColor = new Color(.87f, .89f, .79f); colors.disabledColor = new Color(.75f, .75f, .70f, .65f);
            colors.fadeDuration = .09f; btn.colors = colors;
            btn.onClick.AddListener(() => { _audio?.Play(SfxId.Click); clicked?.Invoke(); ClearSelection(); });
            tr.gameObject.AddComponent<GardenButtonFeedback>();
            float labelSize = size.x <= 60f ? 16f : (size.y > 60 ? 24f : 20f);
            Text(tr, "文本", label, labelSize, Vector2.zero, UiTextFitPolicy.ButtonTextBounds(size), G.Ink).fontStyle = FontStyles.Bold;
            return tr.gameObject;
        }
        private static void ClearSelection() { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
        private void Decorate(Transform parent, bool showTopmark = true)
        {
            Picture(parent, "Flower_L", G.Icon(7), new Vector2(-559, -251), new Vector2(66, 96));
            Picture(parent, "Grass_R", G.Icon(6), new Vector2(548, -270), new Vector2( 96f,  60f));
            if (showTopmark)
                Text(parent, "Topmark", "·  G R O W  ·", 15, new Vector2(0, 324), new Vector2(320,  30f), G.Muted);
        }

        public void ShowTitle()
        {
            _titlePanel.SetActive(true); _hudPanel.SetActive(false); _pausePanel.SetActive(false); _settingsPanel.SetActive(false); _resultPanel.SetActive(false); _levelSelectPanel.SetActive(false); _firstLevelTutorialPanel.SetActive(false); _unlockAllConfirmPanel.SetActive(false);
            ShowGrowthHint(false); ClearSelection();
        }
        public void ShowGame()
        {
            _titlePanel.SetActive(false); _hudPanel.SetActive(true); _pausePanel.SetActive(false); _settingsPanel.SetActive(false); _resultPanel.SetActive(false); _levelSelectPanel.SetActive(false); _firstLevelTutorialPanel.SetActive(false); _unlockAllConfirmPanel.SetActive(false);
            ShowGrowthHint(false);
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            if (_invalidRoutine != null) StopCoroutine(_invalidRoutine);
            _toastStrip.SetActive(false); _invalidStrip.SetActive(false); ClearSelection();
        }
        public void ShowFirstLevelTutorial() { _firstLevelTutorialPanel.SetActive(true); ClearSelection(); }
        public void ShowPause(bool show) { _pausePanel.SetActive(show); if (!show) ClearSelection(); }
        public void ShowResult(bool victory, string stats, bool hasNext, bool completedAllAdventures)
        {
            ShowGrowthHint(false);
            _resultPanel.SetActive(true);
            _resultTitle.text = GameLocalization.Text(victory ? (completedAllAdventures ? "恭喜你完成了所有冒险" : "又长大了一点") : "人被杀，就会死");
            _resultTitle.color = victory ? G.Moss : G.Coral;
            _resultCaption.text = GameLocalization.Text(victory ? "花园已通过，带上好奇心继续出发。" : "换个方向，或许就有新的可能。");
            _resultStats.text = stats;
            _nextButton.SetActive(victory && hasNext);
        }
        public void SetHearts(int hp, int maxHp)
        {
            BuildHeartSlots(maxHp);
            for (int i = 0; i < _hearts.Length; i++)
                _hearts[i].color = i < hp ? Color.white : new Color(.54f, .57f, .51f, .3f);
        }

        private void BuildHeartSlots(int maxHp)
        {
            maxHp = Mathf.Max(1, maxHp);
            if (_hearts != null && _hearts.Length == maxHp)
                return;

            if (_hearts != null)
                foreach (var heart in _hearts)
                    if (heart != null)
                    {
                        heart.gameObject.SetActive(false);
                        Destroy(heart.gameObject);
                    }

            _hearts = new Image[maxHp];
            if (_heartParent == null)
                return;
            for (int i = 0; i < _hearts.Length; i++)
                _hearts[i] = Picture(_heartParent, "生命_" + i, G.Heart(),
                    new Vector2((i - (_hearts.Length - 1) * .5f) * 32, 15), new Vector2(29, 29));
        }
        public void SetRoomInfo(string roomText, string goal, string growthText)
        {
            _lastRoomText = roomText; _lastGoal = goal; _lastGrowthText = growthText;
            _roomText.text = roomText.Replace("/", " / ");
            _goalText.text = GameLocalization.Text(goal);
            _growthText.text = growthText;
        }
        public void ShowGrowthHint(bool show)
        {
            if (_growthStrip != null) _growthStrip.SetActive(show);
            if (show && _toastStrip != null) _toastStrip.SetActive(false);
            if (!show && _storeButton != null) _storeButton.SetActive(false);
        }
        public void SetGrowthHint(string text) { if (_growthHint != null) _growthHint.text = GameLocalization.Text(text); }
        public void SetGrowthNoCandidates(bool none, bool mandatory = false, int mandatoryCredits = 0)
        {
            _storeButton.SetActive(none && !mandatory);
            if (none) SetGrowthHint(mandatory
                ? GameLocalization.MandatoryGrowth(mandatoryCredits)
                : GameLocalization.Text("当前空间不足，成长已保留"));
        }
        public void SetCombatInputEnabled(bool enabled) { foreach (var b in _combatButtons) if (b != null) b.interactable = enabled; }
        public void ShowToast(string message, float duration = 3.5f)
        {
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastText.text = GameLocalization.Text(message);
            _toastStrip.SetActive(!_growthStrip.activeSelf);
            _toastRoutine = StartCoroutine(Fade(_toastText, _toastStrip, duration));
        }
        public void ShowInvalid(string reason)
        {
            if (_invalidRoutine != null) StopCoroutine(_invalidRoutine);
            _invalidText.text = GameLocalization.InvalidAction(reason); _invalidStrip.SetActive(true);
            _invalidRoutine = StartCoroutine(Fade(_invalidText, _invalidStrip, 1.6f));
        }
        private IEnumerator Fade(TMP_Text text, GameObject strip, float hold)
        {
            text.alpha = 1; float t = 0;
            while (t < hold) { t += Time.deltaTime; yield return null; }
            t = 0;
            while (t < .3f) { t += Time.deltaTime; text.alpha = 1f - t / .3f; yield return null; }
            text.text = ""; text.alpha = 1; strip.SetActive(false);
        }
    }
}
