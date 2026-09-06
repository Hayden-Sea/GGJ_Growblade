using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SwordGame
{
    /// <summary>Subtle sticker lift/press; never modifies a game actor or timing.</summary>
    public sealed class GardenButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private bool _hover, _pressed;
        private Button _button;
        private void Awake() { _button = GetComponent<Button>(); }
        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = _pressed = false; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) _pressed = true; }
        public void OnPointerUp(PointerEventData e) { _pressed = false; }
        private void Update()
        {
            bool enabledInput = _button != null && _button.interactable;
            float target = !enabledInput ? 1f : _pressed ? 0.97f : _hover ? 1.025f : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
        }
        private void OnDisable() { _hover = _pressed = false; transform.localScale = Vector3.one; }
    }
}
