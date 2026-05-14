using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || UNITY_2023_2_OR_NEWER
using TMPro;
#endif

namespace DungKeeper
{
    // StrikeReport is defined in StrikeSystem.cs.

    /// <summary>
    /// Singleton MonoBehaviour that owns all heads-up-display logic.
    /// Subscribes to <see cref="EventBus.Global"/> events and translates them
    /// into visual feedback — resource readouts, unit inspection, slap reactions,
    /// strike warnings, and threat alerts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIManager : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Singleton
        // -------------------------------------------------------------------------

        public static UIManager Instance { get; private set; }

        // -------------------------------------------------------------------------
        // Inspector — Resource HUD
        // -------------------------------------------------------------------------

        [Header("Resource HUD")]
        [SerializeField] private Text _goldText;
        [SerializeField] private Text _essenceText;
        [SerializeField] private Text _fearText;

        [Header("Population HUD")]
        [SerializeField] private Text _unitCountText;
        [SerializeField] private Text _moraleText;
        [SerializeField] private Text _threatText;

        // -------------------------------------------------------------------------
        // Inspector — Unit Inspect Panel
        // -------------------------------------------------------------------------

        [Header("Unit Inspect Panel")]
        [SerializeField] private GameObject _unitInspectPanel;
        [SerializeField] private Text       _inspectNameText;
        [SerializeField] private Text       _inspectStateText;
        [SerializeField] private Slider     _inspectFearSlider;
        [SerializeField] private Slider     _inspectAngerSlider;
        [SerializeField] private Slider     _inspectMoraleSlider;
        [SerializeField] private Slider     _inspectLoyaltySlider;
        [SerializeField] private Text       _inspectFeedbackText;

        // -------------------------------------------------------------------------
        // Inspector — Strike Warning Panel
        // -------------------------------------------------------------------------

        [Header("Strike Warning Panel")]
        [SerializeField] private GameObject _strikeWarningPanel;
        [SerializeField] private Text       _strikeWarningText;

        // -------------------------------------------------------------------------
        // Inspector — Threat Warning Panel
        // -------------------------------------------------------------------------

        [Header("Threat Warning Panel")]
        [SerializeField] private GameObject _threatWarningPanel;
        [SerializeField] private Text       _threatWarningText;

        // -------------------------------------------------------------------------
        // Inspector — Room Build Panel
        // -------------------------------------------------------------------------

        [Header("Room Build Panel")]
        [SerializeField] private GameObject _roomBuildPanel;
        [SerializeField] private Button[]   _roomBuildButtons;

        // -------------------------------------------------------------------------
        // Inspector — Slap Feedback (floating text)
        // -------------------------------------------------------------------------

        [Header("Slap Feedback")]
#if TMP_PRESENT || UNITY_2023_2_OR_NEWER
        [SerializeField] private TextMeshProUGUI _slapFeedbackText;
#else
        [SerializeField] private Text _slapFeedbackText;
#endif
        [SerializeField] private RectTransform   _slapFeedbackAnchor;

        // -------------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------------

        private Coroutine _feedbackCoroutine;

        // Cached resource values written by event handlers, read in Update.
        private float _gold;
        private float _essence;
        private float _fear;

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

            // Hide panels that start hidden.
            if (_unitInspectPanel   != null) _unitInspectPanel.SetActive(false);
            if (_strikeWarningPanel != null) _strikeWarningPanel.SetActive(false);
            if (_threatWarningPanel != null) _threatWarningPanel.SetActive(false);
            if (_slapFeedbackText   != null) _slapFeedbackText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            EventBus.Global.Subscribe<ResourceChangedEvent>      (OnResourceChanged);
            EventBus.Global.Subscribe<UnitSlappedEvent>          (OnUnitSlapped);
            EventBus.Global.Subscribe<UnitRebellionStartedEvent> (OnUnitRebellionStarted);
            EventBus.Global.Subscribe<ThreatSpawnedEvent>        (OnThreatSpawned);
        }

        private void OnDisable()
        {
            EventBus.Global.Unsubscribe<ResourceChangedEvent>     (OnResourceChanged);
            EventBus.Global.Unsubscribe<UnitSlappedEvent>         (OnUnitSlapped);
            EventBus.Global.Unsubscribe<UnitRebellionStartedEvent>(OnUnitRebellionStarted);
            EventBus.Global.Unsubscribe<ThreatSpawnedEvent>       (OnThreatSpawned);
        }

        private void Update()
        {
            // Update resource readouts from cached values (set by event handlers).
            SetText(_goldText,    $"Gold: {_gold:F0}");
            SetText(_essenceText, $"Essence: {_essence:F0}");
            SetText(_fearText,    $"Fear: {_fear:F0}");
        }

        // -------------------------------------------------------------------------
        // Unit inspect panel
        // -------------------------------------------------------------------------

        /// <summary>Opens the unit inspection panel populated with <paramref name="unit"/>'s data.</summary>
        public void ShowUnitInspect(UnitData unit)
        {
            if (unit == null || _unitInspectPanel == null) return;

            _unitInspectPanel.SetActive(true);

            SetText(_inspectNameText,    unit.Name);
            SetText(_inspectStateText,   unit.State.ToString());

            SetSlider(_inspectFearSlider,    unit.Fear,    0f, 100f);
            SetSlider(_inspectAngerSlider,   unit.Anger,   0f, 100f);
            SetSlider(_inspectMoraleSlider,  unit.Morale,  0f, 100f);
            SetSlider(_inspectLoyaltySlider, unit.Loyalty, 0f, 100f);

            SetText(_inspectFeedbackText, BuildInspectFeedback(unit));
        }

        /// <summary>Hides the unit inspection panel.</summary>
        public void HideUnitInspect()
        {
            if (_unitInspectPanel != null)
                _unitInspectPanel.SetActive(false);
        }

        // -------------------------------------------------------------------------
        // Slap feedback (floating text)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Shows a short floating message — e.g. "TERRIFIED!" in red — anchored near
        /// <see cref="_slapFeedbackAnchor"/>. Cancels any currently playing feedback.
        /// </summary>
        public void ShowSlapFeedback(string message, Color color)
        {
            if (_slapFeedbackText == null) return;

            if (_feedbackCoroutine != null)
                StopCoroutine(_feedbackCoroutine);

            _feedbackCoroutine = StartCoroutine(AnimateFeedbackText(message, color));
        }

        private IEnumerator AnimateFeedbackText(string msg, Color color)
        {
            if (_slapFeedbackText == null) yield break;

            // Reset position to anchor.
            if (_slapFeedbackAnchor != null)
                _slapFeedbackText.rectTransform.anchoredPosition = _slapFeedbackAnchor.anchoredPosition;

            _slapFeedbackText.text  = msg;
            _slapFeedbackText.color = color;
            _slapFeedbackText.gameObject.SetActive(true);

            const float Duration  = 1.5f;
            const float FloatDist = 60f; // pixels upward

            Vector2 startPos = _slapFeedbackText.rectTransform.anchoredPosition;
            float   elapsed  = 0f;

            while (elapsed < Duration)
            {
                elapsed += Time.deltaTime;
                float t  = elapsed / Duration;

                // Float upward.
                _slapFeedbackText.rectTransform.anchoredPosition =
                    startPos + new Vector2(0f, Mathf.Lerp(0f, FloatDist, t));

                // Fade out in the second half.
                float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) * 2f);
                Color c = _slapFeedbackText.color;
                c.a = alpha;
                _slapFeedbackText.color = c;

                yield return null;
            }

            _slapFeedbackText.gameObject.SetActive(false);
            _feedbackCoroutine = null;
        }

        // -------------------------------------------------------------------------
        // Strike warning panel
        // -------------------------------------------------------------------------

        /// <summary>
        /// Shows or hides the strike warning panel.
        /// Pass null to hide; pass an active <see cref="StrikeReport"/> to show details.
        /// </summary>
        public void ShowStrikeWarning(StrikeReport report)
        {
            if (_strikeWarningPanel == null) return;

            if (report == null)
            {
                _strikeWarningPanel.SetActive(false);
                return;
            }

            _strikeWarningPanel.SetActive(true);

            string fullRebellionTag = report.FullRebellionActive ? " — FULL REBELLION!" : string.Empty;
            SetText(_strikeWarningText,
                $"STRIKE! {report.ActiveStrikers} unit(s) rebelling " +
                $"({report.ProductionLoss * 100f:F0}% production lost){fullRebellionTag}");
        }

        // -------------------------------------------------------------------------
        // Threat warning panel
        // -------------------------------------------------------------------------

        /// <summary>
        /// Shows or hides the threat warning panel.
        /// Pass <see cref="ThreatLevel.None"/> to hide.
        /// </summary>
        public void ShowThreatWarning(ThreatLevel level)
        {
            if (_threatWarningPanel == null) return;

            if (level == ThreatLevel.None)
            {
                _threatWarningPanel.SetActive(false);
                return;
            }

            _threatWarningPanel.SetActive(true);
            SetText(_threatWarningText, $"THREAT: {level} incursion detected!");
        }

        // -------------------------------------------------------------------------
        // Room build panel
        // -------------------------------------------------------------------------

        /// <summary>Enables or disables all room build buttons.</summary>
        public void SetRoomBuildButtonsEnabled(bool enabled)
        {
            if (_roomBuildButtons == null) return;
            foreach (Button btn in _roomBuildButtons)
                if (btn != null) btn.interactable = enabled;
        }

        // -------------------------------------------------------------------------
        // EventBus handlers
        // -------------------------------------------------------------------------

        private void OnResourceChanged(ResourceChangedEvent evt)
        {
            switch (evt.Type)
            {
                case ResourceType.Gold:    _gold    = evt.NewValue; break;
                case ResourceType.Essence: _essence = evt.NewValue; break;
                case ResourceType.Fear:    _fear    = evt.NewValue; break;
            }
        }

        private void OnUnitSlapped(UnitSlappedEvent evt)
        {
            string message;
            Color  color;

            switch (evt.Response)
            {
                case SlapResponse.SpeedUp:
                    message = "SPEEDING UP!";
                    color   = Color.yellow;
                    break;
                case SlapResponse.BecomeExcited:
                    message = "EXCITED!";
                    color   = new Color(0.6f, 0f, 0.9f); // purple
                    break;
                case SlapResponse.BecomeAngry:
                    message = "ANGRY!";
                    color   = Color.red;
                    break;
                case SlapResponse.Ignore:
                    message = "IGNORED...";
                    color   = Color.gray;
                    break;
                case SlapResponse.Quit:
                    message = "QUIT!";
                    color   = new Color(0.8f, 0f, 0f);
                    break;
                case SlapResponse.Rebel:
                    message = "REBELLING!";
                    color   = new Color(1f, 0.4f, 0f); // orange
                    break;
                default:
                    message = "...";
                    color   = Color.white;
                    break;
            }

            ShowSlapFeedback(message, color);
        }

        private void OnUnitRebellionStarted(UnitRebellionStartedEvent evt)
        {
            // Show the strike panel with a minimal synthetic report.
            // The StrikeSystem will provide fuller data via ShowStrikeWarning once its tick runs.
            _strikeWarningPanel?.SetActive(true);
            SetText(_strikeWarningText,
                $"REBELLION! {evt.Unit.Name} has snapped ({evt.StrikeType})!");
        }

        private void OnThreatSpawned(ThreatSpawnedEvent evt)
        {
            ShowThreatWarning(evt.Level);
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        private static void SetText(Text label, string value)
        {
            if (label != null) label.text = value;
        }

        private static void SetSlider(Slider slider, float value, float min, float max)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value    = Mathf.Clamp(value, min, max);
        }

        private static string BuildInspectFeedback(UnitData unit)
        {
            if (unit.Anger > 80f)                       return "Furious — handle with care.";
            if (unit.Morale < 20f)                      return "Morale is critically low.";
            if (unit.Fear > 80f)                        return "Terrified — near breaking point.";
            if (unit.Loyalty < 20f)                     return "Loyalty is dangerously low.";
            if (unit.Hunger > 70f)                      return "Starving — needs food now.";
            if (unit.Fatigue > 80f)                     return "Exhausted — needs rest.";
            if (unit.State == UnitState.Rebelling)      return "Actively rebelling!";
            if (unit.State == UnitState.Impressed)      return "Impressed — productivity boosted!";
            return "Status nominal.";
        }
    }
}
