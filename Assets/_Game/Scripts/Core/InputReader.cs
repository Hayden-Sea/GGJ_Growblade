using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SwordGame
{
    /// <summary>键盘输入 → 统一指令（文档 3.1）。固定优先级 W/A/S/D/Q/E/Space；Shift 预览不执行。
    /// v1.4：成长确认改由战斗画面鼠标左键处理，键盘无确认键。</summary>
    public class InputReader : MonoBehaviour
    {
        public event Action<PlayerAction, bool> OnCommand;
        public event Action OnPauseToggle;

        private static readonly (KeyCode key, Vector2Int dir)[] Moves =
        {
            (KeyCode.W, new Vector2Int(0, 1)),
            (KeyCode.A, new Vector2Int(-1, 0)),
            (KeyCode.S, new Vector2Int(0, -1)),
            (KeyCode.D, new Vector2Int(1, 0)),
            (KeyCode.UpArrow, new Vector2Int(0, 1)),
            (KeyCode.LeftArrow, new Vector2Int(-1, 0)),
            (KeyCode.DownArrow, new Vector2Int(0, -1)),
            (KeyCode.RightArrow, new Vector2Int(1, 0)),
        };

        private void Update()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                // UI 焦点时不吃战斗键，但 Esc 仍可用
                if (Input.GetKeyDown(KeyCode.Escape))
                    OnPauseToggle?.Invoke();
                return;
            }

            bool preview = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            foreach (var (key, dir) in Moves)
            {
                if (Input.GetKeyDown(key))
                {
                    OnCommand?.Invoke(PlayerAction.Move(dir), preview);
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.Q))
            {
                OnCommand?.Invoke(PlayerAction.Rotate(1), preview);
                return;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                OnCommand?.Invoke(PlayerAction.Rotate(-1), preview);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnCommand?.Invoke(PlayerAction.Wait(), false);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
                OnPauseToggle?.Invoke();
        }

        /// <summary>屏幕按钮调用（点击即执行，不预览）。</summary>
        public void EmitMove(Vector2Int dir) => OnCommand?.Invoke(PlayerAction.Move(dir), false);
        public void EmitRotate(int quarterTurns) => OnCommand?.Invoke(PlayerAction.Rotate(quarterTurns), false);
        public void EmitWait() => OnCommand?.Invoke(PlayerAction.Wait(), false);
    }
}
