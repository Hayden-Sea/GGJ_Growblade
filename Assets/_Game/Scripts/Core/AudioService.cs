using System;
using System.Collections;
using UnityEngine;

namespace SwordGame
{
    public enum SfxId
    {
        BladeSwing, BladeWall, HitFlesh, KnockWall, EnemyMove, EnemyShoot,
        Growth, Hurt, Victory, Defeat, Click, PlayerMove,
    }

    /// <summary>固定循环 BGM、12 类事件音效、变体轮播与两路持久化音量（文档 12.4）。</summary>
    public class AudioService : MonoBehaviour
    {
        private const string MusicPath = "Audio/Music/CozyPuzzleLoop";
        private const string SfxRoot = "Audio/SFX/";
        private const int SfxPoolSize = 8;
        private const float MusicMixGain = 0.55f;

        private AudioSource _music;
        private AudioSource _result;
        private readonly AudioSource[] _sfxPool = new AudioSource[SfxPoolSize];
        private int _sfxIndex;
        private AudioClip[][] _sfxVariants;
        private readonly int[] _variantCursors = new int[Enum.GetValues(typeof(SfxId)).Length];
        private AudioClip _musicClip;
        private float _musicDuck = 1f;
        private float _resultGain = 1f;
        private Coroutine _duckRoutine;

        public float MusicVolume { get; private set; }
        public float SfxVolume { get; private set; }
        public bool ShakeEnabled { get; private set; } = true;
        public AudioClip MusicClip => _musicClip;
        public bool MusicIsPlaying => _music != null && _music.isPlaying;

        /// <summary>按 Resources/Audio 约定载入项目正式素材。</summary>
        public void InitDefault()
        {
            var ids = (SfxId[])Enum.GetValues(typeof(SfxId));
            var variants = new AudioClip[ids.Length][];
            foreach (var id in ids)
            {
                variants[(int)id] = Resources.LoadAll<AudioClip>(SfxRoot + id);
                Array.Sort(variants[(int)id], (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            InitInternal(Resources.Load<AudioClip>(MusicPath), variants);
        }

        /// <summary>保留单 Clip 数组入口，供测试或替换整套素材时使用。</summary>
        public void Init(AudioClip music, AudioClip[] sfx)
        {
            int count = Enum.GetValues(typeof(SfxId)).Length;
            var variants = new AudioClip[count][];
            for (int i = 0; i < count; i++)
                variants[i] = sfx != null && i < sfx.Length && sfx[i] != null
                    ? new[] { sfx[i] }
                    : Array.Empty<AudioClip>();
            InitInternal(music, variants);
        }

        private void InitInternal(AudioClip music, AudioClip[][] variants)
        {
            _musicClip = music;
            _sfxVariants = variants;

            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _music.ignoreListenerPause = true;
            if (_musicClip != null)
            {
                _music.clip = _musicClip;
                _music.Play();
            }

            // 长结算乐句使用独立通道，切关时可单独停止而不截断按钮点击等短音效。
            _result = gameObject.AddComponent<AudioSource>();
            _result.playOnAwake = false;
            _result.spatialBlend = 0f;
            _result.ignoreListenerPause = true;

            for (int i = 0; i < _sfxPool.Length; i++)
            {
                _sfxPool[i] = gameObject.AddComponent<AudioSource>();
                _sfxPool[i].playOnAwake = false;
                _sfxPool[i].spatialBlend = 0f;
                _sfxPool[i].ignoreListenerPause = true;
            }

            MusicVolume = PlayerPrefs.GetFloat("sdnf_music", 0.7f);
            SfxVolume = PlayerPrefs.GetFloat("sdnf_sfx", 0.8f);
            ShakeEnabled = PlayerPrefs.GetInt("sdnf_shake", 1) == 1;
            ApplyVolumes();
        }

        public void SetMusicVolume(float v)
        {
            MusicVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat("sdnf_music", MusicVolume);
            ApplyVolumes();
        }

        public void SetSfxVolume(float v)
        {
            SfxVolume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat("sdnf_sfx", SfxVolume);
            ApplyVolumes();
        }

        public void SetShakeEnabled(bool enabled)
        {
            ShakeEnabled = enabled;
            PlayerPrefs.SetInt("sdnf_shake", enabled ? 1 : 0);
        }

        private void ApplyVolumes()
        {
            if (_music != null)
                _music.volume = MusicVolume * MusicMixGain * _musicDuck;
            foreach (var s in _sfxPool)
                if (s != null)
                    s.volume = SfxVolume;
            if (_result != null)
                _result.volume = SfxVolume * _resultGain;
        }

        public void Play(SfxId id)
        {
            int idx = (int)id;
            if (_sfxVariants == null || idx < 0 || idx >= _sfxVariants.Length)
                return;
            var variants = _sfxVariants[idx];
            if (variants == null || variants.Length == 0)
                return;

            // 顺序轮播保证同一事件不会连续抽到同一个变体；起点随运行随机。
            if (_variantCursors[idx] == 0 && variants.Length > 1)
                _variantCursors[idx] = UnityEngine.Random.Range(0, variants.Length);
            var clip = variants[_variantCursors[idx] % variants.Length];
            _variantCursors[idx] = (_variantCursors[idx] + 1) % variants.Length;

            float jitter = id == SfxId.Victory || id == SfxId.Defeat || id == SfxId.Click ? 0f : 0.035f;
            if (id == SfxId.Victory || id == SfxId.Defeat)
            {
                _resultGain = Gain(id);
                _result.clip = clip;
                _result.pitch = 1f;
                _result.volume = SfxVolume * _resultGain;
                _result.Play();
                if (_duckRoutine != null) StopCoroutine(_duckRoutine);
                _duckRoutine = StartCoroutine(DuckMusic(clip.length));
                return;
            }

            var src = _sfxPool[_sfxIndex];
            _sfxIndex = (_sfxIndex + 1) % _sfxPool.Length;
            src.pitch = 1f + UnityEngine.Random.Range(-jitter, jitter);
            src.PlayOneShot(clip, Gain(id));
        }

        public void StopResultCue()
        {
            if (_result != null) _result.Stop();
            if (_duckRoutine != null)
            {
                StopCoroutine(_duckRoutine);
                _duckRoutine = null;
            }
            _musicDuck = 1f;
            ApplyVolumes();
        }

        public int GetVariantCount(SfxId id)
        {
            int idx = (int)id;
            return _sfxVariants != null && idx >= 0 && idx < _sfxVariants.Length && _sfxVariants[idx] != null
                ? _sfxVariants[idx].Length : 0;
        }

        private static float Gain(SfxId id)
        {
            switch (id)
            {
                case SfxId.EnemyMove: return 0.34f;
                case SfxId.PlayerMove: return 0.42f;
                case SfxId.Click: return 0.48f;
                case SfxId.BladeSwing: return 0.68f;
                case SfxId.BladeWall: return 0.72f;
                case SfxId.EnemyShoot: return 0.62f;
                case SfxId.Victory: return 0.86f;
                case SfxId.Defeat: return 0.8f;
                default: return 0.76f;
            }
        }

        private IEnumerator DuckMusic(float holdSeconds)
        {
            const float down = 0.12f;
            const float up = 0.35f;
            float elapsed = 0f;
            while (elapsed < down)
            {
                elapsed += Time.unscaledDeltaTime;
                _musicDuck = Mathf.Lerp(1f, 0.28f, Mathf.Clamp01(elapsed / down));
                ApplyVolumes();
                yield return null;
            }
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, holdSeconds - down - up));
            elapsed = 0f;
            while (elapsed < up)
            {
                elapsed += Time.unscaledDeltaTime;
                _musicDuck = Mathf.Lerp(0.28f, 1f, Mathf.Clamp01(elapsed / up));
                ApplyVolumes();
                yield return null;
            }
            _musicDuck = 1f;
            ApplyVolumes();
            _duckRoutine = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) PlayerPrefs.Save();
        }

        private void OnApplicationQuit()
        {
            PlayerPrefs.Save();
        }
    }
}
