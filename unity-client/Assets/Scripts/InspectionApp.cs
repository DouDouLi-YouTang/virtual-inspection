using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InspectionApp : MonoBehaviour
{
    private Camera inspectionCamera;
    private CharacterController characterController;
    private Transform viewRoot;
    private Transform pendingMarker;
    private Vector3 pendingHitPoint;
    private CanvasGroup formGroup;
    private CanvasGroup markerInfoGroup;
    private InputField titleInput;
    private InputField descriptionInput;
    private int priorityIndex = 1;
    private Button[] priorityButtons;
    private Text positionText;
    private Text statusText;
    private Text markerInfoTitle;
    private Text markerInfoMeta;
    private Text markerInfoDescription;
    private Image crosshair;
    private Button submitButton;
    private Button cancelButton;
    private Text submitLabel;
    private GameObject savedMarkersRoot;
    private float cameraPitch;
    private bool submitting;
    private bool initialized;
    private bool cursorLocked;

    private static readonly Color Accent = new Color(0.86f, 0.35f, 0.16f);
    private static readonly Color Ink = new Color(0.12f, 0.16f, 0.15f);
    private static readonly Color Paper = new Color(0.96f, 0.95f, 0.91f);
    private static readonly Color UnselectedPriority = new Color(0.87f, 0.88f, 0.83f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAppExists()
    {
        if (FindObjectOfType<InspectionApp>() != null)
        {
            return;
        }
        GameObject app = new GameObject("InspectionApp");
        app.AddComponent<InspectionApp>();
    }

    private void Start()
    {
        BuildScene();
        BuildInterface();
        StartCoroutine(LoadSavedMarkers());
        SetFormVisible(false);
        SetStatus("点击机柜、管线或地面放置问题标记", false);
        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        HandleMovement();
        HandleLook();
        UpdateAimFeedback();
        HandlePlacementClick();
    }

    private void HandleMovement()
    {
        if (IsModalOpen
            || titleInput.isFocused
            || descriptionInput.isFocused)
        {
            return;
        }

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 direction = transform.right * horizontal + transform.forward * vertical;
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        float speed = Input.GetKey(KeyCode.LeftShift) ? 8f : 4f;
        characterController.Move(direction * speed * Time.deltaTime);
        if (characterController.isGrounded)
        {
            characterController.Move(Vector3.down * 0.05f);
        }
    }

    private void HandleLook()
    {
        if (IsModalOpen)
        {
            ReleaseCursor();
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            cursorLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Input.GetMouseButtonUp(1))
        {
            ReleaseCursor();
            return;
        }
        if (!Input.GetMouseButton(1))
        {
            return;
        }

        ApplyLook(
            Input.GetAxisRaw("Mouse X") * 3f,
            -Input.GetAxisRaw("Mouse Y") * 3f);
    }

    private void ApplyLook(float yaw, float pitch)
    {
        Transform root = viewRoot != null ? viewRoot : transform;
        root.Rotate(0f, yaw, 0f, Space.World);
        cameraPitch = Mathf.Clamp(cameraPitch + pitch, -70f, 70f);
        inspectionCamera.transform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void HandlePlacementClick()
    {
        if (cursorLocked
            || IsModalOpen
            || submitting
            || pendingMarker != null)
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0)
            || EventSystem.current == null
            || EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Ray ray = inspectionCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            SetStatus("这里没有可放置标记的表面，请点击设备或地面。", true);
            return;
        }

        SavedIssueMarker savedMarker =
            hit.collider.GetComponent<SavedIssueMarker>();
        if (savedMarker != null)
        {
            if (savedMarker.Issue != null)
            {
                ShowMarkerInfo(savedMarker.Issue);
            }
            return;
        }

        pendingHitPoint = hit.point;
        CreatePendingMarker(hit.point, hit.normal);
        OpenForm();
    }

    private void UpdateAimFeedback()
    {
        if (IsModalOpen
            || EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject())
        {
            crosshair.color = new Color(1f, 1f, 1f, 0.35f);
            return;
        }

        Vector2 screenPoint = cursorLocked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : (Vector2)Input.mousePosition;
        Ray ray = inspectionCamera.ScreenPointToRay(screenPoint);
        bool hasTarget = Physics.Raycast(ray, out RaycastHit hit, 100f);
        crosshair.color = hasTarget
            ? new Color(0.96f, 0.45f, 0.23f, 0.95f)
            : new Color(1f, 1f, 1f, 0.35f);
    }

    private IEnumerator LoadSavedMarkers()
    {
        yield return new IssueApi().FetchIssues(
            (responseCode, error, issues) =>
            {
                if (responseCode != 200 || issues == null)
                {
                    SetStatus(
                        string.IsNullOrWhiteSpace(error)
                            ? "已保存标记加载失败，但不影响现场上报。"
                            : $"已保存标记加载失败：{error}",
                        true);
                    return;
                }
                foreach (IssueRecord issue in issues)
                {
                    CreateSavedMarker(issue);
                }
                if (issues.Length > 0)
                {
                    SetStatus(
                        $"已恢复 {issues.Length} 个已保存标记，可点击标记查看信息",
                        false);
                }
            });
    }

    private void CreateSavedMarker(IssueRecord issue)
    {
        if (issue?.position == null)
        {
            return;
        }

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "SavedIssueMarker";
        marker.transform.position = new Vector3(
            issue.position.x,
            issue.position.y,
            issue.position.z) + Vector3.up * 0.11f;
        marker.transform.localScale = Vector3.one * 0.25f;
        marker.transform.SetParent(savedMarkersRoot.transform, true);
        ApplyMarkerMaterial(marker);
        marker.AddComponent<SavedIssueMarker>().Issue = issue;
    }

    private void CreatePendingMarker(Vector3 point, Vector3 normal)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "PendingIssueMarker";
        Vector3 offsetDirection = normal.sqrMagnitude > 0.001f
            ? normal.normalized
            : Vector3.up;
        marker.transform.position = point + offsetDirection * 0.11f;
        marker.transform.localScale = Vector3.one * 0.22f;
        marker.transform.SetParent(savedMarkersRoot.transform, true);
        Collider markerCollider = marker.GetComponent<Collider>();
        markerCollider.isTrigger = true;

        ApplyMarkerMaterial(marker);
        marker.AddComponent<SavedIssueMarker>().Issue = null;
        pendingMarker = marker.transform;
    }

    private static void ApplyMarkerMaterial(GameObject marker)
    {
        Renderer renderer = marker.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Standard"));
        material.color = Accent;
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Accent * 0.45f);
        renderer.material = material;
    }

    private void OpenForm()
    {
        ReleaseCursor();
        titleInput.text = string.Empty;
        descriptionInput.text = string.Empty;
        SelectPriority(1);
        SetFormInteractable(true);
        positionText.text =
            $"X {pendingHitPoint.x:F2}   Y {pendingHitPoint.y:F2}   Z {pendingHitPoint.z:F2}";
        SetFormVisible(true);
        titleInput.Select();
        titleInput.ActivateInputField();
        SetStatus("标记已放置，请填写问题后提交", false);
    }

    private void CancelPendingIssue()
    {
        if (submitting)
        {
            return;
        }
        if (pendingMarker != null)
        {
            Destroy(pendingMarker.gameObject);
            pendingMarker = null;
        }
        pendingHitPoint = Vector3.zero;
        titleInput.text = string.Empty;
        descriptionInput.text = string.Empty;
        SetFormVisible(false);
        ReleaseCursor();
        EventSystem.current?.SetSelectedGameObject(null);
        SetStatus("已取消，未保存的问题标记已移除", false);
    }

    private void SubmitIssue()
    {
        if (submitting || pendingMarker == null)
        {
            return;
        }

        string title = titleInput.text.Trim();
        if (title.Length == 0)
        {
            SetStatus("标题为必填项。", true);
            titleInput.Select();
            titleInput.ActivateInputField();
            return;
        }

        submitting = true;
        SetFormInteractable(false);
        submitLabel.text = "提交中…";
        SetStatus("正在提交到巡检后端…", false);

        IssueCreatePayload payload = IssueApi.BuildPayload(
            title,
            descriptionInput.text,
            priorityIndex,
            pendingHitPoint);
        StartCoroutine(new IssueApi().CreateIssue(payload, OnCreateCompleted));
    }

    private void OnCreateCompleted(
        long responseCode,
        string error,
        IssueRecord savedIssue)
    {
        submitting = false;
        SetFormInteractable(true);
        submitLabel.text = "提交问题";

        if (responseCode == 201)
        {
            if (pendingMarker != null)
            {
                pendingMarker.name = "SavedIssueMarker";
                pendingMarker.GetComponent<SavedIssueMarker>().Issue =
                    savedIssue;
                pendingMarker = null;
            }
            pendingHitPoint = Vector3.zero;
            SetFormVisible(false);
            ReleaseCursor();
            EventSystem.current?.SetSelectedGameObject(null);
            SetStatus("问题已提交成功，可在 React 管理端查看。", false);
            return;
        }

        SetStatus(
            string.IsNullOrWhiteSpace(error) ? "提交失败，请稍后重试。" : error,
            true);
    }

    private void SetFormVisible(bool visible)
    {
        if (!visible)
        {
            ReleaseCursor();
        }
        formGroup.alpha = visible ? 1f : 0f;
        formGroup.interactable = visible;
        formGroup.blocksRaycasts = visible;
    }

    private bool IsModalOpen =>
        formGroup.interactable || markerInfoGroup.interactable;

    private void SetFormInteractable(bool value)
    {
        titleInput.interactable = value;
        descriptionInput.interactable = value;
        foreach (Button button in priorityButtons)
        {
            button.interactable = value;
        }
        submitButton.interactable = value;
        cancelButton.interactable = value;
    }

    private void SetStatus(string message, bool isError)
    {
        statusText.text = message;
        statusText.color = isError ? new Color(0.96f, 0.54f, 0.42f) : Paper;
    }

    private void ReleaseCursor()
    {
        if (cursorLocked || Cursor.lockState != CursorLockMode.None)
        {
            cursorLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void BuildScene()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.63f, 0.66f, 0.61f);
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = new Color(0.72f, 0.76f, 0.71f);

        Camera existingCamera = Camera.main;
        if (existingCamera == null)
        {
            GameObject cameraObject = new GameObject("InspectionCamera");
            existingCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
        }
        inspectionCamera = existingCamera;
        inspectionCamera.fieldOfView = 62f;
        inspectionCamera.nearClipPlane = 0.05f;

        GameObject player = new GameObject("InspectionPlayer");
        player.transform.position = new Vector3(0f, 0.85f, -7.5f);
        viewRoot = player.transform;
        characterController = player.AddComponent<CharacterController>();
        characterController.height = 1.7f;
        characterController.radius = 0.3f;
        characterController.center = Vector3.zero;
        inspectionCamera.transform.SetParent(player.transform, false);
        inspectionCamera.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        inspectionCamera.transform.localRotation = Quaternion.identity;
        transform.SetParent(player.transform, false);
        transform.localPosition = Vector3.zero;

        AudioListener existingListener = inspectionCamera.GetComponent<AudioListener>();
        if (existingListener == null)
        {
            inspectionCamera.gameObject.AddComponent<AudioListener>();
        }

        GameObject sun = new GameObject("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.91f, 0.79f);
        sun.transform.rotation = Quaternion.Euler(48f, -34f, 0f);

        savedMarkersRoot = new GameObject("Saved Issue Markers");

        CreateBox("Ground", new Vector3(0f, -0.15f, 0f), new Vector3(20f, 0.3f, 24f), new Color(0.39f, 0.43f, 0.4f));
        CreateBox("Back Wall", new Vector3(0f, 2.5f, -11.5f), new Vector3(20f, 5f, 0.3f), new Color(0.62f, 0.63f, 0.58f));
        CreateBox("East Wall", new Vector3(8.5f, 2.5f, 2f), new Vector3(0.3f, 5f, 20f), new Color(0.62f, 0.63f, 0.58f));
        CreateBox("West Wall", new Vector3(-8.5f, 2.5f, 2f), new Vector3(0.3f, 5f, 20f), new Color(0.68f, 0.68f, 0.62f));
        CreateBox("North Wall", new Vector3(0f, 2.5f, 9f), new Vector3(17f, 5f, 0.3f), new Color(0.68f, 0.68f, 0.62f));
        CreateBox("Cabinet A", new Vector3(-3.6f, 1.3f, 2.5f), new Vector3(1.6f, 2.6f, 1.1f), new Color(0.16f, 0.25f, 0.25f));
        CreateBox("Cabinet B", new Vector3(3.2f, 1.15f, 1.4f), new Vector3(1.5f, 2.3f, 1.05f), new Color(0.2f, 0.3f, 0.32f));
        CreatePipe();
        CreateCableTray();
        CreateLabels();
    }

    private static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Color color)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
        SetMaterial(box, color);
        return box;
    }


    private static void CreatePipe()
    {
        GameObject pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pipe.name = "Cooling Pipe";
        pipe.transform.position = new Vector3(6.1f, 2.35f, 4.3f);
        pipe.transform.localScale = new Vector3(0.35f, 3.8f, 0.35f);
        pipe.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        SetMaterial(pipe, new Color(0.64f, 0.33f, 0.16f));
    }

    private static void CreateCableTray()
    {
        CreateBox(
            "Cable Tray",
            new Vector3(0.1f, 4.15f, 5.7f),
            new Vector3(12f, 0.14f, 0.7f),
            new Color(0.3f, 0.34f, 0.32f));
    }

    private static void SetMaterial(GameObject target, Color color)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.color = color;
        material.SetFloat("_Glossiness", 0.22f);
        target.GetComponent<Renderer>().material = material;
    }

    private void CreateLabels()
    {
        CreateWorldLabel("1 号机柜", new Vector3(-3.6f, 2.8f, 2.5f), new Vector3(-90f, 0f, 0f));
        CreateWorldLabel("2 号机柜", new Vector3(3.2f, 2.6f, 1.4f), new Vector3(-90f, 0f, 0f));
        CreateWorldLabel("冷却管线", new Vector3(6.1f, 4.4f, 4.3f), new Vector3(-90f, 0f, 0f));
    }

    private static void CreateWorldLabel(string label, Vector3 position, Vector3 rotation)
    {
        GameObject canvasObject = new GameObject(label + " Label");
        canvasObject.transform.position = position;
        canvasObject.transform.rotation = Quaternion.Euler(rotation);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(240f, 50f);
        rect.localScale = Vector3.one * 0.006f;

        Text text = CreateText(
            canvasObject.transform,
            label,
            26,
            Ink,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            rect.sizeDelta,
            new Vector2(0.5f, 0.5f));
        text.fontStyle = FontStyle.Bold;
        text.rectTransform.sizeDelta = rect.sizeDelta;
        text.rectTransform.anchoredPosition = Vector2.zero;
    }

    private void BuildInterface()
    {
        Debug.Log($"UI font: {(RuntimeFonts.UiFont == null ? "fallback" : RuntimeFonts.UiFont.name)}");

        GameObject canvasObject = new GameObject("Inspection UI");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        CreateHud(canvasObject.transform);
        CreateForm(canvasObject.transform);
        CreateMarkerInfo(canvasObject.transform);
        CreateCrosshair(canvasObject.transform);
    }

    private void CreateMarkerInfo(Transform canvas)
    {
        GameObject overlay = new GameObject(
            "Marker Info Overlay",
            typeof(RectTransform),
            typeof(CanvasGroup));
        overlay.transform.SetParent(canvas, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        markerInfoGroup = overlay.GetComponent<CanvasGroup>();

        GameObject panel = CreatePanel(
            overlay.transform,
            "Marker Info",
            new Color(0.96f, 0.95f, 0.91f, 0.97f));
        SetRect(
            panel.GetComponent<RectTransform>(),
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(30f, 70f),
            new Vector2(430f, 390f));

        markerInfoTitle = CreateText(
            panel.transform,
            string.Empty,
            25,
            Ink,
            TextAnchor.UpperLeft,
            new Vector2(24f, -24f),
            new Vector2(350f, 72f),
            new Vector2(0f, 1f));
        markerInfoMeta = CreateText(
            panel.transform,
            string.Empty,
            14,
            Accent,
            TextAnchor.UpperLeft,
            new Vector2(24f, -105f),
            new Vector2(350f, 28f),
            new Vector2(0f, 1f));
        markerInfoDescription = CreateText(
            panel.transform,
            string.Empty,
            16,
            new Color(0.29f, 0.34f, 0.31f),
            TextAnchor.UpperLeft,
            new Vector2(24f, -143f),
            new Vector2(350f, 120f),
            new Vector2(0f, 1f));
        markerInfoDescription.horizontalOverflow =
            HorizontalWrapMode.Wrap;
        markerInfoDescription.verticalOverflow =
            VerticalWrapMode.Truncate;

        Button close = CreateButton(
            panel.transform,
            "关闭",
            new Color(0.83f, 0.85f, 0.8f),
            Ink,
            new Vector2(24f, -290f),
            new Vector2(150f, 44f));
        close.onClick.AddListener(CloseMarkerInfo);
        SetMarkerInfoVisible(false);
    }

    private void ShowMarkerInfo(IssueRecord issue)
    {
        markerInfoTitle.text = issue.title;
        markerInfoMeta.text =
            $"{StatusLabel(issue.status)} · {PriorityLabel(issue.priority)}优先级";
        markerInfoDescription.text =
            string.IsNullOrWhiteSpace(issue.description)
                ? "未填写问题描述"
                : issue.description;
        SetMarkerInfoVisible(true);
        ReleaseCursor();
        SetStatus("已打开标记详情，点击关闭返回现场", false);
    }

    private void CloseMarkerInfo()
    {
        SetMarkerInfoVisible(false);
        EventSystem.current?.SetSelectedGameObject(null);
        SetStatus("已返回现场，可继续观察或上报问题", false);
    }

    private void SetMarkerInfoVisible(bool visible)
    {
        markerInfoGroup.alpha = visible ? 1f : 0f;
        markerInfoGroup.interactable = visible;
        markerInfoGroup.blocksRaycasts = visible;
    }

    private static string StatusLabel(string status)
    {
        switch (status)
        {
            case "in_progress":
                return "处理中";
            case "resolved":
                return "已解决";
            default:
                return "待处理";
        }
    }

    private static string PriorityLabel(string priority)
    {
        switch (priority)
        {
            case "high":
                return "高";
            case "low":
                return "低";
            default:
                return "中";
        }
    }

    private void CreateCrosshair(Transform canvas)
    {
        GameObject crosshairObject = new GameObject(
            "Crosshair",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        crosshairObject.transform.SetParent(canvas, false);
        RectTransform rect = crosshairObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(18f, 18f);
        rect.anchoredPosition = Vector2.zero;
        crosshair = crosshairObject.GetComponent<Image>();
        crosshair.sprite = Resources.GetBuiltinResource<Sprite>(
            "UI/Skin/Knob.psd");
        crosshair.color = new Color(1f, 1f, 1f, 0.35f);
        crosshair.raycastTarget = false;
    }

    private void CreateHud(Transform canvas)
    {
        GameObject header = CreatePanel(canvas, "Header", new Color(0.1f, 0.14f, 0.13f, 0.94f));
        SetRect(header.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -96f), new Vector2(0f, 0f));
        CreateText(header.transform, "虚拟巡检现场", 31, Paper, TextAnchor.MiddleLeft, new Vector2(28f, -16f), new Vector2(450f, 42f), new Vector2(0f, 1f));
        Text controls = CreateText(header.transform, "WASD 移动   ·   按住右键转动视角   ·   左键点击设备或地面放置标记   ·   Shift 加速", 16, new Color(0.74f, 0.8f, 0.75f), TextAnchor.MiddleLeft, new Vector2(28f, -54f), new Vector2(900f, 30f), new Vector2(0f, 1f));
        controls.horizontalOverflow = HorizontalWrapMode.Overflow;

        GameObject status = CreatePanel(canvas, "Status Bar", new Color(0.1f, 0.14f, 0.13f, 0.9f));
        SetRect(status.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 48f));
        statusText = CreateText(status.transform, string.Empty, 16, Paper, TextAnchor.MiddleLeft, new Vector2(28f, 0f), new Vector2(1000f, 36f), new Vector2(0f, 0.5f));
        statusText.rectTransform.sizeDelta = new Vector2(1050f, 36f);

        GameObject legend = CreatePanel(canvas, "Legend", new Color(0.96f, 0.95f, 0.91f, 0.91f));
        SetRect(legend.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-278f, -150f), new Vector2(-20f, -108f));
        CreateText(legend.transform, "场景检查点", 17, Ink, TextAnchor.MiddleLeft, new Vector2(18f, -10f), new Vector2(220f, 28f), new Vector2(0f, 1f));
        CreateText(legend.transform, "1 号机柜 / 2 号机柜 / 冷却管线\n点击设备放置问题；点击已保存标记查看", 14, new Color(0.34f, 0.39f, 0.36f), TextAnchor.UpperLeft, new Vector2(18f, -43f), new Vector2(220f, 60f), new Vector2(0f, 1f));
    }

    private void CreateForm(Transform canvas)
    {
        GameObject overlay = new GameObject("Form Overlay", typeof(RectTransform), typeof(CanvasGroup));
        overlay.transform.SetParent(canvas, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        formGroup = overlay.GetComponent<CanvasGroup>();

        GameObject panel = CreatePanel(overlay.transform, "Issue Form", Paper);
        SetRect(panel.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-495f, -252f), new Vector2(-30f, 252f));

        GameObject stripe = CreatePanel(panel.transform, "Accent", Accent);
        SetRect(stripe.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -7f), Vector2.zero);

        CreateText(panel.transform, "上报现场问题", 28, Ink, TextAnchor.MiddleLeft, new Vector2(28f, -29f), new Vector2(320f, 42f), new Vector2(0f, 1f));
        CreateText(panel.transform, "位置取自已放置标记的世界坐标", 14, new Color(0.43f, 0.48f, 0.44f), TextAnchor.MiddleLeft, new Vector2(29f, -68f), new Vector2(330f, 24f), new Vector2(0f, 1f));
        positionText = CreateText(panel.transform, string.Empty, 17, Accent, TextAnchor.MiddleLeft, new Vector2(29f, -96f), new Vector2(390f, 26f), new Vector2(0f, 1f));
        positionText.fontStyle = FontStyle.Bold;

        titleInput = CreateInput(panel.transform, "标题 *", "例如：1 号机柜温度异常", 143f);
        descriptionInput = CreateInput(panel.transform, "问题描述", "描述异常现象（可选）", 224f);
        descriptionInput.lineType = InputField.LineType.MultiLineNewline;

        CreateText(panel.transform, "优先级", 14, new Color(0.43f, 0.48f, 0.44f), TextAnchor.MiddleLeft, new Vector2(29f, -310f), new Vector2(110f, 24f), new Vector2(0f, 1f));
        CreatePrioritySelector(panel.transform, 334f);

        submitButton = CreateButton(panel.transform, "提交问题", Accent, Color.white, new Vector2(29f, -404f), new Vector2(260f, 48f));
        submitLabel = submitButton.GetComponentInChildren<Text>();
        submitButton.onClick.AddListener(SubmitIssue);
        cancelButton = CreateButton(panel.transform, "取消", new Color(0.83f, 0.85f, 0.8f), Ink, new Vector2(301f, -404f), new Vector2(135f, 48f));
        cancelButton.onClick.AddListener(CancelPendingIssue);
    }

    private InputField CreateInput(Transform parent, string label, string placeholder, float y)
    {
        CreateText(parent, label, 14, new Color(0.43f, 0.48f, 0.44f), TextAnchor.MiddleLeft, new Vector2(29f, -y), new Vector2(180f, 24f), new Vector2(0f, 1f));
        GameObject fieldObject = CreatePanel(parent, label + " Input", Color.white);
        SetRect(fieldObject.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(29f, -y - 56f), new Vector2(408f, -y - 27f));
        InputField field = fieldObject.AddComponent<InputField>();
        Text text = CreateText(fieldObject.transform, string.Empty, 16, Ink, TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(380f, 28f), new Vector2(0f, 0.5f));
        text.supportRichText = false;
        Text hint = CreateText(fieldObject.transform, placeholder, 15, new Color(0.58f, 0.62f, 0.58f), TextAnchor.MiddleLeft, new Vector2(12f, 0f), new Vector2(380f, 28f), new Vector2(0f, 0.5f));
        hint.supportRichText = false;
        field.textComponent = text;
        field.placeholder = hint;
        field.targetGraphic = fieldObject.GetComponent<Image>();
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }

    private void CreatePrioritySelector(Transform parent, float y)
    {
        string[] labels = { "低", "中", "高" };
        GameObject selector = new GameObject("Priority Selector", typeof(RectTransform));
        selector.transform.SetParent(parent, false);
        RectTransform selectorRect = selector.GetComponent<RectTransform>();
        selectorRect.anchorMin = new Vector2(0f, 1f);
        selectorRect.anchorMax = new Vector2(0f, 1f);
        selectorRect.pivot = new Vector2(0f, 1f);
        selectorRect.anchoredPosition = new Vector2(0f, -y);
        selectorRect.sizeDelta = new Vector2(380f, 44f);

        priorityButtons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateButton(
                selector.transform,
                labels[i],
                Color.white,
                Ink,
                new Vector2(29f + i * 124f, 0f),
                new Vector2(116f, 44f));
            button.onClick.AddListener(() => SelectPriority(index));
            priorityButtons[i] = button;
        }
        SelectPriority(1);
    }

    private void SelectPriority(int index)
    {
        priorityIndex = index;
        for (int i = 0; i < priorityButtons.Length; i++)
        {
            bool selected = i == index;
            Image background = priorityButtons[i].GetComponent<Image>();
            Text label = priorityButtons[i].GetComponentInChildren<Text>();
            background.color = selected ? Accent : UnselectedPriority;
            label.color = selected ? Color.white : Ink;
        }
    }

    private static Button CreateButton(Transform parent, string label, Color background, Color foreground, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject buttonObject = CreatePanel(parent, label + " Button", background);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        Text text = CreateText(buttonObject.transform, label, 17, foreground, TextAnchor.MiddleCenter, Vector2.zero, size, new Vector2(0.5f, 0.5f));
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private static Text CreateText(
        Transform parent,
        string content,
        int size,
        Color color,
        TextAnchor alignment,
        Vector2 anchoredPosition,
        Vector2 rectSize,
        Vector2 pivot)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.text = content;
        text.font = RuntimeFonts.UiFont;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = pivot;
        rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = rectSize;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}

