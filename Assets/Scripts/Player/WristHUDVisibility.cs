using UnityEngine;
using UnityEngine.UI;

public class WristHUDVisibility : MonoBehaviour
{
    public Canvas hudCanvas;
    public Transform leftController;
    public Transform headTransform;
    public bool useWatchCheck = false;
    public float minHeightBelowHead = 0.55f;
    public float palmUpDotThreshold = 0.35f;
    public bool hideWhenControllerMissing = false;
    public bool forceReadableHud = true;
    public bool useMeshHudInsteadOfCanvas = true;
    public float minimumReadableScale = 0.00145f;
    public Vector2 minimumReadableSize = new Vector2(150f, 95f);
    public GameObject meshHudRoot;

    bool readableApplied;

    void Awake()
    {
        ApplyReadableHudDefaults();
    }

    void Update()
    {
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();

        if (useMeshHudInsteadOfCanvas)
        {
            if (hudCanvas != null) hudCanvas.enabled = false;
            if (meshHudRoot != null) meshHudRoot.SetActive(true);
            return;
        }

        if (!readableApplied) ApplyReadableHudDefaults();
        if (hudCanvas == null) return;

        if (!useWatchCheck) { hudCanvas.enabled = true; return; }

        if (leftController == null)
        {
            hudCanvas.enabled = !hideWhenControllerMissing;
            return;
        }

        float minY = headTransform != null ? headTransform.position.y - minHeightBelowHead : 0.8f;
        bool raised = leftController.position.y > minY;
        bool palmUp = Vector3.Dot(leftController.up, Vector3.up) > palmUpDotThreshold;
        hudCanvas.enabled = raised && palmUp;
    }

    public void ApplyReadableHudDefaults()
    {
        if (hudCanvas == null) hudCanvas = GetComponent<Canvas>();
        if (hudCanvas == null) return;

        hudCanvas.renderMode = RenderMode.WorldSpace;
        hudCanvas.sortingOrder = Mathf.Max(hudCanvas.sortingOrder, 100);

        var rect = GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.sizeDelta = new Vector2(
                Mathf.Max(rect.sizeDelta.x, minimumReadableSize.x),
                Mathf.Max(rect.sizeDelta.y, minimumReadableSize.y));
        }

        if (forceReadableHud)
        {
            float s = Mathf.Max(transform.localScale.x, minimumReadableScale);
            transform.localScale = new Vector3(s, s, s);
            RestyleLabels();
        }

        readableApplied = true;
    }

    void RestyleLabels()
    {
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            img.raycastTarget = false;
            if (img.name.Contains("Panel")) img.color = new Color(0.018f, 0.026f, 0.026f, 0.72f);
            else if (img.name.Contains("Accent")) img.color = new Color(0.18f, 0.72f, 0.64f, 0.9f);
        }

        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (UnityException) { }

        foreach (var label in GetComponentsInChildren<Text>(true))
        {
            label.enabled = true;
            label.gameObject.SetActive(true);
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMaxSize, label.fontSize, 18);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            if (font != null) label.font = font;

            var rect = label.GetComponent<RectTransform>();
            var color = label.color;
            color.a = 1f;

            if (label.name.Contains("Timer"))
            {
                color = new Color(1f, 0.96f, 0.82f, 1f);
                label.fontSize = 18;
                label.resizeTextMaxSize = 18;
                if (rect != null) { rect.anchoredPosition = new Vector2(10f, -8f); rect.sizeDelta = new Vector2(64f, 22f); }
            }
            else if (label.name.Contains("Marker"))
            {
                color = new Color(1f, 0.66f, 0.25f, 1f);
                label.fontSize = 18;
                label.resizeTextMaxSize = 18;
                if (rect != null) { rect.anchoredPosition = new Vector2(-10f, -8f); rect.sizeDelta = new Vector2(64f, 22f); }
            }
            else if (label.name.Contains("Room"))
            {
                color = new Color(0.72f, 1f, 0.92f, 1f);
                label.fontSize = 18;
                label.resizeTextMaxSize = 18;
                if (rect != null) { rect.anchoredPosition = new Vector2(0f, 2f); rect.sizeDelta = new Vector2(130f, 24f); }
            }
            else if (label.name.Contains("Status"))
            {
                color = new Color(0.9f, 0.95f, 0.86f, 1f);
                label.fontSize = 15;
                label.resizeTextMaxSize = 15;
                if (rect != null) { rect.anchoredPosition = new Vector2(0f, 10f); rect.sizeDelta = new Vector2(130f, 24f); }
            }

            label.color = color;
        }
    }
}
