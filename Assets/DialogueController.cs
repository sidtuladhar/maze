using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;

public class DialogueController : MonoBehaviour
{
    [Header("UI Document Reference")]
    public UIDocument uiDocument;

    [Header("Dialogue Content")]
    [TextArea] public string npcResponseText;
    public string optionOneText;
    public string optionTwoText;
    public string optionThreeText;

    [Header("Interaction Settings")]
    public Transform player;
    public Transform npc;
    public float interactionRadius = 5f;
    public float typingSpeed = 0.05f;

    private VisualElement root;
    private Label npcResponseLabel;
    private Button optionOneButton;
    private Button optionTwoButton;
    private Button optionThreeButton;

    private bool isTyping = false;

    void Start()
    {
        // Access UI root
        root = uiDocument.rootVisualElement;

        // Get references by name
        npcResponseLabel = root.Q<Label>("NPCResponse");
        optionOneButton = root.Q<Button>("optionOne");
        optionTwoButton = root.Q<Button>("optionTwo");
        optionThreeButton = root.Q<Button>("optionThree");

        // Apply initial texts
        npcResponseLabel.text = npcResponseText;
        optionOneButton.text = optionOneText;
        optionTwoButton.text = optionTwoText;
        optionThreeButton.text = optionThreeText;

        // Hide UI at start
        uiDocument.gameObject.SetActive(false);
    }

    void Update()
    {
        float distance = Vector3.Distance(player.position, npc.position);
        Vector3 directionToPlayer = player.position - npc.position;
        directionToPlayer.y = 0;

        if (distance <= interactionRadius && !isTyping)
        {
            uiDocument.gameObject.SetActive(true);
            UnlockCursor();

            if (directionToPlayer != Vector3.zero)
            {
                // Rotate NPC to face player
                Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
                npc.rotation = Quaternion.Slerp(npc.rotation, targetRotation, Time.deltaTime * 5f);
            }

        }
        else if (distance > interactionRadius)
        {
            uiDocument.gameObject.SetActive(false);
            LockCursor();
        }
    }

    IEnumerator TypeNPCText()
    {
        isTyping = true;
        npcResponseLabel.text = ""; // Clear the label before typing

        foreach (char letter in npcResponseText.ToCharArray())
        {
            npcResponseLabel.text += letter;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false; // Typing is finished
    }

    void UnlockCursor()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
    }

    private void LockCursor()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        UnityEngine.Cursor.visible = false;
    }

}
