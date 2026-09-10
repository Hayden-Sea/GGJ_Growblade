using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    // Persisted values: append new languages; never reorder existing ones.
    public enum GameLanguage { English = 0, SimplifiedChinese = 1, Japanese = 2, Korean = 3 }

    public sealed class LanguageDefinition
    {
        public readonly GameLanguage language;
        public readonly string nativeName;
        public readonly IReadOnlyDictionary<string, string> text, roomGoals;
        public readonly string fruit, growth, growthSelected, growthPreview, mandatoryGrowth, noBeat, stats, lockedMark;

        public LanguageDefinition(GameLanguage language, string nativeName, Dictionary<string, string> text,
            Dictionary<string, string> roomGoals,
            string fruit, string growth, string growthSelected, string growthPreview,
            string mandatoryGrowth, string noBeat, string stats, string lockedMark)
        {
            this.language = language; this.nativeName = nativeName; this.text = text;
            this.roomGoals = roomGoals; this.fruit = fruit; this.growth = growth;
            this.growthSelected = growthSelected; this.growthPreview = growthPreview;
            this.mandatoryGrowth = mandatoryGrowth; this.noBeat = noBeat; this.stats = stats; this.lockedMark = lockedMark;
        }
    }

    /// <summary>Data-driven runtime language catalog. UI enumerates SupportedLanguages automatically.</summary>
    public static class GameLocalization
    {
        public const string LanguagePrefsKey = "growblade_language";
        private static Dictionary<string, string> M(params string[] p) { var d = new Dictionary<string, string>(); for (int i = 0; i + 1 < p.Length; i += 2) d[p[i]] = p[i + 1]; return d; }

        private static readonly Dictionary<string, string> En = M(
            "剑会变长","GROWBLADE","剑会长大。\n路，要想好。","The blade grows.\nPlan your path.","by 海蛋","by Hayden Sea","开始冒险","PLAY","设置","SETTINGS","退出游戏","QUIT",
            "一把剑，很多种可能","One blade, endless possibilities","拿起短剑，走进这座小小花园。","Take up your blade and enter the little garden.","选择一段冒险","CHOOSE AN ADVENTURE","每一关，都从一把短剑开始。","Every adventure begins with a short blade.",
            "返回","BACK","解锁全部","UNLOCK ALL","尚未解锁","LOCKED","完成前一关后开放","Complete the previous stage","上一页","PREVIOUS","下一页","NEXT","还没有关卡，请先在关卡编辑器中添加。","No stages are available.","解锁全部关卡？","UNLOCK ALL STAGES?","这会立即开放所有冒险。","This immediately opens every adventure.","再想想","CANCEL","全部解锁","UNLOCK ALL",
            "正在探索","EXPLORING","这一关的目标","OBJECTIVE","暂停","PAUSE","成长手记","GROWTH NOTES","每次行动，世界走一拍","Every action advances the world","待","WAIT","WASD 移动    Q / E 转剑    Space 等待","WASD Move    Q / E Rotate    Space Wait",
            "移动鼠标预览 · 左键生长","Move the cursor to preview · Left-click to grow","空间不足 · 暂存成长","NO SPACE · SAVE GROWTH","歇一小会儿","TAKE A BREATHER","花园会在这里等你。","The garden will wait for you.","继续冒险","CONTINUE","重试本关","RESTART","选择关卡","STAGE SELECT",
            "音乐","MUSIC","音效","SOUND EFFECTS","镜头轻微震动","SCREEN SHAKE","欢迎来到花园","WELCOME TO THE GARDEN","每次行动，世界也走一拍。","Every action advances the world by one beat.","移动  WASD","MOVE  WASD","向前走，剑身会击退敌人。","Move forward to strike and push enemies.","转剑  Q / E","ROTATE  Q / E","转动 90°；撞墙会回弹。","Rotate 90°; walls make the blade rebound.",
            "果实会让剑生长","FRUIT MAKES THE BLADE GROW","碰到果实后，移动鼠标预览，再用左键确认生长。","Collect fruit, preview with the cursor, then left-click to grow.","Space 等待，经过一拍。","Press Space to wait one beat.","这一关：拾取果实，清理敌人，前往出口。","Collect fruit, clear the enemies, and reach the exit.",
            "又长大了一点","THE BLADE GREW","恭喜你完成了所有冒险","ALL ADVENTURES COMPLETE","人被杀，就会死","THE ADVENTURE ENDS HERE","花园已通过，带上好奇心继续出发。","Garden cleared. Carry your curiosity onward.","换个方向，或许就有新的可能。","Try another direction and find a new possibility.","去下一座花园","NEXT GARDEN","再试一次","TRY AGAIN","设置选项","SETTINGS","语言","LANGUAGE","关闭","BACK",
            "机关已启动","PLATES ACTIVATED","敌人已清除，机关已启动","ENEMIES CLEARED · PLATES ACTIVATED","房间已清理","ROOM CLEARED","机关已启动：出口出现了。","Plates activated. The exit has appeared.","敌人已清除，机关已启动：出口出现了。","Enemies cleared and plates activated. The exit has appeared.","房间已清理：前往出口继续。","Room cleared. Head to the exit.",
            "当前空间不足，成长已保留","No room here. Growth has been saved.","该位置无法生长","Cannot grow here","没有可生长的候选","No growth option is available","候选已过期","That growth option has expired","内部状态缺失","Internal state is unavailable","没有可用成长点","No growth points available","位置非法","Invalid position","墙体阻挡","Blocked by a wall","穿过角色","Cannot pass through the player","与果重叠","Cannot overlap fruit",
            "身体会撞墙","Your body would hit a wall","剑会被墙挡住","The blade is blocked by a wall","先把目标格清开","Clear the target tile first","超出地图","Outside the board","未知","Unknown","你行动一拍，敌人行动一拍。","You act for one beat, then the enemies act.","碰到果可以生长。用剑砍果时，先回弹再生长。","Collect fruit to grow. Striking fruit makes the blade rebound first.","只有你动剑时才会攻击。","The blade attacks only while you move it.","把敌人推向墙会追加 1 点伤害。","Push an enemy into a wall for 1 extra damage."
        );

        private static readonly Dictionary<string, string> Ja = M(
            "剑会变长","グロウブレイド","剑会长大。\n路，要想好。","剣は伸びる。\n道をよく考えよう。","by 海蛋","制作：海蛋","开始冒险","冒険を始める","设置","設定","退出游戏","ゲームを終了",
            "一把剑，很多种可能","一本の剣、無限の可能性","拿起短剑，走进这座小小花园。","短い剣を手に、小さな庭へ踏み出そう。","选择一段冒险","冒険を選ぶ","每一关，都从一把短剑开始。","どのステージも短い剣から始まる。","返回","戻る","解锁全部","すべて解放","尚未解锁","未解放","完成前一关后开放","前のステージをクリアしよう","上一页","前へ","下一页","次へ","还没有关卡，请先在关卡编辑器中添加。","ステージがありません。","解锁全部关卡？","全ステージを解放しますか？","这会立即开放所有冒险。","すべての冒険がすぐに遊べます。","再想想","キャンセル","全部解锁","すべて解放",
            "正在探索","探索中","这一关的目标","目標","暂停","ポーズ","成长手记","成長メモ","每次行动，世界走一拍","行動するたび世界も1拍進む","待","待機","WASD 移动    Q / E 转剑    Space 等待","WASD 移動    Q / E 回転    Space 待機","移动鼠标预览 · 左键生长","マウスで確認・左クリックで成長","空间不足 · 暂存成长","空きなし・成長を保留",
            "歇一小会儿","ひと休み","花园会在这里等你。","庭はここで待っている。","继续冒险","続ける","重试本关","やり直す","选择关卡","ステージ選択","音乐","音楽","音效","効果音","镜头轻微震动","画面の揺れ","欢迎来到花园","庭へようこそ","每次行动，世界也走一拍。","行動するたび、世界も1拍進みます。","移动  WASD","移動  WASD","向前走，剑身会击退敌人。","前へ進み、剣で敵を押し返そう。","转剑  Q / E","剣を回す  Q / E","转动 90°；撞墙会回弹。","90°回転。壁に当たると跳ね返る。",
            "果实会让剑生长","果実で剣が伸びる","碰到果实后，移动鼠标预览，再用左键确认生长。","果実を取り、マウスで確認して左クリックで伸ばそう。","Space 等待，经过一拍。","Spaceで1拍待機。","这一关：拾取果实，清理敌人，前往出口。","果実を取り、敵を倒して出口へ向かおう。","又长大了一点","剣が伸びた","恭喜你完成了所有冒险","すべての冒険を達成！","人被杀，就会死","冒険はここで終わる","花园已通过，带上好奇心继续出发。","庭を突破。好奇心を持って先へ進もう。","换个方向，或许就有新的可能。","別の方向に新しい可能性があるかも。","去下一座花园","次の庭へ","再试一次","もう一度","设置选项","設定","语言","言語","关闭","戻る",
            "机关已启动","仕掛け作動","敌人已清除，机关已启动","敵を撃破・仕掛け作動","房间已清理","エリアクリア","机关已启动：出口出现了。","仕掛けが作動し、出口が現れた。","敌人已清除，机关已启动：出口出现了。","敵を倒し仕掛けが作動。出口が現れた。","房间已清理：前往出口继续。","エリアクリア。出口へ進もう。",
            "当前空间不足，成长已保留","ここでは伸ばせません。成長は保留されます。","该位置无法生长","ここには伸ばせない","没有可生长的候选","伸ばせる場所がない","候选已过期","候補が無効になった","内部状态缺失","内部状態がありません","没有可用成长点","成長ポイントがない","位置非法","無効な位置","墙体阻挡","壁に阻まれている","穿过角色","プレイヤーを通過できない","与果重叠","果実と重ねられない","身体会撞墙","体が壁に当たる","剑会被墙挡住","剣が壁に阻まれる","先把目标格清开","移動先を空けよう","超出地图","盤面の外です","未知","不明",
            "你行动一拍，敌人行动一拍。","あなたが1拍動くと、敵も1拍動く。","碰到果可以生长。用剑砍果时，先回弹再生长。","果実で成長できる。剣で取ると、跳ね返ってから伸びる。","只有你动剑时才会攻击。","剣は動かした時だけ攻撃する。","把敌人推向墙会追加 1 点伤害。","敵を壁にぶつけると追加で1ダメージ。"
        );

        private static readonly Dictionary<string, string> Ko = M(
            "剑会变长","그로우블레이드","剑会长大。\n路，要想好。","검은 자란다.\n길을 잘 생각하자.","by 海蛋","제작: 海蛋","开始冒险","모험 시작","设置","설정","退出游戏","게임 종료","一把剑，很多种可能","하나의 검, 무한한 가능성","拿起短剑，走进这座小小花园。","짧은 검을 들고 작은 정원으로 떠나자.","选择一段冒险","모험 선택","每一关，都从一把短剑开始。","모든 스테이지는 짧은 검에서 시작한다.","返回","뒤로","解锁全部","모두 해제","尚未解锁","잠김","完成前一关后开放","이전 스테이지를 완료하세요","上一页","이전","下一页","다음","还没有关卡，请先在关卡编辑器中添加。","스테이지가 없습니다.","解锁全部关卡？","모든 스테이지를 해제할까요?","这会立即开放所有冒险。","모든 모험이 즉시 열립니다.","再想想","취소","全部解锁","모두 해제",
            "正在探索","탐험 중","这一关的目标","목표","暂停","일시정지","成长手记","성장 기록","每次行动，世界走一拍","행동할 때마다 세계도 한 박자 진행","待","대기","WASD 移动    Q / E 转剑    Space 等待","WASD 이동    Q / E 회전    Space 대기","移动鼠标预览 · 左键生长","마우스로 미리보기 · 왼쪽 클릭으로 성장","空间不足 · 暂存成长","공간 부족 · 성장 보관","歇一小会儿","잠시 쉬어 가기","花园会在这里等你。","정원은 여기서 기다립니다.","继续冒险","계속하기","重试本关","다시 시작","选择关卡","스테이지 선택","音乐","음악","音效","효과음","镜头轻微震动","화면 흔들림",
            "欢迎来到花园","정원에 온 것을 환영합니다","每次行动，世界也走一拍。","행동할 때마다 세계도 한 박자 움직입니다.","移动  WASD","이동  WASD","向前走，剑身会击退敌人。","앞으로 이동해 검으로 적을 밀어내세요.","转剑  Q / E","검 회전  Q / E","转动 90°；撞墙会回弹。","90° 회전하며 벽에 닿으면 튕겨 나옵니다.","果实会让剑生长","열매를 먹으면 검이 자랍니다","碰到果实后，移动鼠标预览，再用左键确认生长。","열매를 먹고 마우스로 확인한 뒤 왼쪽 클릭으로 성장하세요.","Space 等待，经过一拍。","Space로 한 박자 기다립니다.","这一关：拾取果实，清理敌人，前往出口。","열매를 먹고 적을 처치한 뒤 출구로 가세요.",
            "又长大了一点","검이 자랐습니다","恭喜你完成了所有冒险","모든 모험 완료!","人被杀，就会死","모험은 여기서 끝납니다","花园已通过，带上好奇心继续出发。","정원을 통과했습니다. 호기심을 품고 계속 나아가세요.","换个方向，或许就有新的可能。","다른 방향에서 새로운 가능성을 찾아보세요.","去下一座花园","다음 정원","再试一次","다시 도전","设置选项","설정","语言","언어","关闭","뒤로","机关已启动","장치 작동","敌人已清除，机关已启动","적 처치 · 장치 작동","房间已清理","구역 완료","机关已启动：出口出现了。","장치가 작동해 출구가 나타났습니다.","敌人已清除，机关已启动：出口出现了。","적을 처치하고 장치를 작동했습니다. 출구가 나타났습니다.","房间已清理：前往出口继续。","구역 완료. 출구로 이동하세요.",
            "当前空间不足，成长已保留","지금은 공간이 부족합니다. 성장이 보관됩니다.","该位置无法生长","여기서는 성장할 수 없습니다","没有可生长的候选","성장 가능한 위치가 없습니다","候选已过期","성장 후보가 만료되었습니다","内部状态缺失","내부 상태가 없습니다","没有可用成长点","성장 포인트가 없습니다","位置非法","잘못된 위치","墙体阻挡","벽에 막혔습니다","穿过角色","플레이어를 통과할 수 없습니다","与果重叠","열매와 겹칠 수 없습니다","身体会撞墙","몸이 벽에 부딪힙니다","剑会被墙挡住","검이 벽에 막힙니다","先把目标格清开","목표 칸을 먼저 비우세요","超出地图","보드 밖입니다","未知","알 수 없음",
            "你行动一拍，敌人行动一拍。","당신이 한 박자 움직이면 적도 한 박자 움직입니다.","碰到果可以生长。用剑砍果时，先回弹再生长。","열매로 성장합니다. 검으로 치면 먼저 튕긴 뒤 성장합니다.","只有你动剑时才会攻击。","검은 움직일 때만 공격합니다.","把敌人推向墙会追加 1 点伤害。","적을 벽으로 밀면 추가 피해 1을 줍니다."
        );

        private static readonly Dictionary<string, string> EnGoals = M("ROOM_Guide","Collect fruit, clear the enemy, and reach the exit.","ROOM_Rotate","Find the right angle.","ROOM_Move","Move up and down.","ROOM_Fight","Clear the enemies.","ROOM_Box","Push the box onto the plate.","ROOM_DoubleBox","Activate every pressure plate.","ROOM_Hook","Shape the blade into a hook.","ROOM_Defense","Survive and clear the enemies.","ROOM_Core","Destroy the core and reach the exit.","ROOM_BoxBox","Solve the box puzzle.","ROOM_Wait","Learn when to wait.","ROOM_BBox","Complete the final garden.");
        private static readonly Dictionary<string, string> JaGoals = M("ROOM_Guide","果実を取り、敵を倒して出口へ。","ROOM_Rotate","正しい角度を見つけよう。","ROOM_Move","上下に動こう。","ROOM_Fight","敵をすべて倒そう。","ROOM_Box","箱を仕掛けまで押そう。","ROOM_DoubleBox","すべての仕掛けを作動させよう。","ROOM_Hook","剣をフック形に伸ばそう。","ROOM_Defense","生き残って敵を倒そう。","ROOM_Core","コアを壊して出口へ。","ROOM_BoxBox","箱のパズルを解こう。","ROOM_Wait","待つタイミングを学ぼう。","ROOM_BBox","最後の庭をクリアしよう。");
        private static readonly Dictionary<string, string> KoGoals = M("ROOM_Guide","열매를 먹고 적을 처치한 뒤 출구로 가세요.","ROOM_Rotate","올바른 각도를 찾으세요.","ROOM_Move","위아래로 이동하세요.","ROOM_Fight","모든 적을 처치하세요.","ROOM_Box","상자를 발판으로 미세요.","ROOM_DoubleBox","모든 발판을 작동하세요.","ROOM_Hook","검을 갈고리 모양으로 만드세요.","ROOM_Defense","살아남아 적을 처치하세요.","ROOM_Core","코어를 파괴하고 출구로 가세요.","ROOM_BoxBox","상자 퍼즐을 푸세요.","ROOM_Wait","기다릴 때를 배우세요.","ROOM_BBox","마지막 정원을 완료하세요.");

        public static readonly IReadOnlyList<LanguageDefinition> SupportedLanguages = new[] {
            new LanguageDefinition(GameLanguage.English,"English",En,EnGoals,"FRUIT {0}","BLADE {0}\n\nFRUIT {1}\n\nGROWTH {2}","Left-click to grow · Segments {0} · Reach {1:0.0} tiles","Move the cursor to preview · Segments {0} · Reach {1:0.0} tiles","Complete {0} more growth"," · NO BEAT SPENT","Beats taken: {0}\nLongest blade reach: {1:0.0} tiles","·"),
            new LanguageDefinition(GameLanguage.SimplifiedChinese,"简体中文",M(),M(),"果实 {0}","剑节 {0}\n\n果 {1}\n\n待生长 {2}","左键生长 · 总节数 {0} · 最大触及 {1:0.0} 格","移动鼠标预览生长位置 · 总节数 {0} · 最大触及 {1:0.0} 格","必须完成剩余 {0} 次生长"," · 不耗拍","行动了 {0} 拍\n剑身最长长度 {1:0.0} 格","锁"),
            new LanguageDefinition(GameLanguage.Japanese,"日本語",Ja,JaGoals,"果実 {0}","剣節 {0}\n\n果実 {1}\n\n成長 {2}","左クリックで成長・剣節 {0}・射程 {1:0.0} マス","マウスで位置を確認・剣節 {0}・射程 {1:0.0} マス","あと {0} 回成長してください","・拍は進みません","行動：{0} 拍\n剣の最大射程：{1:0.0} マス","鍵"),
            new LanguageDefinition(GameLanguage.Korean,"한국어",Ko,KoGoals,"열매 {0}","검 조각 {0}\n\n열매 {1}\n\n성장 {2}","왼쪽 클릭으로 성장 · 검 조각 {0} · 사거리 {1:0.0}칸","마우스로 위치 확인 · 검 조각 {0} · 사거리 {1:0.0}칸","성장을 {0}회 더 완료하세요"," · 박자 소모 없음","행동: {0}박\n검 최대 사거리: {1:0.0}칸","잠김")
        };

        private static readonly Dictionary<GameLanguage, LanguageDefinition> Catalog = BuildCatalog();
        private static Dictionary<GameLanguage, LanguageDefinition> BuildCatalog() { var d = new Dictionary<GameLanguage, LanguageDefinition>(); foreach (var x in SupportedLanguages) d[x.language] = x; return d; }
        private static LanguageDefinition A => Catalog[Current];
        public static GameLanguage Current { get; private set; } = GameLanguage.English;
        public static void Load() { var value = (GameLanguage)PlayerPrefs.GetInt(LanguagePrefsKey, 0); Current = Catalog.ContainsKey(value) ? value : GameLanguage.English; }
        public static void Set(GameLanguage value) { Current = Catalog.ContainsKey(value) ? value : GameLanguage.English; PlayerPrefs.SetInt(LanguagePrefsKey, (int)Current); PlayerPrefs.Save(); }
        public static string Text(string source) { if (string.IsNullOrEmpty(source)) return source; return A.text.TryGetValue(source, out var value) ? value : source; }
        public static string FruitCount(int n) => string.Format(A.fruit, n);
        public static string GrowthSummary(int edges, int fruits, int credits) => string.Format(A.growth, edges, fruits, credits);
        public static string GrowthPreview(bool selected, int edges, float reach) => string.Format(selected ? A.growthSelected : A.growthPreview, edges, reach);
        public static string MandatoryGrowth(int n) => string.Format(A.mandatoryGrowth, n);
        public static string InvalidAction(string reason) => Text(reason) + A.noBeat;
        public static string ResultStats(int beats, float reach) => string.Format(A.stats, beats, reach);
        public static string LockedMark => A.lockedMark;
        public static List<string> MissingStaticTranslations(GameLanguage language)
        {
            var missing = new List<string>();
            if (!Catalog.TryGetValue(language, out var definition)) return new List<string>(En.Keys);
            if (language == GameLanguage.SimplifiedChinese) return missing;
            foreach (var key in En.Keys) if (!definition.text.ContainsKey(key)) missing.Add(key);
            return missing;
        }
        /// <summary>Stage names are editor-authored English identifiers and are intentionally never localized.</summary>
        public static string RoomName(string roomId)
        {
            return (roomId ?? "").Replace("ROOM_", "").Replace("Room_", "").Replace("_", " ");
        }
        public static string RoomGoal(string roomId, string chinese) { if (A.roomGoals.TryGetValue(roomId ?? "", out var value)) return value; return Current == GameLanguage.SimplifiedChinese ? chinese : (string.IsNullOrWhiteSpace(chinese) ? Text("房间已清理：前往出口继续。") : chinese); }
    }
}
