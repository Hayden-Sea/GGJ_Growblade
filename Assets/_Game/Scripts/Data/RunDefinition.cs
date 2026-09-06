using System.Collections.Generic;
using UnityEngine;

namespace SwordGame
{
    /// <summary>一局的阶段列表（文档 9.1）。</summary>
    [CreateAssetMenu(fileName = "RUN_Default", menuName = "SwordGame/Run Definition")]
    public sealed class RunDefinition : ScriptableObject
    {
        public List<RunStage> stages = new List<RunStage>();
    }
}
