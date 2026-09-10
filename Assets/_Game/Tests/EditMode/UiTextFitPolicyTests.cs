using NUnit.Framework;
using UnityEngine;

namespace SwordGame.Tests
{
    public class UiTextFitPolicyTests
    {
        [Test]
        public void CompactActionButton_UsesExtraHorizontalSpaceAndSmallFallback()
        {
            var bounds = UiTextFitPolicy.ButtonTextBounds(new Vector2(53f, 51f));
            Assert.AreEqual(45f, bounds.x);
            Assert.AreEqual(7f, UiTextFitPolicy.MinimumFontSize(16f, bounds));
        }

        [TestCase(65f, 8f)]
        [TestCase(130f, 8f)]
        [TestCase(300f, 10f)]
        public void MinimumFontSize_ScalesForLocalizedBounds(float width, float expected)
        {
            Assert.AreEqual(expected, UiTextFitPolicy.MinimumFontSize(20f, new Vector2(width, 30f)));
        }

        [TestCase(4, 4, 152f)]
        [TestCase(6, 4, 228f)]
        [TestCase(12, 4, 456f)]
        public void LanguageDropdown_CapsViewportButGrowsScrollableContent(int count, int rows, float height)
        {
            Assert.AreEqual(rows, UiTextFitPolicy.LanguageDropdownVisibleRows(count));
            Assert.AreEqual(height, UiTextFitPolicy.LanguageDropdownContentHeight(count));
        }
    }
}
