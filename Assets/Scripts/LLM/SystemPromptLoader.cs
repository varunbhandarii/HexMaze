using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public static class SystemPromptLoader
{
    public const string PromptFileName = "system_prompt.txt";

    static string cached;

    public static string PromptPath => Path.Combine(Application.streamingAssetsPath, PromptFileName);

    public static IEnumerator LoadAsync(Action<string> onLoaded, Action<string> onError = null)
    {
        if (!string.IsNullOrWhiteSpace(cached)) { onLoaded?.Invoke(cached); yield break; }

        string prompt = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var req = UnityWebRequest.Get(PromptPath))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke("Could not read system prompt: " + req.error);
                yield break;
            }
            prompt = req.downloadHandler.text;
        }
#else
        try
        {
            if (!File.Exists(PromptPath))
            {
                onError?.Invoke("Missing system prompt at " + PromptPath);
                yield break;
            }
            prompt = File.ReadAllText(PromptPath);
        }
        catch (Exception ex)
        {
            onError?.Invoke("Could not read system prompt: " + ex.Message);
            yield break;
        }
        yield return null;
#endif

        if (string.IsNullOrWhiteSpace(prompt))
        {
            onError?.Invoke("system_prompt.txt is empty.");
            yield break;
        }

        cached = prompt;
        onLoaded?.Invoke(cached);
    }
}
