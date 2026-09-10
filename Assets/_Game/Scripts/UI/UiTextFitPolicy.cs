using UnityEngine;

namespace SwordGame
{
    /// <summary>Shared sizing rules for localized runtime labels.</summary>
    public static class UiTextFitPolicy
    {
        public static float MinimumFontSize(float requestedSize, Vector2 bounds)
        {
            if (bounds.x <= 60f) return 7f;
            if (bounds.x <= 140f) return 8f;
            return Mathf.Max(9f, requestedSize * 0.5f);
        }

        public static Vector2 ButtonTextBounds(Vector2 buttonSize)
        {
            return buttonSize - (buttonSize.x <= 60f ? new Vector2(8f, 8f) : new Vector2(20f, 8f));
        }

        public const int LanguageDropdownMaxVisibleRows = 4;
        public const float LanguageDropdownRowHeight = 38f;

        public static int LanguageDropdownVisibleRows(int languageCount) =>
            Mathf.Clamp(languageCount, 1, LanguageDropdownMaxVisibleRows);

        public static float LanguageDropdownContentHeight(int languageCount) =>
            Mathf.Max(1, languageCount) * LanguageDropdownRowHeight;
    }
}
