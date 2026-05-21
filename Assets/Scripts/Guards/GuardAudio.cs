using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(AudioSource))]
public class GuardAudio : MonoBehaviour
{
    public AudioClip[] footstepClips;
    public float stepInterval = 0.5f;
    public float chaseStepIntervalMultiplier = 0.6f;
    public float movementThreshold = 0.1f;
    [Range(0f, 1f)] public float footstepVolume = 0.6f;
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    public AudioClip alertSound;
    public AudioClip chaseSound;
    [Range(0f, 1f)] public float alertVolume = 0.8f;
    [Range(0f, 1f)] public float chaseVolume = 1f;

    public float minDistance = 1f;
    public float maxDistance = 15f;
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

    public bool useSimpleOcclusion = true;
    public LayerMask occlusionLayers = ~0;
    [Range(0f, 1f)] public float occludedVolumeMultiplier = 0.35f;
    public bool useLowPassWhenOccluded = true;
    public float clearLowPassCutoff = 22000f;
    public float occludedLowPassCutoff = 1400f;

    AudioSource audioSrc;
    AudioLowPassFilter lowPass;
    NavMeshAgent agent;
    GuardAI ai;
    Transform listener;
    float stepTimer;

    void Awake()
    {
        audioSrc = GetComponent<AudioSource>();
        agent = GetComponent<NavMeshAgent>();
        ai = GetComponent<GuardAI>();
        ApplyAudioSettings();
    }

    void Update()
    {
        ApplyAudioSettings();
        UpdateOcclusion();
        UpdateFootsteps();
    }

    public void PlayAlertSound()
    {
        if (alertSound != null) { audioSrc.pitch = 1f; audioSrc.PlayOneShot(alertSound, alertVolume); }
    }

    public void PlayChaseSound()
    {
        if (chaseSound != null) { audioSrc.pitch = 1f; audioSrc.PlayOneShot(chaseSound, chaseVolume); }
    }

    void UpdateFootsteps()
    {
        if (agent == null || ai == null) return;
        if (ai.currentState == GuardState.Blocked) { stepTimer = 0f; return; }
        if (agent.velocity.magnitude < movementThreshold) return;

        stepTimer += Time.deltaTime;
        float interval = ai.currentState == GuardState.Chase ? stepInterval * chaseStepIntervalMultiplier : stepInterval;
        if (stepTimer < Mathf.Max(0.08f, interval)) return;

        if (footstepClips != null && footstepClips.Length > 0)
        {
            var clip = footstepClips[Random.Range(0, footstepClips.Length)];
            audioSrc.pitch = Random.Range(Mathf.Min(pitchRange.x, pitchRange.y), Mathf.Max(pitchRange.x, pitchRange.y));
            audioSrc.PlayOneShot(clip, footstepVolume);
        }
        stepTimer = 0f;
    }

    void UpdateOcclusion()
    {
        if (!useSimpleOcclusion) return;

        if (listener == null && Camera.main != null) listener = Camera.main.transform;
        if (listener == null)
        {
            audioSrc.volume = 1f;
            SetCutoff(clearLowPassCutoff);
            return;
        }

        Vector3 from = transform.position + Vector3.up * 1.2f;
        Vector3 to = listener.position;
        Vector3 dir = to - from;
        float dist = dir.magnitude;

        bool occluded = false;
        if (dist > 0.001f)
        {
            var hits = Physics.RaycastAll(from, dir.normalized, dist, occlusionLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                var ht = hit.collider.transform;
                if (ht.root == transform.root) continue;
                if (listener != null && (ht == listener || ht.IsChildOf(listener) || listener.IsChildOf(ht) || ht.root == listener.root)) continue;
                occluded = true;
                break;
            }
        }

        audioSrc.volume = occluded ? occludedVolumeMultiplier : 1f;
        SetCutoff(occluded ? occludedLowPassCutoff : clearLowPassCutoff);
    }

    void SetCutoff(float cutoff)
    {
        if (!useLowPassWhenOccluded)
        {
            if (lowPass != null) lowPass.enabled = false;
            return;
        }

        if (lowPass == null)
        {
            lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        }

        lowPass.enabled = true;
        lowPass.cutoffFrequency = cutoff;
    }

    void ApplyAudioSettings()
    {
        if (audioSrc == null) return;
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 1f;
        audioSrc.minDistance = minDistance;
        audioSrc.maxDistance = maxDistance;
        audioSrc.rolloffMode = rolloffMode;
        audioSrc.dopplerLevel = 0.25f;
    }
}
