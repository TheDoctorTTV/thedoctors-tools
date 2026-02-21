using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;

public enum VoiceModeOption
{
    Whisper = 0,
    Talk = 1,
    Shout = 2
}

public class VoiceModeMenu : UdonSharpBehaviour
{
    private const string VoiceModeKey = "voice_mode";
    private const string VoiceChannelKey = "voice_channel";
    private const string VoiceRadioTxKey = "voice_radio_tx";

    [Header("UI (optional)")]
    [Tooltip("Assign the Whisper mode button.")]
    [SerializeField] private Button whisperButton;
    [Tooltip("Assign the Talk mode button.")]
    [SerializeField] private Button talkButton;
    [Tooltip("Assign the Shout mode button.")]
    [SerializeField] private Button shoutButton;
    [Tooltip("Assign the channel slider (1-10).")]
    [SerializeField] private Slider radioSlider;
    [Tooltip("Optional: assign one button to toggle radio transmit (ON = heard globally on your channel).")]
    [SerializeField] private Button radioTransmitButton;
    [Tooltip("Optional: assign one button to reset mode/channel/radio transmit to defaults.")]
    [SerializeField] private Button resetButton;
    [Tooltip("Optional TMP label that shows current mode text.")]
    [SerializeField] private TextMeshProUGUI modeText;
    [Tooltip("Optional TMP label that shows current channel text.")]
    [SerializeField] private TextMeshProUGUI radioText;
    [Tooltip("Optional TMP label that shows radio transmit state.")]
    [SerializeField] private TextMeshProUGUI radioTransmitText;

    [Header("Proximity: Whisper")]
    [Tooltip("How close another player must be for whisper to start being heard.")]
    [SerializeField] private float whisperNear = 0f;
    [Tooltip("Maximum distance whisper can be heard.")]
    [SerializeField] private float whisperFar = 8f;
    [Tooltip("Whisper loudness (0-24).")]
    [SerializeField] private float whisperGain = 10f;

    [Header("Proximity: Talk")]
    [Tooltip("How close another player must be for talk to start being heard.")]
    [SerializeField] private float talkNear = 0f;
    [Tooltip("Maximum distance talk can be heard.")]
    [SerializeField] private float talkFar = 25f;
    [Tooltip("Talk loudness (0-24).")]
    [SerializeField] private float talkGain = 15f;

    [Header("Proximity: Shout")]
    [Tooltip("How close another player must be for shout to start being heard.")]
    [SerializeField] private float shoutNear = 0f;
    [Tooltip("Maximum distance shout can be heard.")]
    [SerializeField] private float shoutFar = 60f;
    [Tooltip("Shout loudness (0-24).")]
    [SerializeField] private float shoutGain = 18f;

    [Header("Radio (Same Channel Global Audio)")]
    [Tooltip("Near distance used when hearing a player via radio.")]
    [SerializeField] private float radioNear = 0f;
    [Tooltip("Far distance used when hearing a player via radio (global = very large).")]
    [SerializeField] private float radioFar = 100000f;
    [Tooltip("Gain used when hearing a player via radio.")]
    [SerializeField] private float radioGain = 15f;
    [Tooltip("Apply lowpass when hearing someone over radio.")]
    [SerializeField] private bool radioLowpass = true;

    [Header("Editor Auto-Wire")]
    [Tooltip("When enabled in the Unity Editor, assigning button/slider refs auto-fills Udon SendCustomEvent bindings.")]
    [SerializeField] private bool autoWireUiEventsInEditor = true;

    [Header("Default Local Settings")]
    [Tooltip("Default mode used when no saved local value exists or when Reset is pressed.")]
    [SerializeField] private VoiceModeOption defaultMode = VoiceModeOption.Talk;
    [Tooltip("Default radio channel used when no saved local value exists or when Reset is pressed.")]
    [SerializeField, Range(1, 10)] private int defaultRadioChannel = 1;
    [Tooltip("Default radio transmit status used when no saved local value exists or when Reset is pressed.")]
    [SerializeField] private bool defaultRadioTransmit = true;

    private int _mode = 1;       // 0=Whisper, 1=Talk, 2=Shout
    private int _channel = 1;    // 1-10
    private bool _radioTransmit = true;
    private bool _localDataReady;
    private bool _suppressSliderCallback;
    private VRCPlayerApi[] _players = new VRCPlayerApi[80];

    private void Start()
    {
        InitializeLocalDataIfNeeded();

        if (radioSlider != null)
        {
            radioSlider.minValue = 1f;
            radioSlider.maxValue = 10f;
            radioSlider.wholeNumbers = true;
            _suppressSliderCallback = true;
            radioSlider.value = _channel;
            _suppressSliderCallback = false;
        }

        RefreshUiOnly();
        ApplyAudioForAllPlayers();
    }

    public void SetWhisperMode()
    {
        SetModeAndApply(0);
    }

    public void SetTalkMode()
    {
        SetModeAndApply(1);
    }

    public void SetShoutMode()
    {
        SetModeAndApply(2);
    }

    public void SetRadioLevel(float sliderValue)
    {
        if (_suppressSliderCallback)
        {
            return;
        }

        _channel = Mathf.Clamp(Mathf.RoundToInt(sliderValue), 1, 10);
        SaveLocalData();
        ApplyAudioForAllPlayers();
    }

    public void SetRadioFromSlider()
    {
        if (radioSlider == null)
        {
            return;
        }

        SetRadioLevel(radioSlider.value);
    }

    public void ToggleRadioTransmit()
    {
        SetRadioTransmit(!_radioTransmit);
    }

    public void SetRadioTransmitOn()
    {
        SetRadioTransmit(true);
    }

    public void SetRadioTransmitOff()
    {
        SetRadioTransmit(false);
    }

    public void ResetToDefaults()
    {
        ApplyDefaultSettingsToLocalState();
        SaveLocalData();
        ApplyAudioForAllPlayers();
    }

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            return;
        }

        int intValue;
        bool boolValue;

        if (PlayerData.TryGetInt(player, VoiceModeKey, out intValue))
        {
            _mode = Mathf.Clamp(intValue, 0, 2);
        }
        else
        {
            _mode = Mathf.Clamp((int)defaultMode, 0, 2);
            PlayerData.SetInt(VoiceModeKey, _mode);
        }

        if (PlayerData.TryGetInt(player, VoiceChannelKey, out intValue))
        {
            _channel = Mathf.Clamp(intValue, 1, 10);
        }
        else
        {
            _channel = Mathf.Clamp(defaultRadioChannel, 1, 10);
            PlayerData.SetInt(VoiceChannelKey, _channel);
        }

        if (PlayerData.TryGetBool(player, VoiceRadioTxKey, out boolValue))
        {
            _radioTransmit = boolValue;
        }
        else
        {
            _radioTransmit = defaultRadioTransmit;
            PlayerData.SetBool(VoiceRadioTxKey, _radioTransmit);
        }

        _localDataReady = true;
        ApplyAudioForAllPlayers();
    }

    public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
    {
        if (!Utilities.IsValid(player))
        {
            return;
        }

        ApplyAudioForAllPlayers();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        ApplyAudioForAllPlayers();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        ApplyAudioForAllPlayers();
    }

    private void SetModeAndApply(int newMode)
    {
        _mode = Mathf.Clamp(newMode, 0, 2);
        SaveLocalData();
        ApplyAudioForAllPlayers();
    }

    private void SetRadioTransmit(bool newValue)
    {
        _radioTransmit = newValue;
        SaveLocalData();
        ApplyAudioForAllPlayers();
    }

    private void SaveLocalData()
    {
        if (!_localDataReady)
        {
            InitializeLocalDataIfNeeded();
            if (!_localDataReady)
            {
                return;
            }
        }

        PlayerData.SetInt(VoiceModeKey, _mode);
        PlayerData.SetInt(VoiceChannelKey, _channel);
        PlayerData.SetBool(VoiceRadioTxKey, _radioTransmit);
    }

    private void InitializeLocalDataIfNeeded()
    {
        if (_localDataReady)
        {
            return;
        }

        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer))
        {
            return;
        }

        int intValue;
        bool boolValue;

        if (PlayerData.TryGetInt(localPlayer, VoiceModeKey, out intValue))
        {
            _mode = Mathf.Clamp(intValue, 0, 2);
        }
        else
        {
            _mode = Mathf.Clamp((int)defaultMode, 0, 2);
            PlayerData.SetInt(VoiceModeKey, _mode);
        }

        if (PlayerData.TryGetInt(localPlayer, VoiceChannelKey, out intValue))
        {
            _channel = Mathf.Clamp(intValue, 1, 10);
        }
        else
        {
            _channel = Mathf.Clamp(defaultRadioChannel, 1, 10);
            PlayerData.SetInt(VoiceChannelKey, _channel);
        }

        if (PlayerData.TryGetBool(localPlayer, VoiceRadioTxKey, out boolValue))
        {
            _radioTransmit = boolValue;
        }
        else
        {
            _radioTransmit = defaultRadioTransmit;
            PlayerData.SetBool(VoiceRadioTxKey, _radioTransmit);
        }

        _localDataReady = true;
    }

    private void ApplyAudioForAllPlayers()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer))
        {
            return;
        }

        int intValue;
        bool boolValue;

        int localChannel = _channel;
        int localMode = _mode;
        bool localRadioTx = _radioTransmit;

        if (PlayerData.TryGetInt(localPlayer, VoiceChannelKey, out intValue))
        {
            localChannel = Mathf.Clamp(intValue, 1, 10);
        }
        if (PlayerData.TryGetInt(localPlayer, VoiceModeKey, out intValue))
        {
            localMode = Mathf.Clamp(intValue, 0, 2);
        }
        if (PlayerData.TryGetBool(localPlayer, VoiceRadioTxKey, out boolValue))
        {
            localRadioTx = boolValue;
        }

        _channel = localChannel;
        _mode = localMode;
        _radioTransmit = localRadioTx;

        int playerCount = VRCPlayerApi.GetPlayerCount();
        if (_players == null || _players.Length < playerCount)
        {
            _players = new VRCPlayerApi[playerCount];
        }

        VRCPlayerApi.GetPlayers(_players);

        for (int i = 0; i < _players.Length; i++)
        {
            VRCPlayerApi target = _players[i];
            if (!Utilities.IsValid(target) || target.isLocal)
            {
                continue;
            }

            int targetMode = 1;
            int targetChannel = 1;
            bool targetRadioTx = true;

            if (PlayerData.TryGetInt(target, VoiceModeKey, out intValue))
            {
                targetMode = Mathf.Clamp(intValue, 0, 2);
            }
            if (PlayerData.TryGetInt(target, VoiceChannelKey, out intValue))
            {
                targetChannel = Mathf.Clamp(intValue, 1, 10);
            }
            if (PlayerData.TryGetBool(target, VoiceRadioTxKey, out boolValue))
            {
                targetRadioTx = boolValue;
            }

            float nearDist;
            float farDist;
            float gain;
            GetProximityProfile(targetMode, out nearDist, out farDist, out gain);

            bool sameChannel = targetChannel == localChannel;
            if (sameChannel && targetRadioTx)
            {
                nearDist = radioNear;
                farDist = radioFar;
                gain = radioGain;
                target.SetVoiceLowpass(radioLowpass);
            }
            else
            {
                target.SetVoiceLowpass(false);
            }

            target.SetVoiceDistanceNear(nearDist);
            target.SetVoiceDistanceFar(farDist);
            target.SetVoiceGain(Mathf.Clamp(gain, 0f, 24f));
        }

        RefreshUiOnly();
    }

    private void GetProximityProfile(int mode, out float nearDist, out float farDist, out float gain)
    {
        nearDist = talkNear;
        farDist = talkFar;
        gain = talkGain;

        if (mode == 0)
        {
            nearDist = whisperNear;
            farDist = whisperFar;
            gain = whisperGain;
        }
        else if (mode == 2)
        {
            nearDist = shoutNear;
            farDist = shoutFar;
            gain = shoutGain;
        }
    }

    private void RefreshUiOnly()
    {
        if (radioSlider != null)
        {
            _suppressSliderCallback = true;
            radioSlider.value = _channel;
            _suppressSliderCallback = false;
        }

        string modeName = "Talk";
        if (_mode == 0)
        {
            modeName = "Whisper";
        }
        else if (_mode == 2)
        {
            modeName = "Shout";
        }

        if (modeText != null)
        {
            modeText.text = "Mode: " + modeName;
        }

        if (radioText != null)
        {
            radioText.text = "Channel: " + _channel;
        }

        if (radioTransmitText != null)
        {
            radioTransmitText.text = _radioTransmit ? "Radio: ON" : "Radio: OFF";
        }
    }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
    private const int PersistentListenerModeString = 5;
    private const int UnityEventCallStateRuntimeOnly = 2;

    private void OnValidate()
    {
        if (!autoWireUiEventsInEditor || Application.isPlaying)
        {
            return;
        }

        TryAutoWireUiEvents();
    }

    [ContextMenu("Auto Wire UI Events")]
    public void AutoWireUiEvents()
    {
        TryAutoWireUiEvents();
    }

    private void TryAutoWireUiEvents()
    {
        UdonBehaviour udonBehaviour = (UdonBehaviour)GetComponent(typeof(UdonBehaviour));
        if (udonBehaviour == null)
        {
            return;
        }

        if (whisperButton != null)
        {
            SetSingleStringPersistentCall(whisperButton, "m_OnClick", udonBehaviour, "SendCustomEvent", "SetWhisperMode");
        }

        if (talkButton != null)
        {
            SetSingleStringPersistentCall(talkButton, "m_OnClick", udonBehaviour, "SendCustomEvent", "SetTalkMode");
        }

        if (shoutButton != null)
        {
            SetSingleStringPersistentCall(shoutButton, "m_OnClick", udonBehaviour, "SendCustomEvent", "SetShoutMode");
        }

        if (radioSlider != null)
        {
            SetSingleStringPersistentCall(radioSlider, "m_OnValueChanged", udonBehaviour, "SendCustomEvent", "SetRadioFromSlider");
        }

        if (radioTransmitButton != null)
        {
            SetSingleStringPersistentCall(radioTransmitButton, "m_OnClick", udonBehaviour, "SendCustomEvent", "ToggleRadioTransmit");
        }

        if (resetButton != null)
        {
            SetSingleStringPersistentCall(resetButton, "m_OnClick", udonBehaviour, "SendCustomEvent", "ResetToDefaults");
        }
    }

    private static bool SetSingleStringPersistentCall(
        Object sourceComponent,
        string eventFieldName,
        UdonBehaviour target,
        string methodName,
        string stringArg
    )
    {
        UnityEditor.SerializedObject serializedObject = new UnityEditor.SerializedObject(sourceComponent);
        UnityEditor.SerializedProperty calls = serializedObject.FindProperty(eventFieldName + ".m_PersistentCalls.m_Calls");
        if (calls == null)
        {
            return false;
        }

        string targetTypeName = target.GetType().AssemblyQualifiedName;

        bool alreadyConfigured = calls.arraySize == 1;
        if (alreadyConfigured)
        {
            UnityEditor.SerializedProperty call = calls.GetArrayElementAtIndex(0);
            Object existingTarget = call.FindPropertyRelative("m_Target").objectReferenceValue;
            string existingMethod = call.FindPropertyRelative("m_MethodName").stringValue;
            int existingMode = call.FindPropertyRelative("m_Mode").enumValueIndex;
            string existingArg = call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
            int existingState = call.FindPropertyRelative("m_CallState").enumValueIndex;

            alreadyConfigured =
                existingTarget == target &&
                existingMethod == methodName &&
                existingMode == PersistentListenerModeString &&
                existingArg == stringArg &&
                existingState == UnityEventCallStateRuntimeOnly;
        }

        if (alreadyConfigured)
        {
            return false;
        }

        UnityEditor.Undo.RecordObject(sourceComponent, "Auto Wire Voice Menu UI Event");
        calls.ClearArray();
        calls.InsertArrayElementAtIndex(0);

        UnityEditor.SerializedProperty newCall = calls.GetArrayElementAtIndex(0);
        newCall.FindPropertyRelative("m_Target").objectReferenceValue = target;
        newCall.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = targetTypeName;
        newCall.FindPropertyRelative("m_MethodName").stringValue = methodName;
        newCall.FindPropertyRelative("m_Mode").enumValueIndex = PersistentListenerModeString;
        newCall.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue = stringArg;
        newCall.FindPropertyRelative("m_CallState").enumValueIndex = UnityEventCallStateRuntimeOnly;

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.EditorUtility.SetDirty(sourceComponent);
        return true;
    }
#endif

    private void ApplyDefaultSettingsToLocalState()
    {
        _mode = Mathf.Clamp((int)defaultMode, 0, 2);
        _channel = Mathf.Clamp(defaultRadioChannel, 1, 10);
        _radioTransmit = defaultRadioTransmit;
    }
}
