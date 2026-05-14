using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Singleton MonoBehaviour that owns all game audio:
    /// ambient music track sequencing, SFX one-shots, and slap force–to–clip mapping.
    /// Subscribes to <see cref="EventBus.Global"/> so that gameplay systems never
    /// need a direct reference to the audio manager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioManager : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Singleton
        // -------------------------------------------------------------------------

        public static AudioManager Instance { get; private set; }

        // -------------------------------------------------------------------------
        // Inspector — Audio sources
        // -------------------------------------------------------------------------

        [Header("Audio Sources")]
        [Tooltip("Looping music / ambient audio source.")]
        [SerializeField] private AudioSource _musicSource;

        [Tooltip("One-shot SFX audio source.")]
        [SerializeField] private AudioSource _sfxSource;

        // -------------------------------------------------------------------------
        // Inspector — Music
        // -------------------------------------------------------------------------

        [Header("Ambient Music")]
        [Tooltip("Tracks played sequentially (or shuffled). Loops back to the start when exhausted.")]
        [SerializeField] private AudioClip[] _ambientTracks;

        [Tooltip("Whether to shuffle the ambient track order each cycle.")]
        [SerializeField] private bool _shuffleTracks = false;

        [Tooltip("Duration of the cross-fade between ambient tracks, in seconds.")]
        [SerializeField] [Range(0f, 5f)] private float _crossFadeDuration = 1.5f;

        // -------------------------------------------------------------------------
        // Inspector — Slap SFX
        // -------------------------------------------------------------------------

        [Header("Slap Sounds")]
        [SerializeField] private AudioClip _slapHitLight;
        [SerializeField] private AudioClip _slapHitMedium;
        [SerializeField] private AudioClip _slapHitHeavy;

        // Force thresholds that determine which clip is chosen.
        [SerializeField] [Range(0f, 100f)] private float _slapLightMaxForce  = 33f;
        [SerializeField] [Range(0f, 100f)] private float _slapMediumMaxForce = 66f;

        // -------------------------------------------------------------------------
        // Inspector — Unit SFX
        // -------------------------------------------------------------------------

        [Header("Unit Sounds")]
        [SerializeField] private AudioClip _unitScream;
        [SerializeField] private AudioClip _unitRebellionStart;
        [SerializeField] private AudioClip _unitDeath;

        // -------------------------------------------------------------------------
        // Inspector — Room SFX
        // -------------------------------------------------------------------------

        [Header("Room Sounds")]
        [SerializeField] private AudioClip _roomBuildSound;
        [SerializeField] private AudioClip _roomDemolishSound;

        // -------------------------------------------------------------------------
        // Inspector — Threat SFX
        // -------------------------------------------------------------------------

        [Header("Threat Sounds")]
        [SerializeField] private AudioClip _threatWarningSound;
        [SerializeField] private AudioClip _threatVictorySound;
        [SerializeField] private AudioClip _threatDefeatSound;

        // -------------------------------------------------------------------------
        // Inspector — Resource SFX
        // -------------------------------------------------------------------------

        [Header("Resource Sounds")]
        [SerializeField] private AudioClip _goldPickupSound;
        [SerializeField] private AudioClip _essencePickupSound;

        // -------------------------------------------------------------------------
        // Inspector — Volume
        // -------------------------------------------------------------------------

        [Header("Volume")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 1f;
        [SerializeField] [Range(0f, 1f)] private float _musicVolume  = 0.6f;
        [SerializeField] [Range(0f, 1f)] private float _sfxVolume    = 1f;

        // -------------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------------

        private int       _currentTrackIndex = -1;
        private Coroutine _trackSequenceCoroutine;
        private Coroutine _crossFadeCoroutine;

        // Scratch array used when shuffling to avoid extra allocations.
        private int[] _shuffledIndices;

        // -------------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Auto-assign AudioSources if not set in the inspector.
            var sources = GetComponents<AudioSource>();
            if (_musicSource == null)
                _musicSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            if (_sfxSource == null)
                _sfxSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();

            _musicSource.loop         = true;
            _musicSource.playOnAwake  = false;
            _sfxSource.loop           = false;
            _sfxSource.playOnAwake    = false;

            ApplyVolumes();
        }

        private void OnEnable()
        {
            EventBus.Global.Subscribe<UnitSlappedEvent>          (OnUnitSlapped);
            EventBus.Global.Subscribe<UnitRebellionStartedEvent> (OnUnitRebellionStarted);
            EventBus.Global.Subscribe<ThreatSpawnedEvent>        (OnThreatSpawned);
            EventBus.Global.Subscribe<ThreatResolvedEvent>       (OnThreatResolved);
            EventBus.Global.Subscribe<RoomBuiltEvent>            (OnRoomBuilt);
        }

        private void OnDisable()
        {
            EventBus.Global.Unsubscribe<UnitSlappedEvent>          (OnUnitSlapped);
            EventBus.Global.Unsubscribe<UnitRebellionStartedEvent> (OnUnitRebellionStarted);
            EventBus.Global.Unsubscribe<ThreatSpawnedEvent>        (OnThreatSpawned);
            EventBus.Global.Unsubscribe<ThreatResolvedEvent>       (OnThreatResolved);
            EventBus.Global.Unsubscribe<RoomBuiltEvent>            (OnRoomBuilt);
        }

        private void Start()
        {
            if (_ambientTracks != null && _ambientTracks.Length > 0)
                _trackSequenceCoroutine = StartCoroutine(RunTrackSequence());
        }

        // -------------------------------------------------------------------------
        // Public API — Slap audio
        // -------------------------------------------------------------------------

        /// <summary>
        /// Selects a light / medium / heavy slap clip based on <paramref name="force"/>
        /// (0–100 range) and plays it with a small random pitch variation.
        /// </summary>
        public void PlaySlapSound(float force)
        {
            AudioClip clip = force <= _slapLightMaxForce  ? _slapHitLight
                           : force <= _slapMediumMaxForce ? _slapHitMedium
                           : _slapHitHeavy;

            if (clip == null) return;

            float originalPitch = _sfxSource.pitch;
            _sfxSource.pitch = 1f + Random.Range(-0.15f, 0.15f);
            PlaySFX(clip);
            _sfxSource.pitch = originalPitch;
        }

        // -------------------------------------------------------------------------
        // Public API — General SFX
        // -------------------------------------------------------------------------

        /// <summary>
        /// Plays <paramref name="clip"/> as a one-shot on the SFX source.
        /// Volume is scaled by <paramref name="volumeScale"/> and the global master / SFX volumes.
        /// </summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * _sfxVolume * _masterVolume);
        }

        // -------------------------------------------------------------------------
        // Public API — Music
        // -------------------------------------------------------------------------

        /// <summary>
        /// Cross-fades from the current music track to <paramref name="clip"/>.
        /// </summary>
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (clip == null || _musicSource == null) return;

            if (_crossFadeCoroutine != null)
                StopCoroutine(_crossFadeCoroutine);

            _crossFadeCoroutine = StartCoroutine(CrossFadeTo(clip, loop));
        }

        // -------------------------------------------------------------------------
        // Public API — Volume control
        // -------------------------------------------------------------------------

        /// <summary>Sets master volume [0, 1] and propagates to all sources.</summary>
        public void SetMasterVolume(float v)
        {
            _masterVolume = Mathf.Clamp01(v);
            ApplyVolumes();
        }

        /// <summary>Sets music volume [0, 1] and propagates to the music source.</summary>
        public void SetMusicVolume(float v)
        {
            _musicVolume = Mathf.Clamp01(v);
            ApplyVolumes();
        }

        /// <summary>Sets SFX volume [0, 1] (applied per PlaySFX call).</summary>
        public void SetSFXVolume(float v)
        {
            _sfxVolume = Mathf.Clamp01(v);
        }

        // -------------------------------------------------------------------------
        // EventBus handlers
        // -------------------------------------------------------------------------

        private void OnUnitSlapped(UnitSlappedEvent evt)
            => PlaySlapSound(evt.Force);

        private void OnUnitRebellionStarted(UnitRebellionStartedEvent evt)
            => PlaySFX(_unitRebellionStart);

        private void OnThreatSpawned(ThreatSpawnedEvent evt)
            => PlaySFX(_threatWarningSound);

        private void OnThreatResolved(ThreatResolvedEvent evt)
            => PlaySFX(evt.PlayerWon ? _threatVictorySound : _threatDefeatSound);

        private void OnRoomBuilt(RoomBuiltEvent evt)
            => PlaySFX(_roomBuildSound);

        // -------------------------------------------------------------------------
        // Ambient track sequencing
        // -------------------------------------------------------------------------

        private IEnumerator RunTrackSequence()
        {
            if (_ambientTracks == null || _ambientTracks.Length == 0) yield break;

            BuildShuffleIndices();
            int sequencePos = 0;

            while (true)
            {
                int trackIdx = _shuffleTracks
                    ? _shuffledIndices[sequencePos % _shuffledIndices.Length]
                    : sequencePos % _ambientTracks.Length;

                AudioClip track = _ambientTracks[trackIdx];
                if (track != null)
                {
                    PlayMusic(track, loop: false);
                    // Wait for the cross-fade to complete, then wait for the track to finish.
                    yield return new WaitForSeconds(_crossFadeDuration);
                    yield return new WaitForSeconds(Mathf.Max(0f, track.length - _crossFadeDuration));
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }

                sequencePos++;

                // Re-shuffle at the start of each new cycle.
                if (_shuffleTracks && sequencePos % _ambientTracks.Length == 0)
                    BuildShuffleIndices();
            }
        }

        private void BuildShuffleIndices()
        {
            int count = _ambientTracks != null ? _ambientTracks.Length : 0;
            if (_shuffledIndices == null || _shuffledIndices.Length != count)
                _shuffledIndices = new int[count];

            for (int i = 0; i < count; i++) _shuffledIndices[i] = i;

            // Fisher-Yates shuffle.
            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_shuffledIndices[i], _shuffledIndices[j]) = (_shuffledIndices[j], _shuffledIndices[i]);
            }
        }

        // -------------------------------------------------------------------------
        // Cross-fade coroutine
        // -------------------------------------------------------------------------

        private IEnumerator CrossFadeTo(AudioClip newClip, bool loop)
        {
            float startVolume = _musicSource.volume;
            float fadeDuration = _crossFadeDuration > 0f ? _crossFadeDuration * 0.5f : 0f;

            // Fade out existing track.
            if (_musicSource.isPlaying && fadeDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeDuration)
                {
                    elapsed += Time.deltaTime;
                    _musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
                    yield return null;
                }
            }

            _musicSource.Stop();
            _musicSource.clip = newClip;
            _musicSource.loop = loop;
            _musicSource.Play();

            // Fade in new track.
            float targetVolume = _musicVolume * _masterVolume;
            if (fadeDuration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < fadeDuration)
                {
                    elapsed += Time.deltaTime;
                    _musicSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeDuration);
                    yield return null;
                }
            }

            _musicSource.volume = targetVolume;
            _crossFadeCoroutine = null;
        }

        // -------------------------------------------------------------------------
        // Volume helpers
        // -------------------------------------------------------------------------

        private void ApplyVolumes()
        {
            if (_musicSource != null)
                _musicSource.volume = _musicVolume * _masterVolume;
            // SFX volume is applied per-shot in PlaySFX / PlaySlapSound.
        }

        // -------------------------------------------------------------------------
        // Cleanup
        // -------------------------------------------------------------------------

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
