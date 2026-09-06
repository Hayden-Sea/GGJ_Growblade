using SwordGame;

namespace SwordGame
{
    /// <summary>编辑器试玩桥（文档 19.5）：编辑器通过 SessionState 传入一次性关卡覆盖；
    /// 运行时只读此接口，构建中全部为空实现（#if UNITY_EDITOR 剥离 UnityEditor 依赖）。</summary>
    public static class PlaytestBridge
    {
#if UNITY_EDITOR
        private const string ActiveKey = "SDNF_PlaytestActive";
        private const string GuidKey = "SDNF_PlaytestRoomGuid";

        /// <summary>读取并消费一次性关卡覆盖（读取后立即清除，下次正常启动回到标题）。</summary>
        public static RoomDefinition ConsumeRoomOverride()
        {
            if (!UnityEditor.SessionState.GetBool(ActiveKey, false))
                return null;
            var guid = UnityEditor.SessionState.GetString(GuidKey, "");
            Clear();
            if (string.IsNullOrEmpty(guid))
                return null;
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;
            return UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
        }

        public static void SetRoomOverride(string roomGuid)
        {
            UnityEditor.SessionState.SetBool(ActiveKey, true);
            UnityEditor.SessionState.SetString(GuidKey, roomGuid ?? "");
        }

        public static void Clear()
        {
            UnityEditor.SessionState.SetBool(ActiveKey, false);
            UnityEditor.SessionState.SetString(GuidKey, "");
        }
#else
        public static RoomDefinition ConsumeRoomOverride() => null;
#endif
    }
}
