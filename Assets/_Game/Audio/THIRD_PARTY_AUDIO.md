# 音频素材与许可证

本目录的第三方音频均以 **Creative Commons Zero（CC0 1.0）** 发布，可用于、修改和随游戏再分发。署名并非许可要求，仍在此保留来源，便于 Game Jam 页面说明与后续追溯。

## 音乐

| 游戏用途 | 项目文件 | 原始文件 | 作者 / 来源 | 许可 |
| --- | --- | --- | --- | --- |
| 固定循环 BGM | `Resources/Audio/Music/CozyPuzzleLoop.ogg` | `cozy_puzzle_in-game_1_bpm118.ogg` | MintoDog / OpenGameArt：Cozy Puzzle In-Game 1 | CC0 1.0 |
| 通关 | `Resources/Audio/SFX/Victory/01_ClearJingle.ogg` | `Cozy Puzzle Clear (Jingle).ogg` | MintoDog / OpenGameArt：Cozy Puzzle Jingle & Result | CC0 1.0 |
| 失败 | `Resources/Audio/SFX/Defeat/01_FailureJingle.ogg` | `Cozy Puzzle Failure (Jingle).ogg` | 同上 | CC0 1.0 |

## 音效映射

| SfxId | 含义 | 来源与选用文件 |
| --- | --- | --- |
| BladeSwing | 剑横扫 | Kenney RPG Audio：`knifeSlice`, `knifeSlice2` |
| BladeWall | 剑碰墙并回弹 | Kenney Impact Sounds：`impactMetal_light_000/001` |
| HitFlesh | 剑命中怪物 | Kenney Impact Sounds：`impactSoft_medium_000/002` |
| KnockWall | 怪物被击退撞墙 | Kenney Impact Sounds：`impactWood_medium_001/003` |
| EnemyMove | 怪物移动 | Kenney Impact Sounds：`footstep_grass_001/002/003` |
| EnemyShoot | 弩手射击 | Kenney Interface Sounds：`pluck_001/002` |
| Growth | 拾果 / 生长 | Kenney Interface Sounds：`confirmation_002/003` |
| Hurt | 玩家受伤 | Kenney Impact Sounds：`impactPunch_medium_001/002` |
| Victory | 通关 | MintoDog：`Cozy Puzzle Clear (Jingle)` |
| Defeat | 失败 | MintoDog：`Cozy Puzzle Failure (Jingle)` |
| Click | UI 点击 | Kenney Interface Sounds：`click_002/003/004` |
| PlayerMove | 玩家平移 | Kenney Impact Sounds：`footstep_grass_000/004` |

## 来源链接

- https://opengameart.org/content/cozy-puzzle-in-game-1
- https://opengameart.org/content/cozy-puzzle-jingle-result
- https://kenney.nl/assets/rpg-audio
- https://kenney.nl/assets/impact-sounds
- https://kenney.nl/assets/interface-sounds
- https://creativecommons.org/publicdomain/zero/1.0/

项目文件仅重命名以表达用途，没有改变音频内容。BGM 使用 Unity 的 Streaming/Vorbis 导入，并在播放侧以 0.55 混音增益为短音效留出动态空间；通关 / 失败乐句播放期间会自动压低 BGM。短音效使用 Decompress On Load，以降低触发延迟。
