using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class LLMClient : MonoBehaviour
{
    public ResponseValidator responseValidator;

    public string apiUrl = "https://api.openai.com/v1/chat/completions";
    public int requestTimeoutSeconds = 5;
    public bool initializeOnStart = true;
    public bool requestJsonObject = true;

    [SerializeField] bool isInitialized;
    [SerializeField] bool requestInFlight;

    string apiKey;
    string model;
    int maxTokens;
    float temperature;
    string systemPrompt;

    public bool IsInitialized => isInitialized;

    void Start()
    {
        if (initializeOnStart) StartCoroutine(Initialize());
    }

    public IEnumerator Initialize()
    {
        isInitialized = false;

        APIConfig cfg = null;
        string err = null;

        yield return ConfigLoader.LoadAsync(r => cfg = r, e => err = e);

        if (!string.IsNullOrEmpty(err)) { Fail(err); yield break; }
        if (cfg == null) { Fail("OpenAI config did not load."); yield break; }
        if (!cfg.HasUsableKey()) { Fail("OpenAI API key is missing or still uses the sample value."); yield break; }

        apiKey = cfg.openai_api_key.Trim();
        model = string.IsNullOrWhiteSpace(cfg.model) ? "gpt-4o-mini" : cfg.model.Trim();
        maxTokens = cfg.max_tokens > 0 ? cfg.max_tokens : 300;
        temperature = Mathf.Clamp(cfg.temperature, 0f, 2f);

        string prompt = null;
        string promptErr = null;
        yield return SystemPromptLoader.LoadAsync(r => prompt = r, e => promptErr = e);

        if (!string.IsNullOrEmpty(promptErr)) { Fail(promptErr); yield break; }

        systemPrompt = prompt;
        isInitialized = true;
    }

    public void SendRequest(string gameStateJson, Action<string> onSuccess, Action<string> onError)
    {
        if (!isInitialized) { onError?.Invoke("LLMClient is not initialized."); return; }
        if (requestInFlight) { onError?.Invoke("LLM request already in flight."); return; }
        if (string.IsNullOrWhiteSpace(gameStateJson)) { onError?.Invoke("Game state JSON is empty."); return; }

        StartCoroutine(SendCoroutine(gameStateJson, onSuccess, onError));
    }

    public void SendRequestForResponse(string gameStateJson, Action<LLMResponse> onSuccess, Action<string> onError)
    {
        SendRequest(gameStateJson, content =>
        {
            var resp = ParseContent(content);
            if (resp == null)
            {
                string msg = responseValidator != null && !string.IsNullOrWhiteSpace(responseValidator.LastRejectionReason)
                    ? responseValidator.LastRejectionReason
                    : "LLM returned content that could not be parsed.";
                onError?.Invoke(msg);
                return;
            }
            onSuccess?.Invoke(resp);
        }, onError);
    }

    IEnumerator SendCoroutine(string gameStateJson, Action<string> onSuccess, Action<string> onError)
    {
        requestInFlight = true;

        string body = BuildBody(gameStateJson);
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(body);

        using (var req = new UnityWebRequest(apiUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(1, requestTimeoutSeconds);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return req.SendWebRequest();

            string responseText = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result != UnityWebRequest.Result.Success)
            {
                string apiErr = ApiErrorMessage(responseText);
                string msg = string.IsNullOrWhiteSpace(apiErr) ? req.error : apiErr;
                requestInFlight = false;
                Debug.LogWarning("LLM API error: " + msg, this);
                onError?.Invoke(msg);
                yield break;
            }

            string content = ExtractAssistantContent(responseText, out string parseErr);
            if (string.IsNullOrWhiteSpace(content))
            {
                string msg = string.IsNullOrWhiteSpace(parseErr) ? "LLM response had no assistant content." : parseErr;
                requestInFlight = false;
                Debug.LogWarning(msg, this);
                onError?.Invoke(msg);
                yield break;
            }

            requestInFlight = false;
            onSuccess?.Invoke(StripCodeFence(content));
        }
    }

    void Fail(string err)
    {
        isInitialized = false;
        Debug.LogWarning("LLMClient init failed: " + err, this);
    }

    string BuildBody(string gameStateJson)
    {
        var req = new ChatRequest
        {
            model = model,
            max_tokens = maxTokens,
            temperature = temperature,
            messages = new[]
            {
                new ChatMessage { role = "system", content = systemPrompt },
                new ChatMessage { role = "user", content = "Current game state:\n" + gameStateJson + "\n\nProvide your maze update." }
            },
            response_format = requestJsonObject ? new ChatResponseFormat { type = "json_object" } : null
        };
        return JsonUtility.ToJson(req);
    }

    LLMResponse ParseContent(string content)
    {
        if (responseValidator == null) responseValidator = FindFirstObjectByType<ResponseValidator>(FindObjectsInactive.Include);
        if (responseValidator != null) return responseValidator.ValidateOrFallback(content);

        try
        {
            var r = JsonUtility.FromJson<LLMResponse>(StripCodeFence(content));
            if (r == null) return null;
            r.lock_doors ??= new DoorCommand[0];
            r.unlock_doors ??= new DoorCommand[0];
            r.guard_targets ??= new GuardCommand[0];
            return r;
        }
        catch { return null; }
    }

    public static string ExtractAssistantContent(string apiResponse, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(apiResponse)) { error = "OpenAI response was empty."; return null; }

        var resp = JsonUtility.FromJson<ChatResponse>(apiResponse);
        if (resp == null) { error = "OpenAI response could not be parsed."; return null; }

        if (resp.error != null && !string.IsNullOrWhiteSpace(resp.error.message)) { error = resp.error.message; return null; }
        if (resp.choices == null || resp.choices.Length == 0) { error = "OpenAI response had no choices."; return null; }
        if (resp.choices[0]?.message == null) { error = "OpenAI choice had no assistant message."; return null; }

        return resp.choices[0].message.content;
    }

    public static string StripCodeFence(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;
        string s = content.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;

        int firstBreak = s.IndexOf('\n');
        int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
        if (firstBreak < 0 || lastFence <= firstBreak) return s;
        return s.Substring(firstBreak + 1, lastFence - firstBreak - 1).Trim();
    }

    static string ApiErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var resp = JsonUtility.FromJson<ChatResponse>(raw);
        return resp?.error?.message ?? "";
    }

    [Serializable]
    class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public int max_tokens;
        public float temperature;
        public ChatResponseFormat response_format;
    }

    [Serializable] class ChatResponseFormat { public string type; }

    [Serializable]
    class ChatResponse
    {
        public ChatChoice[] choices;
        public OpenAIError error;
    }

    [Serializable] class ChatChoice { public ChatMessage message; public string finish_reason; }
    [Serializable] class ChatMessage { public string role; public string content; }
    [Serializable] class OpenAIError { public string message; public string type; public string code; }
}
