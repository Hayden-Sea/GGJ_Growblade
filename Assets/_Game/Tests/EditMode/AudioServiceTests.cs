using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SwordGame.Tests
{
    public class AudioServiceTests
    {
        private GameObject _go;
        private float _oldMusic;
        private float _oldSfx;
        private bool _hadMusic;
        private bool _hadSfx;

        [SetUp]
        public void SetUp()
        {
            _hadMusic = PlayerPrefs.HasKey("sdnf_music");
            _hadSfx = PlayerPrefs.HasKey("sdnf_sfx");
            _oldMusic = PlayerPrefs.GetFloat("sdnf_music");
            _oldSfx = PlayerPrefs.GetFloat("sdnf_sfx");
            _go = new GameObject("AudioServiceTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadMusic) PlayerPrefs.SetFloat("sdnf_music", _oldMusic); else PlayerPrefs.DeleteKey("sdnf_music");
            if (_hadSfx) PlayerPrefs.SetFloat("sdnf_sfx", _oldSfx); else PlayerPrefs.DeleteKey("sdnf_sfx");
            UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void DefaultCatalogHasMusicAndEveryEvent()
        {
            var audio = _go.AddComponent<AudioService>();
            audio.InitDefault();

            Assert.NotNull(audio.MusicClip);
            Assert.Greater(audio.MusicClip.length, 30f);
            int[] expected = { 2, 2, 2, 2, 3, 2, 2, 2, 1, 1, 3, 2 };
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
                Assert.AreEqual(expected[(int)id], audio.GetVariantCount(id), id.ToString());
        }

        [Test]
        public void DefaultSourcesAreTwoDimensionalAndMusicLoops()
        {
            var audio = _go.AddComponent<AudioService>();
            audio.InitDefault();
            var sources = _go.GetComponents<AudioSource>();

            Assert.AreEqual(10, sources.Length);
            Assert.IsTrue(sources[0].loop);
            Assert.AreSame(audio.MusicClip, sources[0].clip);
            foreach (var source in sources)
                Assert.AreEqual(0f, source.spatialBlend);
        }

        [Test]
        public void VolumeSettersClampPersistAndUpdateSources()
        {
            var audio = _go.AddComponent<AudioService>();
            audio.InitDefault();
            audio.SetMusicVolume(2f);
            audio.SetSfxVolume(-1f);
            var sources = _go.GetComponents<AudioSource>();

            Assert.AreEqual(1f, audio.MusicVolume);
            Assert.AreEqual(0f, audio.SfxVolume);
            Assert.AreEqual(0.55f, sources[0].volume, 0.001f);
            for (int i = 1; i < sources.Length; i++) Assert.AreEqual(0f, sources[i].volume);
            Assert.AreEqual(1f, PlayerPrefs.GetFloat("sdnf_music"));
            Assert.AreEqual(0f, PlayerPrefs.GetFloat("sdnf_sfx"));
        }

        [Test]
        public void ImportSettingsMatchRuntimeUse()
        {
            var music = AssetImporter.GetAtPath("Assets/_Game/Audio/Resources/Audio/Music/CozyPuzzleLoop.ogg") as AudioImporter;
            var swing = AssetImporter.GetAtPath("Assets/_Game/Audio/Resources/Audio/SFX/BladeSwing/01_BladeSwing.ogg") as AudioImporter;

            Assert.NotNull(music);
            Assert.NotNull(swing);
            Assert.AreEqual(AudioClipLoadType.Streaming, music.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, swing.defaultSampleSettings.loadType);
            Assert.IsFalse(music.forceToMono);
            Assert.IsTrue(swing.forceToMono);
        }
    }
}
