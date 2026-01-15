using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Auto-setup script that creates UI prefabs at runtime if they're not assigned.
/// Ensures VRUIManager has all required prefabs for testing.
/// </summary>
public class VRUISetup : MonoBehaviour
{
    [Header("Auto-Setup")]
    [Tooltip("Automatically create missing UI prefabs at runtime")]
    [SerializeField] private bool autoCreatePrefabs = true;
    
    [Tooltip("Automatically get/create download modal on start (DEPRECATED: LoadingScreen handles downloads automatically)")]
    [SerializeField] private bool autoSetupDownloadModal = false; // Disabled by default - LoadingScreen handles downloads

    private void Start()
    {
        // Ensure VRUIManager exists (it will create itself as singleton)
        if (VRUIManager.Instance == null)
        {
            GameObject uiManagerObj = new GameObject("VRUIManager");
            uiManagerObj.AddComponent<VRUIManager>();
            Debug.Log("[VRUISetup]: Created VRUIManager");
        }

        // Auto-create missing prefabs if enabled
        if (autoCreatePrefabs && VRUIManager.Instance != null)
        {
            CreateMissingPrefabs();
        }

        // Auto-setup download modal if enabled (DEPRECATED: LoadingScreen handles downloads automatically)
        // LoadingScreen automatically subscribes to CDNDownloadManager events when instantiated
        if (autoSetupDownloadModal && VRUIManager.Instance != null)
        {
            #pragma warning disable CS0618 // Type or member is obsolete
            VRUIManager.Instance.GetDownloadModal();
            #pragma warning restore CS0618
            Debug.Log("[VRUISetup]: Initialized VRDownloadModal (DEPRECATED - use LoadingScreen instead)");
        }
    }

    /// <summary>
    /// Creates missing UI prefabs at runtime by checking VRUIManager's prefab fields.
    /// Uses reflection to access private SerializeField fields.
    /// </summary>
    private void CreateMissingPrefabs()
    {
        var managerType = typeof(VRUIManager);
        var instance = VRUIManager.Instance;

        // Check and create VRModal prefab
        var modalPrefabField = managerType.GetField("modalPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (modalPrefabField != null && modalPrefabField.GetValue(instance) == null)
        {
            GameObject modalPrefab = CreateVRModalPrefab();
            modalPrefabField.SetValue(instance, modalPrefab.GetComponent<VRModal>());
            Debug.Log("[VRUISetup]: Created VRModal prefab");
        }

        // Check and create LoadingScreen prefab
        var loadingScreenPrefabField = managerType.GetField("loadingScreenPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (loadingScreenPrefabField != null && loadingScreenPrefabField.GetValue(instance) == null)
        {
            GameObject loadingScreenPrefab = CreateLoadingScreenPrefab();
            loadingScreenPrefabField.SetValue(instance, loadingScreenPrefab.GetComponent<LoadingScreen>());
            Debug.Log("[VRUISetup]: Created LoadingScreen prefab");
        }

        // Check and create DeDijkInstructionDisplay prefab
        var instructionDisplayPrefabField = managerType.GetField("instructionDisplayPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (instructionDisplayPrefabField != null && instructionDisplayPrefabField.GetValue(instance) == null)
        {
            GameObject instructionPrefab = CreateInstructionDisplayPrefab();
            // instructionDisplayPrefabField.SetValue(instance, instructionPrefab.GetComponent<DeDijkInstructionDisplay>());
            Debug.Log("[VRUISetup]: Created DeDijkInstructionDisplay prefab");
        }

        // Check and create VRDownloadModal prefab
        var downloadModalPrefabField = managerType.GetField("downloadModalPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (downloadModalPrefabField != null && downloadModalPrefabField.GetValue(instance) == null)
        {
            GameObject downloadModalPrefab = CreateVRDownloadModalPrefab();
            downloadModalPrefabField.SetValue(instance, downloadModalPrefab.GetComponent<VRDownloadModal>());
            Debug.Log("[VRUISetup]: Created VRDownloadModal prefab");
        }
    }

    /// <summary>
    /// Creates a VRModal prefab GameObject with all required components.
    /// </summary>
    private GameObject CreateVRModalPrefab()
    {
        GameObject modal = new GameObject("VRModal");
        modal.SetActive(false); // Prefabs should start inactive

        // Add VRModal component
        VRModal modalComponent = modal.AddComponent<VRModal>();

        // Create Canvas
        Canvas canvas = modal.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        CanvasScaler scaler = modal.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        modal.AddComponent<GraphicRaycaster>();

        // Create CanvasGroup for fading
        CanvasGroup canvasGroup = modal.AddComponent<CanvasGroup>();

        // Create modal panel (background)
        GameObject panel = new GameObject("ModalPanel");
        panel.transform.SetParent(modal.transform, false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(1f, 1f, 1f, 0.9f); // De Dijk White with transparency

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(1000, 500);
        panelRect.localPosition = Vector3.zero;

        // Create message text
        GameObject messageTextObj = new GameObject("MessageText");
        messageTextObj.transform.SetParent(panel.transform, false);
        // TextMeshProUGUI messageText = messageTextObj.AddComponent<TextMeshProUGUI>();
        // messageText.text = "Message";
        // messageText.fontSize = 60;
        // messageText.alignment = TextAlignmentOptions.Center;
        // messageText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;

        RectTransform textRect = messageTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = new Vector2(50, 50);
        textRect.offsetMax = new Vector2(-50, -50);

        // Use reflection to set private fields
        var modalType = typeof(VRModal);
        var panelField = modalType.GetField("modalPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var textField = modalType.GetField("messageText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var canvasField = modalType.GetField("modalCanvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (panelField != null) panelField.SetValue(modalComponent, panel);
        // if (textField != null) textField.SetValue(modalComponent, messageText);
        if (canvasField != null) canvasField.SetValue(modalComponent, canvas);

        return modal;
    }

    /// <summary>
    /// Creates a LoadingScreen prefab GameObject with all required components.
    /// </summary>
    private GameObject CreateLoadingScreenPrefab()
    {
        GameObject loadingScreen = new GameObject("LoadingScreen");
        loadingScreen.SetActive(false);

        LoadingScreen loadingComponent = loadingScreen.AddComponent<LoadingScreen>();

        // Create Canvas
        Canvas canvas = loadingScreen.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        loadingScreen.AddComponent<CanvasScaler>();
        loadingScreen.AddComponent<GraphicRaycaster>();

        // Create loading panel
        GameObject panel = new GameObject("LoadingPanel");
        panel.transform.SetParent(loadingScreen.transform, false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.8f); // Dark background

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(1200, 800);
        panelRect.localPosition = Vector3.zero;

        // Create loading text
        GameObject loadingTextObj = new GameObject("LoadingText");
        loadingTextObj.transform.SetParent(panel.transform, false);
        // TextMeshProUGUI loadingText = loadingTextObj.AddComponent<TextMeshProUGUI>();
        // loadingText.text = "Laden...";
        // loadingText.fontSize = 72;
        // loadingText.alignment = TextAlignmentOptions.Center;
        // loadingText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;

        RectTransform loadingTextRect = loadingTextObj.GetComponent<RectTransform>();
        loadingTextRect.anchorMin = new Vector2(0.5f, 0.7f);
        loadingTextRect.anchorMax = new Vector2(0.5f, 0.7f);
        loadingTextRect.sizeDelta = new Vector2(1000, 100);
        loadingTextRect.localPosition = Vector3.zero;

        // Create progress bar container
        GameObject progressContainer = new GameObject("ProgressBarContainer");
        progressContainer.transform.SetParent(panel.transform, false);
        Image containerImage = progressContainer.AddComponent<Image>();
        containerImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

        RectTransform containerRect = progressContainer.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(800, 40);
        containerRect.localPosition = Vector3.zero;

        // Create progress bar fill
        GameObject progressFill = new GameObject("ProgressBarFill");
        progressFill.transform.SetParent(progressContainer.transform, false);
        Image fillImage = progressFill.AddComponent<Image>();
        // fillImage.color = DeDijkUIDesignSystem.DeDijkOrange;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;

        RectTransform fillRect = progressFill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // Create tagline text
        GameObject taglineTextObj = new GameObject("TaglineText");
        taglineTextObj.transform.SetParent(panel.transform, false);
        // TextMeshProUGUI taglineText = taglineTextObj.AddComponent<TextMeshProUGUI>();
        // taglineText.text = DeDijkUIDesignSystem.SchoolTagline;
        // taglineText.fontSize = 36;
        // taglineText.alignment = TextAlignmentOptions.Center;
        // taglineText.color = DeDijkUIDesignSystem.DeDijkOrange;

        RectTransform taglineRect = taglineTextObj.GetComponent<RectTransform>();
        taglineRect.anchorMin = new Vector2(0.5f, 0.3f);
        taglineRect.anchorMax = new Vector2(0.5f, 0.3f);
        taglineRect.sizeDelta = new Vector2(1000, 60);
        taglineRect.localPosition = Vector3.zero;

        // Create spinner (simple rotating object)
        GameObject spinner = new GameObject("Spinner");
        spinner.transform.SetParent(panel.transform, false);
        Image spinnerImage = spinner.AddComponent<Image>();
        // spinnerImage.color = DeDijkUIDesignSystem.DeDijkYellow;
        spinnerImage.type = Image.Type.Filled;
        spinnerImage.fillMethod = Image.FillMethod.Radial360;

        RectTransform spinnerRect = spinner.GetComponent<RectTransform>();
        spinnerRect.anchorMin = new Vector2(0.5f, 0.6f);
        spinnerRect.anchorMax = new Vector2(0.5f, 0.6f);
        spinnerRect.sizeDelta = new Vector2(60, 60);
        spinnerRect.localPosition = Vector3.zero;

        // Use reflection to set private fields
        var loadingType = typeof(LoadingScreen);
        var panelField = loadingType.GetField("loadingPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var textField = loadingType.GetField("loadingText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fillField = loadingType.GetField("progressBarFill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var containerField = loadingType.GetField("progressBarContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var spinnerField = loadingType.GetField("spinner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var taglineField = loadingType.GetField("taglineText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (panelField != null) panelField.SetValue(loadingComponent, panel);
        // if (textField != null) textField.SetValue(loadingComponent, loadingText);
        if (fillField != null) fillField.SetValue(loadingComponent, fillImage);
        if (containerField != null) containerField.SetValue(loadingComponent, progressContainer);
        if (spinnerField != null) spinnerField.SetValue(loadingComponent, spinner);
        // if (taglineField != null) taglineField.SetValue(loadingComponent, taglineText);

        return loadingScreen;
    }

    /// <summary>
    /// Creates a DeDijkInstructionDisplay prefab GameObject with all required components.
    /// </summary>
    private GameObject CreateInstructionDisplayPrefab()
    {
        GameObject instruction = new GameObject("DeDijkInstructionDisplay");
        instruction.SetActive(false);

        // DeDijkInstructionDisplay instructionComponent = instruction.AddComponent<DeDijkInstructionDisplay>();

        // Create Canvas
        Canvas canvas = instruction.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        instruction.AddComponent<CanvasScaler>();
        instruction.AddComponent<GraphicRaycaster>();
        CanvasGroup canvasGroup = instruction.AddComponent<CanvasGroup>();

        // Create background
        GameObject background = new GameObject("InstructionBackground");
        background.transform.SetParent(instruction.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(1f, 1f, 1f, 0.9f); // De Dijk White

        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.sizeDelta = new Vector2(1200, 600);
        bgRect.localPosition = Vector3.zero;

        // Create instruction text
        GameObject instructionTextObj = new GameObject("InstructionText");
        instructionTextObj.transform.SetParent(background.transform, false);
        // TextMeshProUGUI instructionText = instructionTextObj.AddComponent<TextMeshProUGUI>();
        // instructionText.text = "Instruction";
        // instructionText.fontSize = 60;
        // instructionText.alignment = TextAlignmentOptions.Center;
        // instructionText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;

        RectTransform instructionRect = instructionTextObj.GetComponent<RectTransform>();
        instructionRect.anchorMin = new Vector2(0.5f, 0.6f);
        instructionRect.anchorMax = new Vector2(0.5f, 0.6f);
        instructionRect.sizeDelta = new Vector2(1100, 200);
        instructionRect.localPosition = Vector3.zero;

        // Create tagline text
        GameObject taglineTextObj = new GameObject("TaglineText");
        taglineTextObj.transform.SetParent(background.transform, false);
        // TextMeshProUGUI taglineText = taglineTextObj.AddComponent<TextMeshProUGUI>();
        // taglineText.text = DeDijkUIDesignSystem.SchoolTagline;
        // taglineText.fontSize = 36;
        // taglineText.alignment = TextAlignmentOptions.Center;
        // taglineText.color = DeDijkUIDesignSystem.DeDijkOrange;

        RectTransform taglineRect = taglineTextObj.GetComponent<RectTransform>();
        taglineRect.anchorMin = new Vector2(0.5f, 0.3f);
        taglineRect.anchorMax = new Vector2(0.5f, 0.3f);
        taglineRect.sizeDelta = new Vector2(1100, 80);
        taglineRect.localPosition = Vector3.zero;

        // Use reflection to set private fields
        // var instructionType = typeof(DeDijkInstructionDisplay);
        // var textField = instructionType.GetField("instructionText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // var taglineField = instructionType.GetField("taglineText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // var bgField = instructionType.GetField("instructionBackground", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // var canvasField = instructionType.GetField("instructionCanvas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // if (textField != null) textField.SetValue(instructionComponent, instructionText);
        // if (taglineField != null) taglineField.SetValue(instructionComponent, taglineText);
        // if (bgField != null) bgField.SetValue(instructionComponent, bgImage);
        // if (canvasField != null) canvasField.SetValue(instructionComponent, canvas);

        return instruction;
    }

    /// <summary>
    /// Creates a VRDownloadModal prefab GameObject (simplified version).
    /// </summary>
    private GameObject CreateVRDownloadModalPrefab()
    {
        // For now, create a simple version - VRDownloadModal is more complex
        // This will allow basic functionality, but full setup may need manual configuration
        GameObject downloadModal = new GameObject("VRDownloadModal");
        downloadModal.SetActive(false);
        
        VRDownloadModal modalComponent = downloadModal.AddComponent<VRDownloadModal>();
        
        // Basic setup - VRDownloadModal will handle its own initialization
        // The component has fallback logic to create UI if not assigned
        
        return downloadModal;
    }
}

