using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class APIConfig
{
    public string openai_api_key;
    public string model = "gpt-4o-mini";
    public int max_tokens = 300;
    public float temperature = 0.4f;

    public bool HasUsableKey() => ConfigLoader.HasUsableApiKey(openai_api_key);
}

public static class ConfigLoader
{
    public const string ConfigFileName = "config.json";

    static APIConfig cached;

    public static string ConfigPath => Path.Combine(Application.streamingAssetsPath, ConfigFileName);

    public static IEnumerator LoadAsync(Action<APIConfig> onLoaded, Action<string> onError = null)
    {
        if (cached != null) { onLoaded?.Invoke(cached); yield break; }

        string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var req = UnityWebRequest.Get(ConfigPath))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke("Could not read OpenAI config: " + req.error);
                yield break;
            }
            json = req.downloadHandler.text;
        }
#else
        try
        {
            if (!File.Exists(ConfigPath))
            {
                onError?.Invoke("Missing OpenAI config at " + ConfigPath);
                yield break;
            }
            json = File.ReadAllText(ConfigPath);
        }
        catch (Exception ex)
        {
            onError?.Invoke("Could not read OpenAI config: " + ex.Message);
            yield break;
        }
        yield return null;
#endif

        APIConfig parsed;
        try { parsed = JsonUtility.FromJson<APIConfig>(json); }
        catch (Exception ex) { onError?.Invoke("Invalid OpenAI config: " + ex.Message); yield break; }

        if (parsed == null) { onError?.Invoke("config.json could not be parsed."); yield break; }

        if (string.IsNullOrWhiteSpace(parsed.model)) parsed.model = "gpt-4o-mini";
        if (parsed.max_tokens <= 0) parsed.max_tokens = 300;
        parsed.temperature = Mathf.Clamp(parsed.temperature, 0f, 2f);

        cached = parsed;
        onLoaded?.Invoke(cached);
    }

    public static bool HasUsableApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;
        var trimmed = apiKey.Trim();
        if (trimmed.IndexOf("your-key", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return trimmed.StartsWith("sk-", StringComparison.Ordinal);
    }
}
