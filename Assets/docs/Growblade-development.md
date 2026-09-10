# Growblade /《剑会变长》开发文档

## 修改履历

| 文档版本 | 日期 | 内容 |
| --- | --- | --- |
| 1.13.0-doc1 | 2026-09-10 | 发布 v1.13.0：加入四语言与可扩展设置界面，修复多语言字体和排版，精简 HUD，补充 WebGL 构建发布流程。 |
| 1.12.0-doc5 | 2026-09-10 | 补充 Codex 通过 Codely MCP 桥接团结编辑器的方法、调试工具入口、验证闭环与故障排查。 |
| 1.12.0-doc4 | 2026-09-10 | 清理弃用代码与未引用资产；12 个关卡统一迁入并按顺序命名于 `Assets/_Game/Data/Rooms`；删除旧奖励、旧目标迁移和废弃成长配置字段；整理本地构建与演示产物。 |
| 1.12.0-doc3 | 2026-09-10 | 以当前代码、配置、12 关资产和 UI 为唯一事实来源重写文档；删除旧版本方案、重复验收记录和已作废规则；合并玩法、编辑器、构建及素材授权说明。 |

本文描述 **v1.13.0** 的实际行为，用于后续维护和关卡制作。若本文与实现出现差异，应先核对代码与序列化资产，再在同一次改动中更新本文。不要从 Git 历史中的旧交接文档恢复玩法。

## 1. 项目概况

- 中文名：《剑会变长》
- 英文名：Growblade
- 参赛项目：《GGJ翌光计划2026》
- 队伍：你说的队
- 作者：海蛋
- 类型：2D 俯视角、格子制、同步回合解谜
- 引擎：团结引擎 1.10.0（编辑器版本 `2022.3.62t12`）
- 渲染：URP 14.2.0-t1
- 正式流程：12 个独立关卡，每关重新使用初始短剑，不跨关继承剑形、生命、果或拍数。

主场景为 `Assets/_Game/Scenes/Game.scene`。场景只提供启动入口，房间、角色、剑、UI 和表现由运行时代码构建。12 个关卡资产统一位于 `Assets/_Game/Data/Rooms/`，按 `ROOM_01_*.asset` 到 `ROOM_12_*.asset` 命名；正式顺序由 `Assets/_Game/Data/Resources/RUN_Default.asset` 决定。

## 2. 操作与一拍流程

| 输入 | 行为 |
| --- | --- |
| W/A/S/D 或方向键 | 向相邻格移动 |
| Q / E | 剑身逆时针 / 顺时针旋转 90° |
| Space | 原地等待一拍 |
| 鼠标移动 | 生长阶段悬停预览合法剑节 |
| 鼠标左键 | 生长阶段确认当前合法剑节 |
| Esc | 打开或关闭暂停菜单 |

屏幕右侧也提供移动、转剑和等待按钮。按住 Shift 再输入移动或转剑会显示内部行动预览，但当前 HUD 不展示这项提示。

一次正常行动按以下顺序执行：

1. 读取一条玩家指令。
2. 模拟并播放玩家移动、转剑或等待。
3. 若本拍取得果，暂停在生长阶段，完成或暂存生长。
4. 恢复同一拍，按 `actorId` 顺序执行仍可行动的敌人。
5. 结算生命、目标、出口和胜负，再生成下一拍意图。

移动、转剑和等待都会经过一拍。玩家平移时若身体或剑碰到墙、边界等非法空间，会先向目标方向前探，再回弹到原位；该行动照常增加一拍，敌人也照常行动。玩家身体试图直接进入存活角色、核心或箱子所在格时，当前实现只播放受阻回弹，不提交拍数。

## 3. 剑、碰撞与攻击

剑是连接边组成的实体图形，碰撞按连续线段胶囊计算，不按“剑占了哪些整格”近似。

- 内部坐标单位为 0.1 格。
- 初始剑由 0.5 格根段和 0.3 格尖段组成；剑根从角色中心前方 0.5 格开始，最远端距角色中心 1.3 格。
- 每次后续生长固定增加一条 0.5 格、上下左右之一的剑节。
- 新剑节可从任意已有节点长出，因此可以形成折角和分叉。
- 剑只在移动、旋转或生长的运动过程中造成伤害；静止重叠不持续攻击。
- 普通剑击伤害 1。可击退目标 1 格；目标被推向墙时追加 1 点撞墙伤害。
- 箱子可被剑击退但不受伤。核心不被击退。

旋转过程中，剑首先碰到墙或怪物时会在接触点停止、结算合法命中，然后反弹回原朝向并经过一拍。平移过程中剑碰到怪物会继续执行向前攻击，不因怪物自动反弹。敌人移动时必须避开剑身的连续碰撞范围，不能进入或穿过剑身。

## 4. 果与生长

普通果提供 1 次生长。数字果在果实上方显示 2–9，提供对应次数的连续生长。

- 玩家身体合法走到果所在格：果被吃掉，玩家停在该格，然后进入生长阶段，不回弹。
- 玩家平移或旋转时由剑先碰到果：剑和玩家先按本次动作回弹到动作前状态，再进入生长阶段。
- 生长阶段直接在游戏世界显示候选线段；悬停合法位置可预览，左键确认。
- 新剑节不能穿墙、越界、穿过玩家、穿过已有节点或与未收集果重叠。
- 存活敌人、箱子和核心不会使生长候选失效；新剑节伸出时会立即按剑击规则命中它们。
- 生长次数、总边数和剑半径没有全局上限。关卡作者用果的数量与数字控制本关可增长量。
- 数字果产生的强制成长点必须全部用完，才能进入下一步行动。
- 普通成长点若当前没有合法位置，可以暂存；角色回到存在合法候选的位置后会重新打开生长阶段。

每次成长以事务提交：预览不改变状态，伸长表现完成后才一次性增加剑节、扣除成长点并提交命中结果。换关、重试或返回选关会取消未完成事务。

## 5. 敌人、核心、箱子与机关

默认配置数值：普通敌人 2 HP，核心 3 HP，玩家受敌人命中时损失 1 HP。每关可在关卡资产中单独设置玩家初始生命；旧资产填 0 时使用默认 4 HP。

### 冲锋怪

冲锋怪每拍使用 BFS 寻找通往玩家的最短合法一步。寻路会避开墙、其他存活实体和剑身连续碰撞范围；存在绕行路径时会绕过剑，而不是停在剑前。

### 弩手

弩手循环为“预备 1 拍 → 射击 1 拍 → 冷却 1 拍”。预备时锁定水平或垂直方向，射线被墙截断；冷却拍不攻击。

### 核心

核心是不可击退、具有生命值的特殊敌人，会交替发出水平和垂直脉冲。它不再对应独立的“摧毁核心”目标；只要关卡要求“清除敌人”，核心和普通敌人都必须被消灭。只清掉普通敌人而核心存活时，出口不能解锁，UI 也不能提示前往出口。

### 箱子与压力机关

箱子不会自行行动，受剑命中时按敌人相同的击退占位规则移动，但不会掉血。箱子实际显示尺寸为 0.85×0.85 格；压力机关显示尺寸为 0.92×0.92 格。要求机关的关卡只有在全部箱子压住全部机关后才满足机关条件，此前出口隐藏。

## 6. 关卡目标与通关

关卡编辑器只提供四种现行目标：

| 编辑器文本 | 实际条件 |
| --- | --- |
| 直接离开 | 出口开局可用；玩家到达出口即通关 |
| 清除敌人 | 消灭全部普通敌人、弩手和核心，再到达出口 |
| 打开机关 | 让全部箱子压住全部机关，再到达出口 |
| 清敌＋机关 | 同时完成清敌和全部机关，再到达出口 |

核心直接属于“清除敌人”的判定，不存在单独的“摧毁核心”目标。所有目标最终都要求玩家站到出口格；区别只在出口何时可用。

游戏首次启动只解锁第一关。通关第 x 关会解锁第 x+1 关，解锁数量使用 PlayerPrefs 键 `sword_will_grow_unlocked_stage_count` 持久化。关卡选择界面中的未解锁关不可点击；右上角“解锁全部”会先弹出确认窗口。通关第 12 关时，胜利标题显示“恭喜你完成了所有冒险”。

## 7. 界面与音频

运行时界面支持 English、简体中文、日本語与한국어；首次启动默认 English，选择通过 PlayerPrefs 键 `growblade_language` 持久化。语言由 `GameLocalization.SupportedLanguages` 数据目录驱动，设置页自动生成选项；新增语言不应修改 HUD 布局代码。标题页显示游戏名、Play、Settings、Quit 和作者署名。第一关开始时显示一次简短教程，说明移动、Q/E 转剑、Space 等待一拍、拾果生长及本关目标。HUD 显示关卡序号、生命、作者填写的目标说明、剑节数、剩余果和待生长次数。

标题页和暂停菜单都可进入同一个设置面板，集中提供语言、音乐音量、音效音量和镜头轻微震动；关闭设置后回到进入前的界面。音乐与音效音量分别存入 PlayerPrefs 的 `sdnf_music` 和 `sdnf_sfx`。

音频系统包含 1 条固定循环 BGM 和 12 类事件音效：挥剑、剑撞墙、命中、击退撞墙、敌人移动、弩手射击、生长、受伤、胜利、失败、按钮点击和玩家移动。胜利或失败乐句播放时会压低 BGM；进入下一关、重试或离开结算流程时停止结算乐句。

UI 使用 `Scale With Screen Size`，参考分辨率为 1280×720，宽高匹配值 0.5。Windows 默认分辨率为 1920×1080；当前 WebGL 模板画布为 960×600。网页嵌入尺寸应与实际 WebGL 画布一致，或启用全屏/响应式缩放，否则会在较大的 1920×1080 iframe 中显示成中央小窗。

## 8. 关卡编辑器

入口：`Tools/剑会变长/Level Editor`。

编辑器直接修改 `RoomDefinition` 并维护 `RUN_Default` 的关卡顺序，支持：

- 新建、复制、保存、上下排序、Undo/Redo、校验和“保存并试玩本关”。
- 选择、移动和删除已有对象；右键快速删除，选中后也可按 Delete。
- 绘制或编辑墙、地面、玩家、冲锋怪、弩手、果、数字果、出口、核心、箱子和机关。
- 设置玩家与初始剑朝向；编辑器只为玩家本体格和朝向前方 1 格保留初始占位。
- 修改 `roomId`、左侧“这一关的目标”文本、本关玩家初始生命和四种目标类型。
- 数字果可设置 2–9 次成长；选中任意果后可编辑 `fruitId` 与 1–9 的成长值。
- 地图宽高每次增加或减少 1，最小 2×2。缩小时会列出并确认将被清理的越界对象，然后重建固定边界墙。
- 校验出生点、对象重叠、出口、核心、箱子/机关、果 ID、成长预算和基本可达性。存在硬错误时禁止一键试玩。

新关默认 13×11、4 HP、“直接离开”，带边界墙、一个朝东的玩家出生点和一个出口。新建关会自动加入 `RUN_Default`；最终发布顺序以编辑器左侧列表为准。

## 9. 数据与代码入口

| 路径 | 作用 |
| --- | --- |
| `Assets/_Game/Scripts/Core/BeatSimulator.cs` | 玩家阶段、果触发、命中与拍模拟 |
| `Assets/_Game/Scripts/Core/TurnDirector.cs` | 输入门、分段拍、成长暂停与敌人阶段恢复 |
| `Assets/_Game/Scripts/Core/RunController.cs` | 关卡载入、解锁、目标、胜负与成长提交 |
| `Assets/_Game/Scripts/Growth/` | 剑图、候选、放置过滤与成长攻击 |
| `Assets/_Game/Scripts/Combat/` | 敌人规划、寻路、碰撞与击退 |
| `Assets/_Game/Scripts/UI/HUDController.cs` | 标题、教程、HUD、暂停、选关与结算 UI |
| `Assets/_Game/Editor/LevelEditor/` | 关卡编辑器、校验和试玩入口 |
| `Assets/_Game/Data/Resources/CFG_Game_Default.asset` | 当前数值、美术和字体配置 |
| `Assets/_Game/Data/Rooms/ROOM_01_*.asset` 至 `ROOM_12_*.asset` | 12 个正式关卡资产 |
| `Assets/_Game/Data/Resources/RUN_Default.asset` | 12 关正式顺序 |

运行时模型与 GameObject 表现分离。碰撞、成长候选、目标和解锁规则应优先在纯逻辑层修改，并同步对应 EditMode 测试。当前测试目录包含 99 个 `[Test]` / `[TestCase]` 声明，覆盖几何、拍模拟、果、成长、箱子、目标、弩手节奏、移动碰撞、音频、解锁和编辑器格子操作。

### Codex 与团结编辑器 MCP 调试

本项目可以让 Codex 通过 Codely MCP 直接读取和控制团结编辑器，用于编译、Console 检查、场景与资产检查、Play Mode 运行时诊断、输入模拟和画面捕获。它与游戏代码中的 `PlaytestBridge` 无关：前者连接外部 AI 与编辑器，后者只负责关卡编辑器的一键试玩参数传递。

项目已具备以下桥接组件：

- `Packages/manifest.json` 中的 `cn.tuanjie.codely.bridge`，当前版本为 `1.0.80`。
- 全局 Codely CLI `@unity-china/codely-cli`，本机验证版本为 `1.0.0-rc.58`。
- 项目根目录运行时生成的 `.com-unity-codely.json` 握手文件。该文件记录动态端口与心跳，不应手工修改、复制端口或提交到 Git。

在新的 Codex 环境中，将下面配置加入用户级 `~/.codex/config.toml`，并把命令和项目路径改成当前机器的实际路径：

```toml
[mcp_servers.tuanjie-editor]
command = 'C:\Users\<用户名>\AppData\Roaming\npm\codely.cmd'
args = ["serve", "unity-mcp", "--stdio", "--unity-project-path", 'C:\path\to\WuHanGGJ']
default_tools_approval_mode = "writes"
```

若机器尚未安装 CLI，可使用 `npm install -g @unity-china/codely-cli`。配置完成后启动团结编辑器并打开本项目，等待导入和编译结束，再重新启动 Codex 任务以加载 `mcp__tuanjie_editor__*` 工具。首次连接应调用 `unity_editor` 的 `get_state`，确认活动场景为 `Assets/_Game/Scenes/Game.scene`、`isCompiling=false`，且返回的渲染管线为 URP。本项目已在 2026-09-10 实际通过该调用验证连接成功。

常用工具如下：

| MCP 工具 | 用途 |
| --- | --- |
| `unity_editor` | 读取状态、等待空闲、触发完整 C# 编译流程、控制 Play Mode |
| `unity_console` | 清空并读取 Console，确认本次操作产生的错误与警告 |
| `unity_scene`、`unity_gameobject`、`unity_asset` | 检查或修改场景、层级、组件和资产 |
| `exec_editor_script` | 在 Edit Mode 执行使用 `UnityEditor` API 的诊断或批量编辑脚本 |
| `exec_runtime_script` | 自动进入 Play Mode，通过游戏 API 检查运行时状态和行为 |
| `unity_gameview`、`unity_screenshot` | 设置 Game View 分辨率，并完成静态或录制式视觉检查 |

每次调试遵循同一闭环：

1. `unity_editor.get_state` 读取真实状态，随后 `unity_console.clear` 建立新的日志边界，并用 `unity_editor.wait_for_idle` 等待导入结束。
2. 编辑代码或资产。优先让逻辑测试返回结构化数据，不要只凭截图判断碰撞、状态或可见性。
3. 修改 C# 后调用 `unity_editor.start_compilation_pipeline`，再调用 `unity_console.get` 检查这一轮编译产生的 Console 信息。
4. 涉及运行时行为时使用 `exec_runtime_script` 调用游戏现有 API 验证；动画、输入和时序问题通过同一次运行时脚本触发并录制。继续编辑时使用 `exec_editor_script`，它会自动回到 Edit Mode。
5. 结束前确认场景保存状态、运行房间、Console 和相关 EditMode 测试；视觉改动最后再截取 Game View 检查。

连接失败时先确认团结编辑器仍打开且未卡在编译，检查 `.com-unity-codely.json` 的 `reason` 是否为 `ready`、`last_heartbeat` 是否持续更新。编辑器菜单 `AI > Check Connections` 可检查 Bridge 连接。端口冲突时让 Bridge 自动重建握手信息，不要在 Codex 配置中写死 `unity_port`。若 Codex 没有出现 `mcp__tuanjie_editor__*` 工具，检查用户级 TOML 配置、`codely.cmd serve unity-mcp --stdio --unity-project-path <项目路径>` 是否可启动，然后重启 Codex 任务。

## 10. 构建与发布

- 产品名和可执行文件：`Growblade` / `Growblade.exe`
- Windows 输出目录：`Builds/Windows/`
- WebGL 输出目录：`Builds/WebGL/`
- 当前工程版本：`1.13.0`
- v1.13.0 WebGL 发布构建：成功，入口场景为 `Assets/_Game/Scenes/Game.scene`；发布包根目录保持 `index.html`、`Build/`、`TemplateData/`，并在模板 CSS 中隐藏团结引擎页脚。
- v1.12.0 Windows 64 位构建记录：成功，入口场景为 `Assets/_Game/Scenes/Game.scene`，BuildReport 汇总大小 124,969,270 bytes。

发布 Windows 版时必须一起保留 `Growblade.exe`、`Growblade_Data`、`TuanjiePlayer.dll`、运行时 DLL 和 `Licenses/NotoSansSC-OFL.txt`，不能只分发 EXE。公开发布包和仓库版本号应同时更新 `ProjectSettings/ProjectSettings.asset`、README、CHANGELOG 与 Release 标题。

## 11. 美术、音乐、字体与授权

### 正式美术

- 骑士、冲锋怪、弩手、核心、果、出口、草和花位于 `Assets/_Game/Art/Garden/Resources/DoodleAtlas.png`。该透明图集于 2026-09-05 使用 OpenAI imagegen 按本项目角色描述生成，实际提示词保存在 `Assets/_Game/Art/Garden/GENERATION.md`。
- 箱子与机关位于 `Assets/_Game/Art/Garden/Mechanisms/Box_v1.png` 和 `PressurePlate_v1.png`，同样使用 OpenAI imagegen 为本项目生成。
- UI 卡片、按钮、地砖、墙格、爱心、箭头、剑、轨迹和预览由项目代码与引擎绘制，不使用外部图片素材。

### 音乐与音效

正式音频均为 CC0 1.0：BGM 和胜负乐句来自 MintoDog 在 OpenGameArt 发布的 Cozy Puzzle 系列；短音效来自 Kenney 的 RPG Audio、Impact Sounds 和 Interface Sounds。逐文件映射、原始文件名、来源链接和许可见 `Assets/_Game/Audio/THIRD_PARTY_AUDIO.md`。

### 字体

中文与日文默认使用 Noto Sans SC Regular，项目文件位于 `Assets/Codely/Fonts/`。韩文使用 `Assets/_Game/Resources/Fonts/NotoSansKR-VF.ttf`，运行时创建动态 TMP 字体以覆盖完整韩文字形。两者均采用 SIL Open Font License 1.1；公开源码及可提取字体的发行包必须保留对应许可证文本。

### 项目许可证

除单独声明的第三方内容外，项目原创代码、文档与原创游戏内容按仓库根目录 `LICENSE.md` 的 CC BY-NC-SA 4.0 发布。新增外部素材前必须记录作者、原始名称、来源链接、许可证、项目路径和修改方式；只有“免费”而没有明确许可证的素材不能进入发布包。

建议发布页署名：

> Art: original project artwork created with OpenAI image generation; UI and terrain graphics generated by project code.
> Music: “Cozy Puzzle In-Game 1” and “Cozy Puzzle Jingle & Result” by MintoDog, CC0 1.0 via OpenGameArt.
> Sound effects: RPG Audio, Impact Sounds and Interface Sounds by Kenney, CC0 1.0.
> Font: Noto Sans SC Regular, SIL Open Font License 1.1.

## 12. 后续修改检查

任何玩法改动至少同步检查以下内容：

1. 纯逻辑实现、表现事件和 UI 文案是否仍描述同一行为。
2. `RoomDefinition`、编辑器选项和校验器是否一致。
3. 对应 EditMode 测试是否需要更新或新增。
4. 12 个正式关卡是否仍可载入，出生点和出口条件是否有效。
5. 本文、README、CHANGELOG、版本号和发布包是否同步。
6. 新增素材的来源和许可证是否已登记并随包保留。
