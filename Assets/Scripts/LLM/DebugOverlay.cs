using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class DebugOverlay : MonoBehaviour
{
    public Canvas canvas;
    public Text text;
    public MazeBrain brain;
    public CommandDispatcher dispatcher;
    public Camera headCamera;

    public bool showOnStart;
    public float distance = 1.35f;
    public float yOffset = -0.12f;
    public float refreshTime = 0.25f;

    bool showing;
    bool gripsPrev;
    float nextRefresh;
    readonly StringBuilder sb = new StringBuilder(256);

    void Awake()
    {
        ResolveRefs();
        BuildPanel();
        SetVisible(showOnStart);
    }

    void Update()
    {
        bool grips = BothGrips();
        if (grips && !gripsPrev) SetVisible(!showing);
        gripsPrev = grips;

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F1)) SetVisible(!showing);
#endif

        if (!showing) return;

        PutInFront();
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshTime;
            Refresh();
        }
    }

    public void SetVisible(bool value)
    {
        showing = value;
        if (canvas != null) canvas.enabled = value;
        if (value) { Refresh(); PutInFront(); }
    }

    void Refresh()
    {
        ResolveRefs();
        sb.Length = 0;
        sb.AppendLine("AI MAZE DIRECTOR");

        if (brain != null)
            sb.Append("Calls ").Append(brain.GetCallCount())
              .Append("  Thinking ").AppendLine(brain.IsRequestPending() ? "yes" : "no");

        sb.AppendLine().AppendLine("Latest move");
        sb.AppendLine(dispatcher != null && !string.IsNullOrWhiteSpace(dispatcher.LastDemoSummary)
            ? Truncate(dispatcher.LastDemoSummary, 130)
            : "No maze change yet.");

        if (dispatcher != null && !string.IsNullOrWhiteSpace(dispatcher.LastReasoning))
        {
            sb.AppendLine().Append("Why: ").AppendLine(Truncate(dispatcher.LastReasoning, 90));
        }

        sb.AppendLine().Append("Both grips: hide/show");
        if (text != null) text.text = sb.ToString();
    }

    void BuildPanel()
    {
        if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            var go = new GameObject("AI Overlay Canvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 250;
        canvas.transform.localScale = Vector3.one * 0.0017f;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(460f, 265f);

        var bg = canvas.GetComponentInChildren<Image>(true);
        if (bg == null)
        {
            bg = new GameObject("Background").AddComponent<Image>();
            bg.transform.SetParent(canvas.transform, false);
        }
        bg.color = new Color(0f, 0f, 0f, 0.78f);
        bg.raycastTarget = false;
        Stretch(bg.rectTransform, Vector2.zero);

        if (text == null) text = canvas.GetComponentInChildren<Text>(true);
        if (text == null)
        {
            text = new GameObject("Text").AddComponent<Text>();
            text.transform.SetParent(canvas.transform, false);
        }

        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 13;
        text.resizeTextMaxSize = 18;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.color = new Color(0.86f, 1f, 0.96f, 1f);
        text.raycastTarget = false;
        Stretch(text.rectTransform, new Vector2(18f, 14f));
    }

    void PutInFront()
    {
        if (canvas == null) return;
        if (headCamera == null) headCamera = Camera.main;
        if (headCamera == null) return;

        var head = headCamera.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = head.forward;

        Vector3 pos = head.position + fwd * distance;
        pos.y = head.position.y + yOffset;
        canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
    }

    void ResolveRefs()
    {
        if (brain == null) brain = FindFirstObjectByType<MazeBrain>(FindObjectsInactive.Include);
        if (dispatcher == null) dispatcher = FindFirstObjectByType<CommandDispatcher>(FindObjectsInactive.Include);
        if (headCamera == null) headCamera = Camera.main;
    }

    static bool BothGrips() => GripDown(XRNode.LeftHand) && GripDown(XRNode.RightHand);

    static bool GripDown(XRNode hand)
    {
        var d = InputDevices.GetDeviceAtXRNode(hand);
        return d.isValid && d.TryGetFeatureValue(CommonUsages.gripButton, out bool p) && p;
    }

    static void Stretch(RectTransform r, Vector2 pad)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = pad;
        r.offsetMax = -pad;
    }

    static string Truncate(string v, int len)
        => string.IsNullOrEmpty(v) || v.Length <= len ? v : v.Substring(0, len - 3) + "...";
}
