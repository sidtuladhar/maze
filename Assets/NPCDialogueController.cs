using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections;

public class NPCDialogueController : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject dialogueUI;
    public TMP_Text reputationText;
    public TMP_Text npcResponseText;
    public Button[] responseButtons;
    public TMP_Text[] responseButtonTexts;

    [Header("NPC Settings")]
    public float interactionRadius = 5f;
    public string systemPrompt;
    public string[] initialNPCResponses;

    private Transform player;
    private bool isPlayerInRange = false;
    private bool isConversationStarted = false;
    private bool isProcessing = false;
    private int reputation = 10;
    private int[] optionReputationDeltas = new int[3]; // +1, -1, -1, etc.

    private string currentNPCLine = "";
    private string[] currentOptions = new string[3];

    private const string OpenAiApiKey = ""; // Add key
    private const string OpenAiApiUrl = "https://api.openai.com/v1/chat/completions";

    void Start()
    {
        dialogueUI.SetActive(false);
        player = GameObject.FindGameObjectWithTag("Player").transform;

        for (int i = 0; i < responseButtons.Length; i++)
        {
            int index = i;
            responseButtons[i].onClick.AddListener(() => OnResponseClick(index));
        }

        UpdateReputationText();
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= interactionRadius && !isPlayerInRange)
        {
            isPlayerInRange = true;
            UnlockCursor();
            dialogueUI.SetActive(true);

            if (!isConversationStarted)
            {
                isConversationStarted = true;
                currentNPCLine = initialNPCResponses[Random.Range(0, initialNPCResponses.Length)];
                npcResponseText.text = currentNPCLine;

                Debug.Log($"[Start] NPC greeting: {currentNPCLine}");
                StartCoroutine(HandleInitialDialogue(currentNPCLine));

            }
            else
            {
                Debug.Log("[Re-entry] Continuing conversation...");
                npcResponseText.text = currentNPCLine;
                for (int i = 0; i < 3; i++)
                {
                    responseButtonTexts[i].text = currentOptions[i];
                }
            }
        }
        else if (distance > interactionRadius && isPlayerInRange)
        {
            isPlayerInRange = false;
            dialogueUI.SetActive(false);
            LockCursor();
        }
    }
    private IEnumerator HandleInitialDialogue(string message)
    {
        var task = SendMessageToChatGPT(message);
        while (!task.IsCompleted) yield return null;

        var result = task.Result;
        currentNPCLine = result.Item1;
        currentOptions = result.Item2;

        for (int i = 0; i < 3; i++)
        {
            if (!string.IsNullOrWhiteSpace(currentOptions[i]))
                responseButtonTexts[i].text = CleanOptionForButton(currentOptions[i]);
            else
                responseButtonTexts[i].text = "MISSING OPTION";
        }

        // ❌ Remove this line:
        // reputation += optionReputationDeltas[index];

        UpdateReputationText();
    }


    async void OnResponseClick(int index)
    {
        if (isProcessing) return;
        isProcessing = true;

        if (string.IsNullOrEmpty(currentOptions[index]))
        {
            Debug.LogWarning($"[Click] No valid option at index {index}");
            isProcessing = false;
            return;
        }

        string selected = currentOptions[index];
        Debug.Log($"[Click] Player selected: {selected}");

        reputation += optionReputationDeltas[index]; // ✅ Apply rep change based on clicked option
        UpdateReputationText();

        var result = await SendMessageToChatGPT(selected);
        currentNPCLine = result.Item1;
        npcResponseText.text = currentNPCLine;

        currentOptions = result.Item2;
        for (int i = 0; i < 3; i++)
        {
            if (!string.IsNullOrWhiteSpace(currentOptions[i]))
            {
                string displayText = CleanOptionForButton(currentOptions[i]);
                responseButtonTexts[i].text = displayText;
                Debug.Log($"[UI] Button {i}: '{displayText}'");
            }
            else
            {
                responseButtonTexts[i].text = "MISSING OPTION";
                Debug.LogWarning($"[UI] Button {i}: MISSING");
            }
        }

        isProcessing = false;
    }


    async Task<(string, string[], int)> SendMessageToChatGPT(string message)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {OpenAiApiKey}");

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = message }
                },
                max_tokens = 250
            };

            string json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            Debug.Log($"[API] Sending: {message}");

            try
            {
                HttpResponseMessage response = await client.PostAsync(OpenAiApiUrl, content);
                string jsonResult = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<ChatGPTResponse>(jsonResult);
                    string reply = data.choices[0].message.content.Trim();

                    Debug.Log($"[API Response] Raw: {reply}");

                    return ParseChatGPTResponse(reply);
                }
                else
                {
                    Debug.LogError($"[API] Failed: {response.StatusCode}");
                    return ("Error: Failed API call", new string[3], 0);
                }
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"[API] Exception: {e.Message}");
                return ("Error: Exception", new string[3], 0);
            }
        }
    }

    (string, string[], int) ParseChatGPTResponse(string fullResponse)
    {
        string npcLine = "";
        string[] options = new string[3];
        int repDelta = 0;

        // Handle case where everything is on one line
        if (fullResponse.Contains("OPTIONS:"))
        {
            string[] parts = fullResponse.Split(new[] { "OPTIONS:" }, System.StringSplitOptions.None);
            npcLine = parts[0].Trim();

            string optionsText = parts.Length > 1 ? parts[1].Trim() : "";
            string[] rawOptions = optionsText.Split(new[] { "(+1)", "(-1)" }, System.StringSplitOptions.None);

            List<string> parsedOptions = new List<string>();
            int currentIndex = 0;

            foreach (string chunk in rawOptions)
            {
                string clean = chunk.TrimStart('-', ' ', '\n', '\r').Trim();
                if (string.IsNullOrWhiteSpace(clean)) continue;

                string tag = optionsText.Contains(clean + " (+1)") ? "(+1)" : "(-1)";
                parsedOptions.Add($"{clean} {tag}");

                if (tag == "(+1)")
                    repDelta = 1;

                currentIndex++;
                if (currentIndex == 3) break;
            }

            for (int i = 0; i < 3; i++)
            {
                options[i] = i < parsedOptions.Count ? parsedOptions[i] : "Option missing (-1)";
            }

            Debug.Log($"[Parsed] NPC: {npcLine}, Reputation Change: {repDelta}");
            Debug.Log($"[Options] 1: {options[0]} | 2: {options[1]} | 3: {options[2]}");

            optionReputationDeltas = new int[3]; // reset
            for (int i = 0; i < 3; i++)
            {
                if (i < parsedOptions.Count)
                {
                    options[i] = parsedOptions[i];
                    optionReputationDeltas[i] = parsedOptions[i].Contains("(+1)") ? 1 : -1;
                }
                else
                {
                    options[i] = "Option missing (-1)";
                    optionReputationDeltas[i] = -1;
                }
            }
            return (npcLine, options, 0); // We don’t assign global rep here anymore
        }

        // If no OPTIONS section, treat whole message as error
        Debug.LogWarning("[Parse] No OPTIONS section found. Returning full message as NPC line.");
        return (fullResponse.Trim(), new[] { "Option missing (-1)", "Option missing (-1)", "Option missing (-1)" }, 0);
    }

    void UpdateReputationText()
    {
        reputationText.text = $"Reputation: {reputation}";
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    [System.Serializable]
    private class ChatGPTResponse
    {
        public Choice[] choices;
    }

    [System.Serializable]
    private class Choice
    {
        public Message message;
    }

    [System.Serializable]
    private class Message
    {
        public string role;
        public string content;
    }
    private string CleanOptionForButton(string option)
    {
        // Remove (+1) or (-1)
        option = option.Replace("(+1)", "").Replace("(-1)", "").Trim();

        // Remove square brackets
        if (option.StartsWith("[") && option.EndsWith("]"))
        {
            option = option.Substring(1, option.Length - 2).Trim();
        }

        // Remove surrounding quotes
        if ((option.StartsWith("\"") && option.EndsWith("\"")) ||
            (option.StartsWith("'") && option.EndsWith("'")))
        {
            option = option.Substring(1, option.Length - 2).Trim();
        }

        // Remove leading dash
        if (option.StartsWith("- "))
        {
            option = option.Substring(2).Trim();
        }

        return option;
    }


}
