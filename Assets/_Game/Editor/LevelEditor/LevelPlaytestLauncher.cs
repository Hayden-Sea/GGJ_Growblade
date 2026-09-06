using System.IO;
using SwordGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SwordGame.EditorTools
{
    /// <summary>一键试玩桥（文档 19.5）：保存启动覆盖 → 打开 Game.scene → Play；
    /// 停止后清除一次性覆盖并恢复原编辑场景。覆盖信息存 SessionState，跨 Domain Reload 有效。</summary>
    public static class LevelPlaytestLauncher
    {
        private const string SceneKey = "SDNF_PlaytestOriginalScene";
        private const string RestoreKey = "SDNF_PlaytestRestoreScene";
        private const string GameScenePath = "Assets/_Game/Scenes/Game.scene";

        private static bool _hooked;

        public static bool Launch(RoomDefinition room)
        {
            if (room == null || EditorApplication.isPlaying)
                return false;

            // 保存当前场景（标准保存流程，不丢弃用户改动）
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            SessionState.SetString(SceneKey, EditorSceneManager.GetActiveScene().path);
            SessionState.SetBool(RestoreKey, true);
            PlaytestBridge.SetRoomOverride(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(room)));
            HookOnce();

            EditorSceneManager.OpenScene(GameScenePath);
            EditorApplication.isPlaying = true;
            return true;
        }

        private static void HookOnce()
        {
            if (_hooked)
                return;
            _hooked = true;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;
            if (!SessionState.GetBool(RestoreKey, false))
                return;

            PlaytestBridge.Clear();
            SessionState.SetBool(RestoreKey, false);
            var original = SessionState.GetString(SceneKey, "");
            if (!string.IsNullOrEmpty(original) && File.Exists(original) && !EditorApplication.isPlaying)
                EditorSceneManager.OpenScene(original);
        }
    }
}
