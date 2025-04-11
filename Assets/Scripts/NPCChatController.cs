using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class NPCChatController : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject chatBoxUI;
    public TMP_InputField playerInputField;
    public TMP_Text npcResponseText;
    public TMP_Text reputationText;
    public Button sendButton;

    [Header("NPC Settings")]
    public float interactionRadius = 5f;
    public string systemPrompt = "You are a generic NPC with no special personality.";

    private bool isPlayerInRange = false;
    private Transform player;
    private int reputation = 10; // Default reputation
    private bool isProcessingMessage = false; // Prevents overlapping requests

    private const string OpenAiApiKey = ""; // Replace with actual key //POPOP
    private const string OpenAiApiUrl = "https://api.openai.com/v1/chat/completions";

    void Start()
    {
        chatBoxUI.SetActive(false);
        player = GameObject.FindGameObjectWithTag("Player").transform;
        LockCursor();

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSubmitMessage);
        }
        else
        {
            Debug.LogError("Send Button is not assigned in the Inspector!");
        }

        UpdateReputationText(); // Initialize UI
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= interactionRadius)
        {
            if (!isPlayerInRange)
            {
                isPlayerInRange = true;
                chatBoxUI.SetActive(true);
                UnlockCursor();
            }

            if (Input.GetMouseButtonDown(0) && !IsMouseOverChatbox())
            {
                CloseChatbox();
            }
        }
        else
        {
            if (isPlayerInRange)
            {
                CloseChatbox();
            }
        }
    }

    public async void OnSubmitMessage()
    {
        if (isProcessingMessage) return; // Prevents multiple API calls
        isProcessingMessage = true;

        string playerMessage = playerInputField.text;
        playerInputField.text = "";

        Debug.Log($"Player Message: {playerMessage}");

        string npcResponse = await SendMessageToChatGPT(playerMessage);

        Debug.Log($"Raw NPC Response: {npcResponse}");

        // Process response to extract reputation change
        (string cleanResponse, int reputationChange) = ParseReputationResponse(npcResponse);

        // If the response didn't include a reputation score, retry once
        if (reputationChange == 0)
        {
            Debug.LogWarning("NPC response did not include +1 or -1. Retrying...");
            npcResponse = await SendMessageToChatGPT(playerMessage);
            (cleanResponse, reputationChange) = ParseReputationResponse(npcResponse);
        }

        // Update UI
        npcResponseText.text = cleanResponse;
        reputation += reputationChange;
        UpdateReputationText();

        Debug.Log($"Reputation Change: {reputationChange} | New Reputation: {reputation}");

        isProcessingMessage = false; // Reset flag
    }

    private async Task<string> SendMessageToChatGPT(string message)
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
                    new { role = "system", content = systemPrompt }, // Use the inspector-assigned prompt
                    new { role = "user", content = message }
                },
                max_tokens = 100 // Prevent truncation
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            Debug.Log($"Sending API Request: {jsonBody}");

            try
            {
                HttpResponseMessage response = await client.PostAsync(OpenAiApiUrl, content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Debug.Log($"API Response: {responseJson}");
                    var responseData = JsonConvert.DeserializeObject<ChatGPTResponse>(responseJson);
                    return responseData.choices[0].message.content;
                }
                else
                {
                    Debug.LogError($"API Request Failed: {response.StatusCode} - {responseJson}");
                    return "Error: API request failed. Please check API key and permissions.";
                }
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"API Request Exception: {e.Message}");
                return "Error: Network or API request issue.";
            }
        }
    }

    private (string, int) ParseReputationResponse(string response)
    {
        string cleanResponse = response.Trim();
        int reputationChange = 0;

        string[] lines = cleanResponse.Split('\n');
        if (lines.Length > 1)
        {
            string lastLine = lines[lines.Length - 1].Trim();
            if (lastLine == "+1")
            {
                reputationChange = 1;
            }
            else if (lastLine == "-1")
            {
                reputationChange = -1;
            }
            else
            {
                Debug.LogWarning("No reputation score detected in NPC response.");
            }

            cleanResponse = string.Join("\n", lines, 0, lines.Length - 1);
        }

        return (cleanResponse, reputationChange);
    }

    private void UpdateReputationText()
    {
        if (reputationText != null)
        {
            reputationText.text = $"Reputation: {reputation}";
        }
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

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CloseChatbox()
    {
        isPlayerInRange = false;
        chatBoxUI.SetActive(false);
        LockCursor();
    }

    private bool IsMouseOverChatbox()
    {
        RectTransform chatboxRect = chatBoxUI.GetComponent<RectTransform>();
        Vector2 localMousePosition = chatboxRect.InverseTransformPoint(Input.mousePosition);
        return chatboxRect.rect.Contains(localMousePosition);
    }
}
