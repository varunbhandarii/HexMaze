using System.Collections.Generic;
using UnityEngine;

public class AmbientSoundManager : MonoBehaviour
{
    public AudioClip ambientDrone;
    public AudioClip[] randomSounds;
    public MazeGridManager gridManager;
    public bool playDroneOnStart = true;
    public bool playRandomSpatialSounds = true;
    public float droneVolume = 0.12f;
    public float randomSoundVolume = 0.28f;
    public float minInterval = 10f;
    public float maxInterval = 24f;
    public float randomHeightOffset = 1.4f;
    public float minDistance = 1f;
    public float maxDistance = 14f;

    AudioSource drone;
    float nextRandom;

    void Awake()
    {
        drone = GetComponent<AudioSource>();
        if (drone == null) drone = gameObject.AddComponent<AudioSource>();
        drone.playOnAwake = false;
        drone.loop = true;
        drone.volume = droneVolume;
        drone.spatialBlend = 0f;
        drone.priority = 180;
    }

    void Start()
    {
        if (gridManager == null) gridManager = FindFirstObjectByType<MazeGridManager>();
        if (playDroneOnStart && ambientDrone != null)
        {
            drone.clip = ambientDrone;
            drone.Play();
        }
        nextRandom = Time.time + Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));
    }

    void Update()
    {
        if (!playRandomSpatialSounds || randomSounds == null || randomSounds.Length == 0) return;
        if (Time.time < nextRandom) return;

        PlayOneShot();
        nextRandom = Time.time + Random.Range(minInterval, Mathf.Max(minInterval, maxInterval));
    }

    void PlayOneShot()
    {
        AudioClip clip = null;
        for (int i = 0; i < randomSounds.Length && clip == null; i++)
            clip = randomSounds[Random.Range(0, randomSounds.Length)];
        if (clip == null) return;

        var pos = transform.position + Vector3.up * randomHeightOffset;
        if (gridManager != null && gridManager.GetAllRooms().Count > 0)
        {
            var rooms = new List<HexRoom>(gridManager.GetAllRooms().Values);
            pos = rooms[Random.Range(0, rooms.Count)].transform.position + Vector3.up * randomHeightOffset;
        }

        var go = new GameObject($"AmbientOneShot_{clip.name}");
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = randomSoundVolume;
        src.spatialBlend = 1f;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.priority = 170;
        src.Play();
        Destroy(go, clip.length + 0.25f);
    }
}
