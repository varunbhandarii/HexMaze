using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

public class CrouchDetector : MonoBehaviour
{
    public enum ControllerButton
    {
        PrimaryButton, SecondaryButton, GripButton, TriggerButton, Primary2DAxisClick
    }

    public Transform cameraTransform;
    public bool requireCalibration = true;
    public bool calibrated;
    public float standingHeight = 1.6f;
    [Range(0.4f, 0.9f)] public float crouchThreshold = 0.65f;
    public float crouchHysteresis = 0.06f;
    public float minimumValidHeadHeight = 0.5f;

    public bool allowControllerCalibration = true;
    public XRNode calibrationHand = XRNode.RightHand;
    public ControllerButton calibrationButton = ControllerButton.PrimaryButton;

    public bool isCrouching;
    public float currentHeadHeight;
    public float crouchHeightLimit;
    public UnityEvent onCalibrated = new UnityEvent();
    public UnityEvent onCrouchStarted = new UnityEvent();
    public UnityEvent onCrouchEnded = new UnityEvent();

    public bool showCalibrationPrompt = true;
    public bool showLiveStatus = false;
    public Vector3 promptOffset = new Vector3(-0.3f, -0.38f, 1.1f);
    public float promptTextSize = 0.009f;
    public float promptHideDelayAfterCalibration = 2f;
    public Color promptColor = new Color(0.9f, 0.86f, 0.72f, 1f);

    TextMesh prompt;
    bool prevButton;
    float lastCalibrationTime = -100f;

    void Start()
    {
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        crouchHeightLimit = standingHeight * crouchThreshold;
        if (!requireCalibration) calibrated = true;
        EnsurePrompt();
        UpdatePromptText();
    }

    void Update()
    {
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        if (allowControllerCalibration)
        {
            bool down = ReadButton();
            if (down && !prevButton) CalibrateStandingHeight();
            prevButton = down;
        }

        currentHeadHeight = cameraTransform != null ? Mathf.Max(0f, cameraTransform.localPosition.y) : standingHeight;

        if (calibrated || !requireCalibration) UpdateCrouchState();
        UpdatePromptText();
    }

    public void CalibrateStandingHeight()
    {
        float h = cameraTransform != null ? Mathf.Max(0f, cameraTransform.localPosition.y) : standingHeight;

        if (h >= minimumValidHeadHeight) standingHeight = h;
        else Debug.LogWarning($"Crouch calibration read {h:0.00}m, keeping {standingHeight:0.00}m.", this);

        calibrated = true;
        lastCalibrationTime = Time.time;
        crouchHeightLimit = standingHeight * crouchThreshold;
        UpdateCrouchState();
        onCalibrated?.Invoke();

        if (GameManager.Current != null) GameManager.Current.HandleCrouchCalibrationButton();
    }

    public void ResetCalibration()
    {
        calibrated = false;
        isCrouching = false;
        lastCalibrationTime = -100f;
        UpdatePromptText();
    }

    void UpdateCrouchState()
    {
        bool was = isCrouching;
        float exitHeight = crouchHeightLimit + crouchHysteresis;
        isCrouching = was ? currentHeadHeight < exitHeight : currentHeadHeight < crouchHeightLimit;

        if (was == isCrouching) return;
        if (isCrouching) onCrouchStarted?.Invoke();
        else onCrouchEnded?.Invoke();
    }

    bool ReadButton()
    {
        var device = InputDevices.GetDeviceAtXRNode(calibrationHand);
        if (!device.isValid) return false;

        InputFeatureUsage<bool> usage = CommonUsages.primaryButton;
        switch (calibrationButton)
        {
            case ControllerButton.SecondaryButton: usage = CommonUsages.secondaryButton; break;
            case ControllerButton.GripButton: usage = CommonUsages.gripButton; break;
            case ControllerButton.TriggerButton: usage = CommonUsages.triggerButton; break;
            case ControllerButton.Primary2DAxisClick: usage = CommonUsages.primary2DAxisClick; break;
        }
        return device.TryGetFeatureValue(usage, out bool pressed) && pressed;
    }

    void EnsurePrompt()
    {
        if (!showCalibrationPrompt || cameraTransform == null || prompt != null) return;

        var go = new GameObject("Crouch Calibration Prompt");
        go.transform.SetParent(cameraTransform, false);
        go.transform.localPosition = promptOffset;

        prompt = go.AddComponent<TextMesh>();
        prompt.anchor = TextAnchor.MiddleCenter;
        prompt.alignment = TextAlignment.Center;
        prompt.fontSize = 64;
        prompt.characterSize = promptTextSize;
        prompt.lineSpacing = 0.75f;
        prompt.color = promptColor;
    }

    void UpdatePromptText()
    {
        if (!showCalibrationPrompt)
        {
            if (prompt != null) prompt.gameObject.SetActive(false);
            return;
        }

        EnsurePrompt();
        if (prompt == null) return;

        if (!calibrated && requireCalibration)
        {
            prompt.gameObject.SetActive(true);
            prompt.text = "Stand + A";
            return;
        }

        if (Time.time - lastCalibrationTime < promptHideDelayAfterCalibration)
        {
            prompt.gameObject.SetActive(true);
            prompt.text = $"Crouch set\nbelow {crouchHeightLimit:0.00}m";
            return;
        }

        if (showLiveStatus)
        {
            prompt.gameObject.SetActive(true);
            prompt.text = isCrouching ? "Crouch" : "Standing";
            return;
        }

        prompt.gameObject.SetActive(false);
    }
}
