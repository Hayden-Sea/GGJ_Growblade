using UnityEngine;

namespace SwordGame
{
    /// <summary>Fit the full board between the HUD margins without changing the board transform.</summary>
    public sealed class GardenBoardFraming : MonoBehaviour
    {
        private Camera _camera;
        private float _width = 13, _height = 11;
        private void Awake() { _camera = GetComponent<Camera>(); }
        public void SetBoard(int width, int height) { _width = width; _height = height; Fit(); }
        private void LateUpdate() { Fit(); }
        private void Fit()
        {
            if (_camera == null) return;
            _camera.orthographicSize = Mathf.Max((_height + 1.2f) / 1.58f, (_width + 1.2f) / (2f * Mathf.Max(0.5f, _camera.aspect) * 0.60f));
        }
    }
}
