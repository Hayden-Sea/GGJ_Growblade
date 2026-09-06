using System.Collections;
using UnityEngine;

namespace SwordGame
{
    /// <summary>角色/敌人/核心的视图：格心定位、闪白、死亡表现。Transform 仅作表现（R11）。
    /// 缩放只作用于内部 Body 子物体，根物体保持单位缩放，避免子级（剑视图）继承视觉缩放。</summary>
    public class ActorView : MonoBehaviour
    {
        public int ActorId = -1;
        public SpriteRenderer Renderer { get; private set; }
        private Transform _body;
        private Vector3 _baseScale;
        private Coroutine _routine;
        private SpriteRenderer _shadow;

        public void Init(int actorId, Sprite sprite, Color tint, float diameter, int sortingOrder, float maxVisualHeight = .88f)
        {
            ActorId = actorId;
            if (_body == null)
            {
                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(transform, false);
                _body = bodyGo.transform;
            }
            Renderer = _body.GetComponent<SpriteRenderer>();
            if (Renderer == null)
                Renderer = _body.gameObject.AddComponent<SpriteRenderer>();
            Renderer.sprite = sprite;
            Renderer.color = tint;
            Renderer.sortingOrder = sortingOrder;
            float baseSize = sprite != null && sprite.bounds.size.x > 0.001f ? sprite.bounds.size.x : 1f;
            _baseScale = Vector3.one * (diameter / baseSize);
            if (sprite != null && sprite.bounds.size.y * _baseScale.y > maxVisualHeight)
                _baseScale = Vector3.one * (maxVisualHeight / sprite.bounds.size.y);
            _body.localScale = _baseScale;
            if (_shadow == null)
            {
                var shadow = new GameObject("GroundShadow");
                shadow.transform.SetParent(transform, false);
                _shadow = shadow.AddComponent<SpriteRenderer>();
                _shadow.sprite = SpriteFactory.SoftCircle();
                _shadow.color = GardenTheme.Alpha(GardenTheme.Ink, .17f);
                _shadow.sortingOrder = -3;
            }
            _shadow.transform.localPosition = new Vector3(.035f, -.28f, 0);
            _shadow.transform.localScale = new Vector3(diameter * .85f, .17f, 1);
        }

        public void SnapTo(Vector2 worldPos) => transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

        public void Flash(Color color, float duration = 0.12f)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FlashRoutine(color, duration, scalePulse: 0f));
        }

        public void HurtFlash(float duration = 0.12f)
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(FlashRoutine(Color.white, duration, scalePulse: 0.12f));
        }

        private IEnumerator FlashRoutine(Color color, float duration, float scalePulse)
        {
            var original = Renderer.color;
            float t = 0f;
            while (t < duration)
            {
                float k = 1f - t / duration;
                Renderer.color = Color.Lerp(original, color, k);
                _body.localScale = _baseScale * (1f + scalePulse * k);
                t += Time.deltaTime;
                yield return null;
            }
            Renderer.color = original;
            _body.localScale = _baseScale;
            _routine = null;
        }

        /// <summary>死亡：原位切碎表现（缩放消散）。</summary>
        public IEnumerator PlayDeath(float duration = 0.22f)
        {
            if (_routine != null) StopCoroutine(_routine);
            float t = 0f;
            var original = Renderer.color;
            while (t < duration)
            {
                float k = t / duration;
                _body.localScale = _baseScale * (1f + 0.6f * k);
                var c = original;
                c.a = 1f - k;
                Renderer.color = c;
                t += Time.deltaTime;
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
