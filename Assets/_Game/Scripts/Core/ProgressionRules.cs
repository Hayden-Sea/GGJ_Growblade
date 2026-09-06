using UnityEngine;

namespace SwordGame
{
    /// <summary>关卡解锁的纯规则，存档读写由 RunController 负责。</summary>
    public static class ProgressionRules
    {
        public static int NormalizeUnlockedCount(int savedCount, int stageCount)
        {
            return Mathf.Clamp(savedCount, stageCount > 0 ? 1 : 0, Mathf.Max(0, stageCount));
        }

        public static int UnlockAfterVictory(int unlockedCount, int completedStageIndex, int stageCount)
        {
            int normalized = NormalizeUnlockedCount(unlockedCount, stageCount);
            return Mathf.Min(stageCount, Mathf.Max(normalized, completedStageIndex + 2));
        }
    }
}
