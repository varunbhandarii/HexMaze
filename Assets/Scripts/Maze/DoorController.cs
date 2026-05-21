using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum DoorState { Unlocked, Locked, Open }

public class DoorController : MonoBehaviour, IXRSelectFilter
{
    public DoorState state = DoorState.Unlocked;
    public int wallIndex;

    public float maxOpenAngle = 105f;
    public float closedSnapAngle = 4f;
    public bool requireNearHandle = true;
    public float handleGrabRadius = 0.55f;
    public Vector3 handleLocalPoint = new Vector3(0.34f, 0f, -0.55f);
    public float hingeLocalX = -0.5f;

    public bool autoClose = true;
    public float autoCloseDelay = 2.75f;
    public float autoCloseSpeed = 120f;

    public float lockedHapticAmplitude = 0.8f;
    public float lockedHapticDuration = 0.2f;
    public float lockedShakeDuration = 0.15f;
    public float lockedShakeIntensity = 0.012f;
    public Material unlockedMaterial;
    public Material lockedMaterial;
    public bool revealLockedMaterial = false;
    public bool showLockedStatusOnWristHud = true;
    public AudioClip openSound;
    public AudioClip lockedSound;

    Vector3 closedLocalPos;
    Quaternion closedLocalRot;
    Vector3 closedWorldPos;
    Quaternion closedWorldRot;
    Vector3 hingeLocal;
    Vector3 hingeWorld;
    float openAngle;

    Collider col;
    Renderer rend;
    AudioSource audioSrc;
    XRBaseInteractable interactable;
    Coroutine shakeCo;
    Coroutine closeCo;
    bool hasPose;
    bool dragging;
    Transform grabber;
    Vector3 dragStartVec;
    float dragStartAngle;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        col = GetComponent<Collider>();
        rend = GetComponent<Renderer>();
        audioSrc = GetComponent<AudioSource>();
        interactable = GetComponent<XRBaseInteractable>();
        CapturePose();
        Repaint();
    }

    void OnEnable()
    {
        if (interactable != null)
        {
            interactable.selectFilters.Remove(this);
            interactable.selectFilters.Add(this);
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }
    }

    void OnDisable()
    {
        StopCloseTimer();
        if (interactable != null)
        {
            interactable.selectFilters.Remove(this);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);
        }
    }

    void Update()
    {
        if (dragging) DragStep();
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable target)
    {
        if (!requireNearHandle) return true;
        float r = Mathf.Max(0.05f, handleGrabRadius);
        return Vector3.SqrMagnitude(interactor.transform.position - HandleWorld()) <= r * r;
    }

    public void OnInteract(SelectEnterEventArgs args) => OnSelectEntered(args);

    public void OpenDoor()
    {
        if (!hasPose) CapturePose();
        if (state == DoorState.Locked) return;

        PlayClip(openSound);
        SetAngle(maxOpenAngle);
        ScheduleClose();
    }

    public void SetState(DoorState newState)
    {
        if (!hasPose) CapturePose();
        EndDrag();
        StopCloseTimer();
        var prev = state;

        if (shakeCo != null) { StopCoroutine(shakeCo); shakeCo = null; }

        state = newState;
        if (state == DoorState.Open) SetAngle(maxOpenAngle);
        else SetAngle(0f, false);

        if (col != null) col.enabled = true;
        Repaint();

        if (state == DoorState.Open) ScheduleClose();
        if ((prev == DoorState.Locked) != (state == DoorState.Locked))
            NavMeshRebaker.RequestSceneRebake();
    }

    public void ResetDoor() => SetState(DoorState.Unlocked);

    public void DelayAutoClose()
    {
        if (autoClose && state == DoorState.Open && !dragging) ScheduleClose();
    }

    public void CaptureClosedPosition() => CapturePose();

    void CapturePose()
    {
        closedLocalPos = transform.localPosition;
        closedLocalRot = transform.localRotation;
        closedWorldPos = transform.position;
        closedWorldRot = transform.rotation;

        Vector3 hingeW = transform.TransformPoint(new Vector3(hingeLocalX, 0f, 0f));
        hingeWorld = hingeW;
        hingeLocal = transform.parent != null ? transform.parent.InverseTransformPoint(hingeW) : hingeW;
        openAngle = 0f;
        hasPose = true;
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!hasPose) CapturePose();
        if (state == DoorState.Locked) { RejectPush(args); return; }
        BeginDrag(args);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (grabber == args.interactorObject?.transform) EndDrag();
    }

    void BeginDrag(SelectEnterEventArgs args)
    {
        var t = args.interactorObject?.transform;
        if (t == null || !TryFlatVec(t.position, out Vector3 startVec)) return;

        if (shakeCo != null) { StopCoroutine(shakeCo); shakeCo = null; }

        grabber = t;
        dragStartVec = startVec;
        dragStartAngle = openAngle;
        dragging = true;
        StopCloseTimer();

        if (Mathf.Abs(openAngle) <= closedSnapAngle) PlayClip(openSound);
    }

    void DragStep()
    {
        if (grabber == null) { EndDrag(); return; }
        if (!TryFlatVec(grabber.position, out Vector3 v)) return;

        float delta = Vector3.SignedAngle(dragStartVec, v, Vector3.up);
        SetAngle(Mathf.Clamp(dragStartAngle + delta, -maxOpenAngle, maxOpenAngle));
    }

    void EndDrag()
    {
        dragging = false;
        grabber = null;

        if (Mathf.Abs(openAngle) <= closedSnapAngle) SetAngle(0f);
        else ScheduleClose();
    }

    bool TryFlatVec(Vector3 world, out Vector3 v)
    {
        v = world - HingeWorld();
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) { v = Vector3.zero; return false; }
        v.Normalize();
        return true;
    }

    Vector3 HingeWorld() => transform.parent != null ? transform.parent.TransformPoint(hingeLocal) : hingeWorld;

    Vector3 HandleWorld()
    {
        var h = transform.Find("Handle");
        return h != null ? h.position : transform.TransformPoint(handleLocalPoint);
    }

    void SetAngle(float angle, bool updateState = true)
    {
        openAngle = Mathf.Clamp(angle, -maxOpenAngle, maxOpenAngle);
        Quaternion rot = Quaternion.AngleAxis(openAngle, Vector3.up);

        if (transform.parent != null)
        {
            Vector3 offset = closedLocalPos - hingeLocal;
            transform.localPosition = hingeLocal + rot * offset;
            transform.localRotation = rot * closedLocalRot;
        }
        else
        {
            Vector3 offset = closedWorldPos - hingeWorld;
            transform.SetPositionAndRotation(hingeWorld + rot * offset, rot * closedWorldRot);
        }

        if (col != null) col.enabled = true;
        if (updateState) state = Mathf.Abs(openAngle) > closedSnapAngle ? DoorState.Open : DoorState.Unlocked;
        Repaint();
    }

    void ScheduleClose()
    {
        if (!autoClose || !isActiveAndEnabled || dragging || state == DoorState.Locked) return;
        StopCloseTimer();
        closeCo = StartCoroutine(AutoCloseRoutine());
    }

    void StopCloseTimer()
    {
        if (closeCo != null) { StopCoroutine(closeCo); closeCo = null; }
    }

    IEnumerator AutoCloseRoutine()
    {
        if (autoCloseDelay > 0f) yield return new WaitForSeconds(autoCloseDelay);

        while (!dragging && state == DoorState.Open && Mathf.Abs(openAngle) > closedSnapAngle)
        {
            SetAngle(Mathf.MoveTowards(openAngle, 0f, Mathf.Max(1f, autoCloseSpeed) * Time.deltaTime));
            yield return null;
        }

        if (!dragging && state != DoorState.Locked)
        {
            SetAngle(0f);
            state = DoorState.Unlocked;
            Repaint();
        }

        closeCo = null;
    }

    void RejectPush(SelectEnterEventArgs args)
    {
        var serializer = FindFirstObjectByType<MazeStateSerializer>(FindObjectsInactive.Include);
        if (serializer != null) serializer.IncrementLockedDoorAttempts();

        var input = args.interactorObject as XRBaseInputInteractor
                    ?? args.interactorObject.transform.GetComponentInParent<XRBaseInputInteractor>();
        if (input != null) input.SendHapticImpulse(lockedHapticAmplitude, lockedHapticDuration);

        PlayClip(lockedSound);

        if (showLockedStatusOnWristHud)
        {
            var hud = FindFirstObjectByType<WristHUD>(FindObjectsInactive.Include);
            if (hud != null) hud.SetStatus("Locked", 1.5f);
        }

        if (shakeCo != null) StopCoroutine(shakeCo);
        shakeCo = StartCoroutine(Shake());
    }

    IEnumerator Shake()
    {
        float t = 0f;
        float deg = Mathf.Max(1f, lockedShakeIntensity * 150f);

        while (t < lockedShakeDuration)
        {
            float falloff = 1f - t / Mathf.Max(0.001f, lockedShakeDuration);
            SetAngle(Random.Range(-deg, deg) * falloff, false);
            t += Time.deltaTime;
            yield return null;
        }

        SetAngle(0f, false);
        state = DoorState.Locked;
        shakeCo = null;
        Repaint();
    }

    void Repaint()
    {
        if (rend == null) return;
        if (revealLockedMaterial && state == DoorState.Locked && lockedMaterial != null)
            rend.sharedMaterial = lockedMaterial;
        else if (unlockedMaterial != null)
            rend.sharedMaterial = unlockedMaterial;
    }

    void PlayClip(AudioClip clip)
    {
        if (audioSrc != null && clip != null) audioSrc.PlayOneShot(clip);
    }
}
