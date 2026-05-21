using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class LockedDoorFeedback : MonoBehaviour
{
    public float hapticAmplitude = 0.8f;
    public float hapticDuration = 0.2f;
    public AudioClip lockedSound;
    public bool useBuiltInLockedSound = true;
    public float feedbackCooldown = 0.25f;
    public float shakeDuration = 0.15f;
    public float shakeIntensity = 0.012f;
    public bool showStatusOnWristHud = true;

    XRBaseInteractable interactable;
    AudioSource audioSrc;
    Vector3 restPos;
    Coroutine shakeCo;
    float lastFeedback = -100f;
    static AudioClip generatedClip;

    void Awake()
    {
        restPos = transform.localPosition;

        interactable = GetComponent<XRBaseInteractable>();
        if (interactable == null)
        {
            var simple = gameObject.AddComponent<XRSimpleInteractable>();
            var mgr = FindFirstObjectByType<XRInteractionManager>();
            if (mgr != null) simple.interactionManager = mgr;
            interactable = simple;
        }

        var body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeAll;

        audioSrc = GetComponent<AudioSource>();
        if (audioSrc == null) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = 0.5f;
        audioSrc.maxDistance = 8f;
        audioSrc.volume = 0.8f;
    }

    void OnEnable()
    {
        if (interactable != null) interactable.selectEntered.AddListener(OnLockedDoorInteract);
    }

    void OnDisable()
    {
        if (interactable != null) interactable.selectEntered.RemoveListener(OnLockedDoorInteract);
    }

    public void OnLockedDoorInteract(SelectEnterEventArgs args)
    {
        if (Time.time - lastFeedback < feedbackCooldown) return;
        lastFeedback = Time.time;

        var serializer = FindFirstObjectByType<MazeStateSerializer>(FindObjectsInactive.Include);
        if (serializer != null) serializer.IncrementLockedDoorAttempts();

        var input = args.interactorObject as XRBaseInputInteractor
                    ?? args.interactorObject.transform.GetComponentInParent<XRBaseInputInteractor>();
        if (input != null) input.SendHapticImpulse(hapticAmplitude, hapticDuration);

        var clip = lockedSound != null ? lockedSound : (useBuiltInLockedSound ? BuiltInClip() : null);
        if (clip != null) audioSrc.PlayOneShot(clip);

        if (showStatusOnWristHud)
        {
            var hud = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
            if (hud != null) hud.SetStatus("Locked", 1.5f);
        }

        if (shakeCo != null)
        {
            StopCoroutine(shakeCo);
            transform.localPosition = restPos;
        }
        shakeCo = StartCoroutine(Shake());
    }

    IEnumerator Shake()
    {
        restPos = transform.localPosition;
        float t = 0f;

        while (t < shakeDuration)
        {
            float falloff = 1f - t / Mathf.Max(0.001f, shakeDuration);
            transform.localPosition = restPos + new Vector3(
                Random.Range(-shakeIntensity, shakeIntensity) * falloff,
                0f,
                Random.Range(-shakeIntensity, shakeIntensity) * falloff);
            t += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = restPos;
        shakeCo = null;
    }

    static AudioClip BuiltInClip()
    {
        if (generatedClip != null) return generatedClip;

        const int sr = 22050;
        const float dur = 0.18f;
        int n = Mathf.RoundToInt(sr * dur);
        var samples = new float[n];

        for (int i = 0; i < n; i++)
        {
            float time = i / (float)sr;
            float fade = Mathf.Exp(-time * 22f);
            float low = Mathf.Sin(2f * Mathf.PI * 92f * time);
            float rattle = Mathf.Sin(2f * Mathf.PI * 310f * time) * 0.35f;
            samples[i] = (low + rattle) * fade * 0.38f;
        }

        generatedClip = AudioClip.Create("LockedDoor_Thud", n, 1, sr, false);
        generatedClip.SetData(samples, 0);
        return generatedClip;
    }
}
