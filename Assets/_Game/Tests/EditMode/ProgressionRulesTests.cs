using NUnit.Framework;

namespace SwordGame.Tests
{
    public class ProgressionRulesTests
    {
        [Test]
        public void NewSave_AlwaysStartsWithOnlyFirstStageUnlocked()
        {
            Assert.AreEqual(1, ProgressionRules.NormalizeUnlockedCount(0, 12));
            Assert.AreEqual(1, ProgressionRules.NormalizeUnlockedCount(1, 12));
        }

        [Test]
        public void CompletingStage_UnlocksOnlyItsImmediateSuccessor()
        {
            Assert.AreEqual(2, ProgressionRules.UnlockAfterVictory(1, 0, 12));
            Assert.AreEqual(4, ProgressionRules.UnlockAfterVictory(3, 2, 12));
            Assert.AreEqual(4, ProgressionRules.UnlockAfterVictory(4, 1, 12), "旧关通关不应锁回更后面的进度");
        }

        [Test]
        public void CompletingFinalStage_DoesNotExceedStageCount()
        {
            Assert.AreEqual(12, ProgressionRules.UnlockAfterVictory(11, 11, 12));
            Assert.AreEqual(12, ProgressionRules.NormalizeUnlockedCount(99, 12));
        }
    }
}
