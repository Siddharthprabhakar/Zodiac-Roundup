using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;
using System;
using System.Text.RegularExpressions;

public class UIButton : MonoBehaviour
{
    public GameObject panel;
    public TMP_InputField joinCodeField;
    public UnityEngine.UI.Button joinRelayButton;

    [Header("Relay room UI")]
    [Tooltip("Optional lobby text that displays the room code to the host.")]
    public TMP_Text roomCodeText;

    [Tooltip("Optional Create Room button, used to prevent double-clicking while connecting.")]
    public UnityEngine.UI.Button hostRelayButton;

    [Header("Connection feedback")]
    [SerializeField] private TMP_Text connectionStatusText;
    [SerializeField] private TMP_Text connectionErrorText;
    [SerializeField] private UnityEngine.UI.Button copyCodeButton;
    [SerializeField] private UnityEngine.UI.Button legacyClientButton;

    bool _joining;
    bool _hosting;
    string _currentRoomCode = "";
    [SerializeField] private TMP_InputField nameInput;
    [SerializeField] private GameObject lobbyPanel;

    void Awake()
    {
        ConfigureConnectionScreen();
    }

    void ConfigureConnectionScreen()
    {
        SetButtonLabel(hostRelayButton, "Create Room");
        SetButtonLabel(joinRelayButton, "Join Room");

        if (!legacyClientButton && panel)
        {
            Transform oldClient = panel.transform.Find("Client Button");
            if (oldClient) legacyClientButton = oldClient.GetComponent<UnityEngine.UI.Button>();
        }

        if (legacyClientButton) legacyClientButton.gameObject.SetActive(false);

        if (!connectionStatusText)
            connectionStatusText = CreateRuntimeText("ConnectionStatusText", panel.transform, new Vector2(0, -150), Color.white);

        if (!connectionErrorText)
            connectionErrorText = CreateRuntimeText("ConnectionErrorText", panel.transform, new Vector2(0, -190), new Color(1f, 0.35f, 0.35f));

        if (!copyCodeButton)
            copyCodeButton = CreateCopyCodeButton();

        if (copyCodeButton)
        {
            copyCodeButton.onClick.RemoveListener(CopyRoomCode);
            copyCodeButton.onClick.AddListener(CopyRoomCode);
            copyCodeButton.interactable = false;
        }

        SetStatus("");
        ShowError("");
    }

    public async void StartHost()
    {
        if (_hosting || _joining) return;

        try
        {
            _hosting = true;
            SetBusy(true, "Creating room...");

            if (GameState.Instance) GameState.Instance.ChangeName(nameInput ? nameInput.text.Trim() : "");

            string joinCode = await RelayManager.Instance.CreateRelayAndStartHostAsync();
            if (string.IsNullOrEmpty(joinCode))
                throw new InvalidOperationException("Unity Relay did not return a room code.");

            _currentRoomCode = joinCode;
            if (roomCodeText) roomCodeText.text = joinCode;
            GUIUtility.systemCopyBuffer = joinCode;
            if (copyCodeButton) copyCodeButton.interactable = true;
            Debug.Log($"[ConnectionUI] Room created. Code {joinCode} copied to clipboard.");

            SetStatus("");
            panel.SetActive(false);
            lobbyPanel.SetActive(true);
        }
        catch (Exception ex)
        {
            Debug.LogError("[ConnectionUI] Relay host failed: " + ex.Message);
            ShowError(GetFriendlyError(ex, true));
        }
        finally
        {
            _hosting = false;
            SetBusy(false, "");
        }
    }

    public void StartClient()
    {
        if (GameState.Instance) GameState.Instance.ChangeName(nameInput ? nameInput.text.Trim() : "");
        NetworkManager.Singleton.StartClient();
        panel.SetActive(false);
        lobbyPanel.SetActive(true);
    }

    public async void StartClientViaRelay()
    {
        if (_joining || _hosting) return;
        var code = (joinCodeField ? joinCodeField.text : "").Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(code) || !Regex.IsMatch(code, "^[A-Z0-9]{6,8}$"))
        {
            Debug.LogWarning($"[ConnectionUI] Join Code invalid: '{code}'.");
            ShowError("Enter the 6-character room code shown on the host's screen.");
            return;
        }

        try
        {
            _joining = true;
            SetBusy(true, "Joining room...");

            if (GameState.Instance) GameState.Instance.ChangeName(nameInput ? nameInput.text.Trim() : "");
            await RelayManager.Instance.JoinRelayAndStartClientAsync(code);
            _currentRoomCode = code;
            if (roomCodeText) roomCodeText.text = code;
            if (copyCodeButton) copyCodeButton.interactable = true;
            Debug.Log("[ConnectionUI] Relay connection confirmed.");
            SetStatus("");
            panel.SetActive(false);
            lobbyPanel.SetActive(true);
        }
        catch (Exception ex)
        {
            Debug.LogError("[ConnectionUI] Relay join failed: " + ex.Message);
            ShowError(GetFriendlyError(ex, false));
        }
        finally
        {
            _joining = false;
            SetBusy(false, "");
        }
    }

    public void CopyRoomCode()
    {
        if (string.IsNullOrWhiteSpace(_currentRoomCode)) return;

        GUIUtility.systemCopyBuffer = _currentRoomCode;
        Debug.Log($"[ConnectionUI] Room code {_currentRoomCode} copied to clipboard.");
    }

    void SetBusy(bool busy, string message)
    {
        if (hostRelayButton) hostRelayButton.interactable = !busy;
        if (joinRelayButton) joinRelayButton.interactable = !busy;
        if (joinCodeField) joinCodeField.interactable = !busy;
        if (nameInput) nameInput.interactable = !busy;

        if (busy) ShowError("");
        SetStatus(message);
    }

    void SetStatus(string message)
    {
        if (!connectionStatusText) return;
        connectionStatusText.text = message;
        connectionStatusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    void ShowError(string message)
    {
        if (!connectionErrorText) return;
        connectionErrorText.text = message;
        connectionErrorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    static string GetFriendlyError(Exception exception, bool creatingRoom)
    {
        string message = exception.Message.ToLowerInvariant();

        if (exception is TimeoutException || message.Contains("timed out"))
            return "The connection timed out. Check your internet connection and try again.";
        if (message.Contains("join code") || message.Contains("not found") || message.Contains("invalid"))
            return "That room code is invalid or has expired. Ask the host for the latest code.";
        if (message.Contains("allocation") || message.Contains("relay"))
            return creatingRoom
                ? "The room could not be created. Check your internet connection and try again."
                : "The room could not be reached. Check the code and try again.";

        return creatingRoom
            ? "Could not create the room. Please try again."
            : "Could not join the room. Please check the code and try again.";
    }

    TMP_Text CreateRuntimeText(string objectName, Transform parent, Vector2 position, Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(760, 42);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = roomCodeText && roomCodeText.font ? roomCodeText.font : TMP_Settings.defaultFontAsset;
        text.fontSize = 26;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    UnityEngine.UI.Button CreateCopyCodeButton()
    {
        if (!lobbyPanel) return null;

        GameObject buttonObject = new GameObject("CopyCodeButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
        buttonObject.transform.SetParent(lobbyPanel.transform, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(40, -105);
        rect.sizeDelta = new Vector2(190, 48);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

        UnityEngine.UI.Button button = buttonObject.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.font = roomCodeText && roomCodeText.font ? roomCodeText.font : TMP_Settings.defaultFontAsset;
        label.text = "Copy Code";
        label.fontSize = 24;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return button;
    }

    static void SetButtonLabel(UnityEngine.UI.Button button, string label)
    {
        if (!button) return;
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text) text.text = label;
    }

    public void ChangeName()
    {
        if (GameState.Instance != null)
            GameState.Instance.ChangeName(nameInput.text);
        if (SingleSceneSessionManager.Instance != null)
        {
            var mgr = SingleSceneSessionManager.Instance;
            var cid = NetworkManager.Singleton.LocalClientId;
            var nm = nameInput ? nameInput.text : "";
            if (NetworkManager.Singleton.IsServer) mgr.SetLobbyName_Server(cid, nm);
            else mgr.ReportPlayerNameServerRpc(cid, nm);
        }
    }

}
