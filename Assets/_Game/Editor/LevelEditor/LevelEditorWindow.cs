using System.Collections.Generic;
using System.Linq;
using SwordGame;
using UnityEditor;
using UnityEngine;

namespace SwordGame.EditorTools
{
    /// <summary>可视化关卡编辑器（文档 19）：RoomDefinition 为唯一数据源；
    /// 画墙 / 地面 / 玩家 / 敌人 / 果 / 出口 / 核心，校验、试玩、列表排序。</summary>
    public class LevelEditorWindow : EditorWindow
    {
        [MenuItem("Tools/剑会变长/Level Editor")]
        public static void Open() => GetWindow<LevelEditorWindow>("Level Editor");

        private enum Tool { Select, Wall, Floor, Player, Enemy, Fruit, MultiFruit, Exit, Core, Box, PressurePlate }

        private RoomDefinition _room;
        private Tool _tool = Tool.Wall;
        private EnemyKind _enemyKind = EnemyKind.Charger;
        private int _paintFacing;
        private int _multiFruitGrowthValue = 2;
        private float _zoom = 34f;
        private Vector2 _pan = new Vector2(24f, 24f);
        private bool _panning;
        private Vector2 _listScroll;
        private Vector2 _issueScroll;
        private List<RoomValidator.Issue> _issues = new List<RoomValidator.Issue>();
        private GameConfig _cfg;
        private Vector2Int _hoverCell = new Vector2Int(-1, -1);
        private Vector2Int _selectedCell = new Vector2Int(-1, -1);
        private LevelCellObject _selectedObject = LevelCellObject.None;
        private bool _canvasFocused;
        private string _editMessage;
        private bool _painting;
        private int _newRoomSeq = 1;
        private Vector2 _propScroll;

        private void OnEnable()
        {
            _cfg = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Game/Data/Resources/CFG_Game_Default.asset");
            if (_cfg == null)
                _cfg = ScriptableObject.CreateInstance<GameConfig>();
            var levels = LevelAssetOperations.LoadListLevels();
            _room = levels.FirstOrDefault();
            Undo.willFlushUndoRecord += Repaint;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.willFlushUndoRecord -= Repaint;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            if (_room != null)
            {
                _issues = RoomValidator.QuickValidate(_room, _cfg);
                if (_selectedObject != LevelCellObject.None && LevelCellEditing.Find(_room, _selectedCell) != _selectedObject)
                {
                    _selectedObject = LevelCellObject.None;
                    _selectedCell = new Vector2Int(-1, -1);
                }
            }
            Repaint();
        }

        private bool Dirty => _room != null && EditorUtility.IsDirty(_room);

        private void OnGUI()
        {
            DrawToolbar();
            if (_room == null)
            {
                EditorGUILayout.HelpBox("使用「新建」或左侧列表选择一个关卡。", MessageType.Info);
                if (GUILayout.Button("新建关卡", GUILayout.Height(32)))
                    NewRoom();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawLevelList();
            EditorGUILayout.BeginVertical();
            DrawCanvas();
            DrawIssues();
            EditorGUILayout.EndVertical();
            DrawProperties();
            EditorGUILayout.EndHorizontal();
        }

        // ---- 工具栏 ----

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var levels = LevelAssetOperations.LoadListLevels();
            var names = levels.Select((l, i) => (i + 1) + ". " + l.roomId).ToArray();
            int cur = levels.FindIndex(l => l == _room);
            int next = EditorGUILayout.Popup(Mathf.Max(0, cur), names, EditorStyles.toolbarPopup, GUILayout.Width(190));
            if (next != cur && next >= 0 && next < levels.Count)
            {
                if (ConfirmDiscard())
                    SelectRoom(levels[next]);
            }

            if (GUILayout.Button("新建", EditorStyles.toolbarButton)) NewRoom();
            if (GUILayout.Button("复制", EditorStyles.toolbarButton)) DuplicateRoom();
            if (GUILayout.Button("保存", EditorStyles.toolbarButton)) SaveRoom();
            if (GUILayout.Button("校验", EditorStyles.toolbarButton)) { _issues = RoomValidator.QuickValidate(_room, _cfg); }
            GUI.enabled = !EditorApplication.isPlaying;
            if (GUILayout.Button("▶ 保存并试玩本关", EditorStyles.toolbarButton, GUILayout.Width(130)))
                Playtest();
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            // v1.4：果数 = 本关成长预算，仅提示不限制（文档 19.9）
            int fruitBudget = _room.fruitSpawns.Sum(f => Mathf.Max(1, f.growthValue));
            EditorGUILayout.LabelField($"果 {_room.fruitSpawns.Count}（成长 {fruitBudget} 次）", EditorStyles.miniLabel, GUILayout.Width(135));
            if (Dirty)
                EditorGUILayout.LabelField("● 未保存", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLevelList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(170));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Width(170));
            foreach (var room in LevelAssetOperations.LoadListLevels())
            {
                var r = room;
                bool selected = r == _room;
                var style = selected ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                var bg = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
                if (GUILayout.Button(r.roomId, style, GUILayout.Height(26)))
                    if (ConfirmDiscard())
                        SelectRoom(r);
                GUI.backgroundColor = bg;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("↑", EditorStyles.miniButtonLeft))
                    LevelAssetOperations.MoveInList(r, -1);
                if (GUILayout.Button("↓", EditorStyles.miniButtonRight))
                    LevelAssetOperations.MoveInList(r, 1);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---- 画布 ----

        private void DrawCanvas()
        {
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.10f, 0.10f, 0.12f));

                // 地板与墙
                for (int y = _room.height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < _room.width; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        var r = CellRect(rect, cell);
                        if (!rect.Overlaps(r))
                            continue;
                        bool wall = _room.wallCells.Contains(cell);
                        EditorGUI.DrawRect(r, wall ? new Color(0.45f, 0.33f, 0.22f) : new Color(0.16f, 0.16f, 0.19f));
                        // 网格线
                        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), new Color(0, 0, 0, 0.25f));
                        EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), new Color(0, 0, 0, 0.25f));

                        // 实体
                        var center = new Vector2(r.center.x, r.center.y);
                        if (room_HasEnemyAt(cell))
                            DrawDot(center, 0.8f, new Color(0.9f, 0.35f, 0.25f), r);
                        var fruit = _room.fruitSpawns.FirstOrDefault(f => f.cell == cell);
                        if (_room.fruitSpawns.Any(f => f.cell == cell))
                        {
                            DrawDot(center, 0.5f, new Color(1f, 0.75f, 0.2f), r);
                            int value = Mathf.Max(1, fruit.growthValue);
                            if (value >= 2)
                            {
                                var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                                GUI.Label(new Rect(r.x, r.y + r.height * .08f, r.width, r.height * .72f), value.ToString(), style);
                            }
                        }
                        if (_room.hasCore && _room.coreCell == cell)
                            DrawDot(center, 0.9f, new Color(0.7f, 0.4f, 1f), r);
                        if (_room.pressurePlateCells.Contains(cell))
                            DrawDot(center, 0.62f, new Color(0.95f, 0.72f, 0.22f), r);
                        if (_room.boxSpawns.Contains(cell))
                        {
                            var br = new Rect(center.x-r.width*.26f, center.y-r.height*.26f, r.width*.52f, r.height*.52f);
                            EditorGUI.DrawRect(br, new Color(.68f,.43f,.2f));
                            DrawOutline(br, new Color(.95f,.72f,.38f), 2f);
                        }
                        if (room_EffectiveExit == cell)
                        {
                            var er = new Rect(center.x - r.width * 0.2f, center.y - r.height * 0.2f, r.width * 0.4f, r.height * 0.4f);
                            EditorGUI.DrawRect(er, RoomClearedVisual() ? new Color(0.35f, 1f, 0.5f) : new Color(0.3f, 0.5f, 0.4f));
                        }
                    }
                }

                if (_selectedObject != LevelCellObject.None)
                {
                    var selectedRect = CellRect(rect, _selectedCell);
                    DrawOutline(selectedRect, new Color(0.25f, 0.85f, 1f), 3f);
                }

                // 主出生 + 剑轮廓
                if (_room.playerSpawns.Count > 0)
                {
                    var sp = _room.playerSpawns[0];
                    var r = CellRect(rect, sp.cell);
                    EditorGUI.DrawRect(new Rect(r.center.x - r.width * 0.25f, r.center.y - r.height * 0.25f, r.width * 0.5f, r.height * 0.5f),
                        new Color(0.4f, 0.7f, 1f));
                    // 0.5+0.3 格初始剑只会占到玩家正前方一格。
                    var swordRect = CellRect(rect, LevelCellEditing.InitialSwordCell(sp));
                    EditorGUI.DrawRect(new Rect(swordRect.x + 6, swordRect.y + 6, swordRect.width - 12, swordRect.height - 12),
                        new Color(0.9f, 0.95f, 1f, 0.35f));
                }

                // 悬停格
                if (_hoverCell.x >= 0 && _hoverCell.x < _room.width && _hoverCell.y >= 0 && _hoverCell.y < _room.height)
                {
                    var r = CellRect(rect, _hoverCell);
                    var c = IsPlacementLegal(_hoverCell) ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.45f);
                    if (_tool != Tool.Select && _tool != Tool.Player && _tool != Tool.Exit && _tool != Tool.Core && _tool != Tool.Enemy)
                        EditorGUI.DrawRect(r, c);
                    DrawOutline(r, Color.white, 2f);
                }

                GUI.Label(new Rect(rect.x + 6, rect.yMax - 22, 260, 20),
                    $"格 {_hoverCell.x},{_hoverCell.y} · 缩放 {_zoom:0} · 工具 {_tool}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(_editMessage))
                    GUI.Label(new Rect(rect.x + 270, rect.yMax - 22, rect.width - 276, 20), _editMessage, EditorStyles.miniLabel);
            }

            HandleCanvasEvents(rect);
        }

        private static bool RoomClearedVisual() => false;

        private static void DrawOutline(Rect r, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - width, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, width, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - width, r.y, width, r.height), color);
        }

        private bool room_HasEnemyAt(Vector2Int cell) => _room.enemySpawns.Any(e => e.cell == cell);

        private Vector2Int room_EffectiveExit => _room.exitCell;

        private void DrawDot(Vector2 center, float k, Color color, Rect cellRect)
        {
            var r = new Rect(center.x - cellRect.width * k * 0.35f, center.y - cellRect.height * k * 0.35f,
                cellRect.width * k * 0.7f, cellRect.height * k * 0.7f);
            EditorGUI.DrawRect(r, color);
        }

        private Rect CellRect(Rect canvas, Vector2Int cell)
        {
            float px = canvas.x + _pan.x + cell.x * _zoom;
            float py = canvas.y + _pan.y + (_room.height - 1 - cell.y) * _zoom; // Y 轴翻转：格 y 向上
            return new Rect(px, py, _zoom, _zoom);
        }

        private bool ScreenToCell(Rect canvas, Vector2 mouse, out Vector2Int cell)
        {
            cell = new Vector2Int(-1, -1);
            float fx = Mathf.FloorToInt((mouse.x - canvas.x - _pan.x) / _zoom);
            float fy = _room.height - 1 - Mathf.FloorToInt((mouse.y - canvas.y - _pan.y) / _zoom);
            var c = new Vector2Int((int)fx, (int)fy);
            if (c.x < 0 || c.x >= _room.width || c.y < 0 || c.y >= _room.height)
                return false;
            cell = c;
            return true;
        }

        private void HandleCanvasEvents(Rect rect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
                _canvasFocused = false;
            if (e.type == EventType.KeyDown && _canvasFocused && !EditorGUIUtility.editingTextField)
            {
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                {
                    DeleteSelection();
                    e.Use();
                    return;
                }
                if ((e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E) && _selectedObject == LevelCellObject.Player)
                {
                    RotateSelectedPlayer(e.keyCode == KeyCode.E ? 1 : -1);
                    e.Use();
                    return;
                }
            }
            if (!rect.Contains(e.mousePosition) && e.type != EventType.MouseDrag)
                return;

            switch (e.type)
            {
                case EventType.MouseMove:
                    if (ScreenToCell(rect, e.mousePosition, out var hover))
                        _hoverCell = hover;
                    Repaint();
                    break;

                case EventType.MouseDown:
                    if (e.button == 2)
                    {
                        _panning = true;
                        e.Use();
                        break;
                    }
                    if (e.button != 0 && e.button != 1)
                        break;
                    _canvasFocused = true;
                    if (ScreenToCell(rect, e.mousePosition, out var downCell))
                    {
                        _painting = true;
                        if (_tool == Tool.Select)
                        {
                            SelectAt(downCell);
                            if (e.button == 1)
                                DeleteSelection();
                        }
                        else
                        {
                            Undo.RecordObject(_room, "Paint " + _tool);
                            PaintAt(downCell, e.button == 1);
                        }
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (_panning)
                    {
                        _pan += e.delta;
                        e.Use();
                        break;
                    }
                    if (_painting && (e.button == 0 || e.button == 1) && ScreenToCell(rect, e.mousePosition, out var dragCell))
                    {
                        if (_tool == Tool.Wall || _tool == Tool.Floor)
                            PaintAt(dragCell, e.button == 1);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp:
                    _painting = false;
                    _panning = false;
                    Repaint();
                    break;

                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        _zoom = Mathf.Clamp(_zoom - e.delta.y * 2f, 14f, 80f);
                        e.Use();
                    }
                    break;
            }
        }

        private bool IsPlacementLegal(Vector2Int cell)
        {
            var found = LevelCellEditing.Find(_room, cell);
            switch (_tool)
            {
                case Tool.Select: return found != LevelCellObject.None;
                case Tool.Floor: return found == LevelCellObject.Wall && !LevelCellEditing.IsBoundary(_room, cell);
                case Tool.Wall: return found == LevelCellObject.Wall || LevelCellEditing.CanPlace(_room, LevelCellObject.Wall, cell, LevelCellObject.None, default, out _);
                case Tool.Enemy: return found == LevelCellObject.Enemy || LevelCellEditing.CanPlace(_room, LevelCellObject.Enemy, cell, LevelCellObject.None, default, out _);
                case Tool.Fruit:
                case Tool.MultiFruit: return found == LevelCellObject.Fruit || LevelCellEditing.CanPlace(_room, LevelCellObject.Fruit, cell, LevelCellObject.None, default, out _);
                case Tool.Player:
                    var oldPlayer = _room.playerSpawns.Count > 0 ? _room.playerSpawns[0].cell : new Vector2Int(-1, -1);
                    return LevelCellEditing.CanPlace(_room, LevelCellObject.Player, cell, LevelCellObject.Player, oldPlayer, _paintFacing, out _);
                case Tool.Exit: return LevelCellEditing.CanPlace(_room, LevelCellObject.Exit, cell, LevelCellObject.Exit, _room.exitCell, out _);
                case Tool.Core: return LevelCellEditing.CanPlace(_room, LevelCellObject.Core, cell, LevelCellObject.Core, _room.coreCell, out _);
                case Tool.Box: return found == LevelCellObject.Box || LevelCellEditing.CanPlace(_room, LevelCellObject.Box, cell, LevelCellObject.None, default, out _);
                case Tool.PressurePlate: return found == LevelCellObject.PressurePlate || LevelCellEditing.CanPlace(_room, LevelCellObject.PressurePlate, cell, LevelCellObject.None, default, out _);
                default: return false;
            }
        }

        private void PaintAt(Vector2Int cell, bool erase)
        {
            if (cell.x < 0 || cell.x >= _room.width || cell.y < 0 || cell.y >= _room.height)
                return;

            bool changed = false;
            switch (_tool)
            {
                case Tool.Wall:
                    if (erase)
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Wall, cell, out _editMessage);
                    else if (_room.wallCells.Contains(cell))
                        return;
                    else if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Wall, cell, LevelCellObject.None, default, out _editMessage))
                        return;
                    else
                    {
                        _room.wallCells.Add(cell);
                        changed = true;
                    }
                    break;
                case Tool.Floor:
                    if (!_room.wallCells.Contains(cell))
                        return;
                    if (cell.x == 0 || cell.y == 0 || cell.x == _room.width - 1 || cell.y == _room.height - 1)
                    {
                        _editMessage = "固定边界墙不可删除";
                        return;
                    }
                    changed = _room.wallCells.Remove(cell);
                    break;
                case Tool.Player:
                    if (erase)
                    {
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Player, cell, out _editMessage);
                        break;
                    }
                    if (_room.playerSpawns.Count == 0)
                    {
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Player, cell, LevelCellObject.None, default, _paintFacing, out _editMessage)) return;
                        _room.playerSpawns.Add(new SpawnCandidate { cell = cell, facing = _paintFacing });
                        changed = true;
                    }
                    else
                    {
                        var sp = _room.playerSpawns[0];
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Player, cell, LevelCellObject.Player, sp.cell, _paintFacing, out _editMessage)) return;
                        changed = sp.cell != cell || sp.facing != _paintFacing;
                        sp.cell = cell;
                        sp.facing = _paintFacing;
                        _room.playerSpawns[0] = sp;
                    }
                    SelectAt(cell);
                    break;
                case Tool.Enemy:
                    if (erase)
                    {
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Enemy, cell, out _editMessage);
                        break;
                    }
                    if (LevelCellEditing.Find(_room, cell) == LevelCellObject.Enemy)
                        changed = LevelCellEditing.SetEnemyKind(_room, cell, _enemyKind);
                    else
                    {
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Enemy, cell, LevelCellObject.None, default, out _editMessage)) return;
                        _room.enemySpawns.Add(new EnemySpawnDef { cell = cell, kind = _enemyKind });
                        changed = true;
                    }
                    SelectAt(cell);
                    break;
                case Tool.Fruit:
                case Tool.MultiFruit:
                    if (erase)
                    {
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Fruit, cell, out _editMessage);
                        break;
                    }
                    int growthValue = _tool == Tool.MultiFruit ? Mathf.Clamp(_multiFruitGrowthValue, 2, 9) : 1;
                    if (LevelCellEditing.Find(_room, cell) != LevelCellObject.Fruit)
                    {
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Fruit, cell, LevelCellObject.None, default, out _editMessage)) return;
                        _room.fruitSpawns.Add(new RoomDefinition.FruitSpawnDef { fruitId = NextFruitId(cell), cell = cell, growthValue = growthValue });
                        changed = true;
                    }
                    else
                        changed = LevelCellEditing.SetFruitGrowthValue(_room, cell, growthValue, out _editMessage);
                    SelectAt(cell);
                    break;
                case Tool.Exit:
                    if (erase)
                    {
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Exit, cell, out _editMessage);
                        break;
                    }
                    if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Exit, cell, LevelCellObject.Exit, _room.exitCell, out _editMessage)) return;
                    changed = _room.exitCell != cell;
                    _room.exitCell = cell;
                    SelectAt(cell);
                    break;
                case Tool.Core:
                    if (erase)
                    {
                        changed = LevelCellEditing.Delete(_room, LevelCellObject.Core, cell, out _editMessage);
                        break;
                    }
                    if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Core, cell, LevelCellObject.Core, _room.coreCell, out _editMessage)) return;
                    changed = !_room.hasCore || _room.coreCell != cell;
                    _room.hasCore = true;
                    _room.coreCell = cell;
                    SelectAt(cell);
                    break;
                case Tool.Box:
                    if (erase) changed = LevelCellEditing.Delete(_room, LevelCellObject.Box, cell, out _editMessage);
                    else if (!_room.boxSpawns.Contains(cell))
                    {
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.Box, cell, LevelCellObject.None, default, out _editMessage)) return;
                        _room.boxSpawns.Add(cell); changed = true;
                    }
                    SelectAt(cell);
                    break;
                case Tool.PressurePlate:
                    if (erase) changed = LevelCellEditing.Delete(_room, LevelCellObject.PressurePlate, cell, out _editMessage);
                    else if (!_room.pressurePlateCells.Contains(cell))
                    {
                        if (!LevelCellEditing.CanPlace(_room, LevelCellObject.PressurePlate, cell, LevelCellObject.None, default, out _editMessage)) return;
                        _room.pressurePlateCells.Add(cell); changed = true;
                    }
                    SelectAt(cell);
                    break;
            }
            if (changed)
            {
                EditorUtility.SetDirty(_room);
                _issues = RoomValidator.QuickValidate(_room, _cfg);
                _editMessage = erase ? "已删除" : "已修改";
            }
            if (erase && _selectedCell == cell)
            {
                _selectedCell = new Vector2Int(-1, -1);
                _selectedObject = LevelCellObject.None;
            }
            Repaint();
        }

        private string NextFruitId(Vector2Int cell)
        {
            string stem = $"f_{cell.x}_{cell.y}";
            string id = stem;
            int suffix = 2;
            while (_room.fruitSpawns.Any(f => f.fruitId == id))
                id = stem + "_" + suffix++;
            return id;
        }

        private void SelectAt(Vector2Int cell)
        {
            _selectedCell = cell;
            _selectedObject = LevelCellEditing.Find(_room, cell);
            _editMessage = _selectedObject == LevelCellObject.None
                ? $"已选择空地 {cell}"
                : $"已选择{LevelCellEditing.Name(_selectedObject)} {cell}";
            Repaint();
        }

        private void DeleteSelection()
        {
            if (_selectedObject == LevelCellObject.None)
            {
                _editMessage = "请先选择要删除的对象";
                Repaint();
                return;
            }
            Undo.RecordObject(_room, "Delete " + _selectedObject);
            if (LevelCellEditing.Delete(_room, _selectedObject, _selectedCell, out var reason))
            {
                EditorUtility.SetDirty(_room);
                _issues = RoomValidator.QuickValidate(_room, _cfg);
                _editMessage = "已删除" + LevelCellEditing.Name(_selectedObject);
                _selectedObject = LevelCellObject.None;
                _selectedCell = new Vector2Int(-1, -1);
            }
            else
                _editMessage = reason;
            Repaint();
        }

        private void RotateSelectedPlayer(int quarterTurns)
        {
            int index = _room.playerSpawns.FindIndex(p => p.cell == _selectedCell);
            if (index < 0) return;
            Undo.RecordObject(_room, "Rotate player spawn");
            var player = _room.playerSpawns[index];
            int nextFacing = (player.facing + quarterTurns + 4) % 4;
            if (LevelCellEditing.TrySetPlayerFacing(_room, _selectedCell, nextFacing, out var reason))
            {
                _paintFacing = nextFacing;
                EditorUtility.SetDirty(_room);
                _editMessage = "已旋转玩家与初始剑身朝向";
            }
            else
                _editMessage = reason;
            Repaint();
        }

        // ---- 属性栏 ----

        private void DrawProperties()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230));
            _propScroll = EditorGUILayout.BeginScrollView(_propScroll, GUILayout.Width(230));

            EditorGUILayout.LabelField("关卡属性", EditorStyles.boldLabel);
            string roomId = EditorGUILayout.TextField("roomId", _room.roomId);
            EditorGUILayout.LabelField("左侧目标说明（“这一关的目标”下方）");
            string goalText = EditorGUILayout.TextArea(_room.goalText, GUILayout.MinHeight(46));
            if (roomId != _room.roomId || goalText != _room.goalText)
            {
                Undo.RecordObject(_room, "Edit room properties");
                _room.roomId = roomId;
                _room.goalText = goalText;
                MarkEdited("已修改关卡属性");
            }
            int displayedHp = _room.ResolveInitialPlayerHp(_cfg);
            EditorGUI.BeginChangeCheck();
            int initialHp = Mathf.Max(1, EditorGUILayout.IntField("本关玩家初始生命", displayedHp));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_room, "Change room player health");
                _room.initialPlayerHp = initialHp;
                MarkEdited("已修改本关玩家初始生命");
            }
            EditorGUILayout.LabelField($"尺寸 {_room.width}×{_room.height}");
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _room.width > 2;
            if (GUILayout.Button("宽-1"))
                ConfirmResize(_room.width - 1, _room.height);
            GUI.enabled = _room.height > 2;
            if (GUILayout.Button("高-1"))
                ConfirmResize(_room.width, _room.height - 1);
            GUI.enabled = true;
            if (GUILayout.Button("宽+1"))
                ConfirmResize(_room.width + 1, _room.height);
            if (GUILayout.Button("高+1"))
                ConfirmResize(_room.width, _room.height + 1);
            EditorGUILayout.EndHorizontal();

            var objectiveValues = new[]
            {
                RoomDefinition.LevelObjective.ReachExit,
                RoomDefinition.LevelObjective.ClearEnemiesAndExit,
                RoomDefinition.LevelObjective.PushBoxesToPlatesAndExit,
                RoomDefinition.LevelObjective.ClearEnemiesAndPlatesAndExit,
            };
            var objectiveNames = new[] { "直接离开", "清除敌人", "打开机关", "清敌＋机关" };
            int objectiveIndex = System.Array.IndexOf(objectiveValues, _room.EffectiveObjective);
            if (objectiveIndex < 0) objectiveIndex = 0;
            int nextObjectiveIndex = EditorGUILayout.Popup("目标类型", objectiveIndex, objectiveNames);
            if (nextObjectiveIndex != objectiveIndex)
            {
                Undo.RecordObject(_room, "Change level objective");
                _room.objective = objectiveValues[nextObjectiveIndex];
                _room.MarkMigrated();
                MarkEdited("已修改目标类型");
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("工具", EditorStyles.boldLabel);
            _tool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "选择", "墙", "地面", "玩家" }, GUILayout.Height(24));
            _tool2Toolbar();
            _tool3Toolbar();
            EditorGUILayout.Space(4);
            _enemyKind = (EnemyKind)EditorGUILayout.EnumPopup("敌人类别", _enemyKind);
            if (_tool == Tool.MultiFruit)
                _multiFruitGrowthValue = Mathf.Clamp(EditorGUILayout.IntField("多果成长次数", _multiFruitGrowthValue), 2, 9);
            EditorGUILayout.LabelField("玩家 / 初始剑方向", EditorStyles.miniLabel);
            _paintFacing = GUILayout.Toolbar(_paintFacing, new[] { "东", "北", "西", "南" });

            DrawSelectedProperties();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"问题（{_issues.Count}）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_issues.Count == 0 ? "使用「校验」检查当前关。" : "点击问题以定位", MessageType.None);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedProperties()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("选中格属性", EditorStyles.boldLabel);
            if (_selectedObject == LevelCellObject.None)
            {
                EditorGUILayout.HelpBox("选择工具：左键选中对象，右键快速删除。选中后也可按 Delete。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("对象", LevelCellEditing.Name(_selectedObject));
            Vector2Int targetCell = EditorGUILayout.Vector2IntField("格坐标", _selectedCell);
            if (targetCell != _selectedCell)
            {
                Undo.RecordObject(_room, "Move " + _selectedObject);
                if (LevelCellEditing.TryMove(_room, _selectedObject, _selectedCell, targetCell, out var reason))
                {
                    _selectedCell = targetCell;
                    MarkEdited("已移动" + LevelCellEditing.Name(_selectedObject));
                }
                else
                    _editMessage = reason;
            }

            if (_selectedObject == LevelCellObject.Enemy)
            {
                int index = _room.enemySpawns.FindIndex(e => e.cell == _selectedCell);
                if (index >= 0)
                {
                    var nextKind = (EnemyKind)EditorGUILayout.EnumPopup("怪物类型", _room.enemySpawns[index].kind);
                    if (nextKind != _room.enemySpawns[index].kind)
                    {
                        Undo.RecordObject(_room, "Change enemy type");
                        LevelCellEditing.SetEnemyKind(_room, _selectedCell, nextKind);
                        _enemyKind = nextKind;
                        MarkEdited("已修改怪物类型");
                    }
                }
            }
            else if (_selectedObject == LevelCellObject.Fruit)
            {
                int index = _room.fruitSpawns.FindIndex(f => f.cell == _selectedCell);
                if (index >= 0)
                {
                    string nextId = EditorGUILayout.DelayedTextField("fruitId", _room.fruitSpawns[index].fruitId);
                    if (nextId != _room.fruitSpawns[index].fruitId)
                    {
                        Undo.RecordObject(_room, "Edit fruit ID");
                        if (LevelCellEditing.SetFruitId(_room, _selectedCell, nextId, out var reason))
                            MarkEdited("已修改 fruitId");
                        else
                            _editMessage = reason;
                    }
                    int currentValue = Mathf.Max(1, _room.fruitSpawns[index].growthValue);
                    int nextValue = Mathf.Clamp(EditorGUILayout.IntField("提供生长次数", currentValue), 1, 9);
                    if (nextValue != currentValue)
                    {
                        Undo.RecordObject(_room, "Edit fruit growth value");
                        if (LevelCellEditing.SetFruitGrowthValue(_room, _selectedCell, nextValue, out var reason))
                            MarkEdited("已修改果的成长次数");
                        else
                            _editMessage = reason;
                    }
                }
            }
            else if (_selectedObject == LevelCellObject.Player)
            {
                int index = _room.playerSpawns.FindIndex(p => p.cell == _selectedCell);
                if (index >= 0)
                {
                    int facing = GUILayout.Toolbar(_room.playerSpawns[index].facing, new[] { "东", "北", "西", "南" });
                    if (facing != _room.playerSpawns[index].facing)
                    {
                        Undo.RecordObject(_room, "Change player facing");
                        if (LevelCellEditing.TrySetPlayerFacing(_room, _selectedCell, facing, out var reason))
                        {
                            _paintFacing = facing;
                            MarkEdited("已修改玩家与初始剑身朝向");
                        }
                        else
                            _editMessage = reason;
                    }
                }
            }

            GUI.enabled = !(_selectedObject == LevelCellObject.Wall && LevelCellEditing.IsBoundary(_room, _selectedCell));
            if (GUILayout.Button("删除选中对象", GUILayout.Height(26)))
                DeleteSelection();
            GUI.enabled = true;
        }

        private void MarkEdited(string message)
        {
            EditorUtility.SetDirty(_room);
            _issues = RoomValidator.QuickValidate(_room, _cfg);
            _editMessage = message;
            Repaint();
        }

        private void _tool2Toolbar()
        {
            var names = new[] { "怪", "果", "多果", "出口", "核心" };
            int v = GUILayout.Toolbar(
                _tool == Tool.Enemy ? 0 : _tool == Tool.Fruit ? 1 : _tool == Tool.MultiFruit ? 2 : _tool == Tool.Exit ? 3 : _tool == Tool.Core ? 4 : -1,
                names, GUILayout.Height(24));
            _tool = v switch
            {
                0 => Tool.Enemy,
                1 => Tool.Fruit,
                2 => Tool.MultiFruit,
                3 => Tool.Exit,
                4 => Tool.Core,
                _ => _tool,
            };
        }

        private void _tool3Toolbar()
        {
            int v = GUILayout.Toolbar(_tool == Tool.Box ? 0 : _tool == Tool.PressurePlate ? 1 : -1,
                new[] { "箱子", "机关" }, GUILayout.Height(24));
            if (v == 0) _tool = Tool.Box;
            else if (v == 1) _tool = Tool.PressurePlate;
        }

        private void DrawIssues()
        {
            EditorGUILayout.BeginVertical(GUILayout.Height(110));
            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll);
            foreach (var issue in _issues)
            {
                var color = issue.severity == "Error" ? new Color(1f, 0.5f, 0.4f) : new Color(1f, 0.85f, 0.4f);
                var before = GUI.color;
                GUI.color = color;
                if (GUILayout.Button($"[{issue.severity}] {issue.code}: {issue.message}" + (issue.cell.HasValue ? $" @ {issue.cell.Value}" : ""), EditorStyles.miniButton))
                {
                    if (issue.cell.HasValue)
                        CenterOn(issue.cell.Value);
                }
                GUI.color = before;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void CenterOn(Vector2Int cell)
        {
            _hoverCell = cell;
            _selectedCell = cell;
            _selectedObject = LevelCellEditing.Find(_room, cell);
            Repaint();
        }

        // ---- 操作 ----

        private void SelectRoom(RoomDefinition room)
        {
            _room = room;
            _selectedObject = LevelCellObject.None;
            _selectedCell = new Vector2Int(-1, -1);
            _editMessage = null;
            _issues = RoomValidator.QuickValidate(_room, _cfg);
            Repaint();
        }

        private void NewRoom()
        {
            if (!ConfirmDiscard())
                return;
            _room = LevelAssetOperations.CreateNew();
            _selectedObject = LevelCellObject.None;
            _selectedCell = new Vector2Int(-1, -1);
            _editMessage = null;
            _newRoomSeq++;
            _issues = RoomValidator.QuickValidate(_room, _cfg);
            Repaint();
        }

        private void DuplicateRoom()
        {
            if (_room == null)
                return;
            var copy = LevelAssetOperations.Duplicate(_room);
            if (copy != null)
                SelectRoom(copy);
        }

        private void SaveRoom()
        {
            _issues = RoomValidator.QuickValidate(_room, _cfg);
            if (LevelAssetOperations.Save(_room))
                Debug.Log($"[LevelEditor] 已保存 {_room.roomId}（{_issues.Count} 项校验结果）");
        }

        private void ConfirmResize(int w, int h)
        {
            if (w < 2 || h < 2)
            {
                _editMessage = "地图最小尺寸为 2×2";
                return;
            }
            int removed = 0;
            var temp = Instantiate(_room);
            var impact = LevelAssetOperations.ApplyResize(temp, w, h, out removed);
            for (int x = 0; x < w; x++)
                foreach (var y in new[] { 0, h - 1 })
                    if (temp.wallCells.Contains(new Vector2Int(x, y)) && !_room.wallCells.Contains(new Vector2Int(x, y)) &&
                        (x >= _room.width || y >= _room.height))
                        impact.Add($"新增边界墙 @ {new Vector2Int(x, y)}");
            DestroyImmediate(temp);

            if (removed > 0 || impact.Count > 0)
            {
                var msg = "将清理越界对象：\n" + string.Join("\n", impact.Take(6).ToArray()) + (impact.Count > 6 ? "\n..." : "") + $"\n清理 {removed} 个实体。继续？";
                if (!EditorUtility.DisplayDialog("调整尺寸", msg, "确认", "取消"))
                    return;
            }
            Undo.RecordObject(_room, "Resize");
            LevelAssetOperations.ApplyResize(_room, w, h, out _);
            if (_selectedCell.x >= w || _selectedCell.y >= h)
            {
                _selectedCell = new Vector2Int(-1, -1);
                _selectedObject = LevelCellObject.None;
            }
            MarkEdited($"已调整地图为 {w}×{h}");
        }

        private void Playtest()
        {
            _issues = RoomValidator.QuickValidate(_room, _cfg);
            if (_issues.Any(i => i.severity == "Error"))
            {
                EditorUtility.DisplayDialog("无法试玩", "存在硬错误，请先处理问题列表中的红色项。", "好");
                return;
            }
            LevelAssetOperations.Save(_room);
            if (!LevelPlaytestLauncher.Launch(_room))
                EditorUtility.DisplayDialog("无法试玩", "试玩启动失败（场景未保存或已在 Play 中）。", "好");
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty)
                return true;
            var choice = EditorUtility.DisplayDialogComplex("未保存的修改", "当前关卡有未保存修改。", "保存", "取消", "丢弃");
            if (choice == 0)
            {
                LevelAssetOperations.Save(_room);
                return true;
            }
            if (choice == 2)
            {
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(_room), ImportAssetOptions.ForceSynchronousImport);
                _room = AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GetAssetPath(_room));
                return true;
            }
            return false;
        }
    }
}
