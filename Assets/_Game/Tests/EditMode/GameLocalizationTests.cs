using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    public class GameLocalizationTests
    {
        private bool _hadLanguage;
        private int _savedLanguage;

        [SetUp]
        public void SetUp()
        {
            _hadLanguage = PlayerPrefs.HasKey(GameLocalization.LanguagePrefsKey);
            _savedLanguage = PlayerPrefs.GetInt(GameLocalization.LanguagePrefsKey, 0);
            PlayerPrefs.DeleteKey(GameLocalization.LanguagePrefsKey);
            GameLocalization.Load();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(GameLocalization.LanguagePrefsKey);
            if (_hadLanguage) PlayerPrefs.SetInt(GameLocalization.LanguagePrefsKey, _savedLanguage);
            GameLocalization.Load();
        }

        [Test]
        public void FirstLaunch_DefaultsToEnglish()
        {
            Assert.AreEqual(GameLanguage.English, GameLocalization.Current);
            Assert.AreEqual("PLAY", GameLocalization.Text("开始冒险"));
        }

        [Test]
        public void SelectedLanguage_IsPersisted()
        {
            GameLocalization.Set(GameLanguage.SimplifiedChinese);
            GameLocalization.Load();
            Assert.AreEqual(GameLanguage.SimplifiedChinese, GameLocalization.Current);
            Assert.AreEqual("开始冒险", GameLocalization.Text("开始冒险"));
        }

        [Test]
        public void EnglishRoomCopy_UsesStableRoomId()
        {
            Assert.AreEqual("Guide", GameLocalization.RoomName("ROOM_Guide"));
            Assert.AreEqual("Learn when to wait.", GameLocalization.RoomGoal("ROOM_Wait", "学会等待"));
        }

        [TestCase(GameLanguage.English)]
        [TestCase(GameLanguage.SimplifiedChinese)]
        [TestCase(GameLanguage.Japanese)]
        [TestCase(GameLanguage.Korean)]
        public void RoomName_AlwaysUsesEditorEnglishIdentifier(GameLanguage language)
        {
            GameLocalization.Set(language);
            Assert.AreEqual("Double Box", GameLocalization.RoomName("ROOM_Double_Box"));
        }

        [Test]
        public void Catalog_ContainsFourStableLanguages()
        {
            Assert.AreEqual(4, GameLocalization.SupportedLanguages.Count);
            Assert.AreEqual(GameLanguage.Japanese, GameLocalization.SupportedLanguages[2].language);
            Assert.AreEqual(GameLanguage.Korean, GameLocalization.SupportedLanguages[3].language);
        }

        [TestCase(GameLanguage.English)]
        [TestCase(GameLanguage.Japanese)]
        [TestCase(GameLanguage.Korean)]
        public void EveryRuntimeKey_HasTranslation(GameLanguage language)
        {
            CollectionAssert.IsEmpty(GameLocalization.MissingStaticTranslations(language));
        }

        [TestCase(GameLanguage.Japanese, "冒険を始める", "果実を取り、敵を倒して出口へ。")]
        [TestCase(GameLanguage.Korean, "모험 시작", "열매를 먹고 적을 처치한 뒤 출구로 가세요.")]
        public void NewLanguages_TranslateUiAndRoomGoals(GameLanguage language, string play, string goal)
        {
            GameLocalization.Set(language);
            Assert.AreEqual(play, GameLocalization.Text("开始冒险"));
            Assert.AreEqual(goal, GameLocalization.RoomGoal("ROOM_Guide", "中文后备"));
        }

        [Test]
        public void KoreanFont_IsBundledAsRuntimeResource()
        {
            Assert.IsNotNull(Resources.Load<Font>("Fonts/NotoSansKR-VF"));
        }
    }
}
