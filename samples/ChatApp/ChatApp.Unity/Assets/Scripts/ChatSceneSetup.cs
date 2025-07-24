using UnityEngine;
using UnityEngine.UI;

namespace ChatApp.Unity
{
    /// <summary>
    /// ChatScene UI 自动设置脚本
    /// 为 ChatComponent 创建完整的UI布局
    /// </summary>
    public class ChatSceneSetup : MonoBehaviour
    {
        [Header("UI设置")]
        [SerializeField] private bool autoSetupOnStart = true;
        [SerializeField] private Font defaultFont;

        [Header("颜色配置")]
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        [SerializeField] private Color panelColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        [SerializeField] private Color buttonColor = new Color(0.3f, 0.6f, 0.9f, 1f);
        [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 1f);

        [Header("聊天组件")]
        [SerializeField] private ChatComponent _chatComponent;

        private Canvas mainCanvas;
        private ChatComponent chatComponent;

        private void Start()
        {
            if (autoSetupOnStart)
            {
                SetupChatUI();
            }
        }

        [ContextMenu("设置聊天UI")]
        public void SetupChatUI()
        {
            // 创建或获取主Canvas
            SetupMainCanvas();

            // 创建ChatComponent
            SetupChatComponent();

            // 创建UI布局
            CreateUILayout();

            // 配置ChatComponent的UI引用
            ConfigureChatComponent();

            Debug.Log("[ChatSceneSetup] 聊天UI设置完成！");
        }

        private void SetupMainCanvas()
        {
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                GameObject canvasGO = new GameObject("Canvas");
                mainCanvas = canvasGO.AddComponent<Canvas>();
                mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            // 设置Canvas Scaler
            var scaler = mainCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void SetupChatComponent()
        {
            chatComponent = FindObjectOfType<ChatComponent>();
            if (chatComponent == null)
            {
                GameObject chatGO = new GameObject("ChatComponent");
                chatComponent = chatGO.AddComponent<ChatComponent>();
            }
        }

        private void CreateUILayout()
        {
            // 清理现有UI (可选)
            // ClearExistingUI();

            // 创建主面板
            GameObject mainPanel = CreateMainPanel();

            // 创建左侧聊天面板
            GameObject chatPanel = CreateChatPanel(mainPanel);

            // 创建右侧控制面板
            GameObject controlPanel = CreateControlPanel(mainPanel);

            // 创建底部状态面板
            GameObject statusPanel = CreateStatusPanel(mainPanel);
        }

        private GameObject CreateMainPanel()
        {
            GameObject mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(mainCanvas.transform, false);

            RectTransform rect = mainPanel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image img = mainPanel.AddComponent<Image>();
            img.color = backgroundColor;

            return mainPanel;
        }

        private GameObject CreateChatPanel(GameObject parent)
        {
            GameObject chatPanel = new GameObject("ChatPanel");
            chatPanel.transform.SetParent(parent.transform, false);

            RectTransform rect = chatPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0.2f);
            rect.anchorMax = new Vector2(0.7f, 1);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image img = chatPanel.AddComponent<Image>();
            img.color = panelColor;

            // 聊天显示区域
            CreateChatText(chatPanel);

            // 输入区域
            CreateInputArea(chatPanel);

            return chatPanel;
        }

        private void CreateChatText(GameObject parent)
        {
            GameObject scrollView = new GameObject("ChatScrollView");
            scrollView.transform.SetParent(parent.transform, false);

            RectTransform rect = scrollView.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.05f, 0.2f);
            rect.anchorMax = new Vector2(0.95f, 0.95f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();

            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollView.transform, false);
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.1f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            // Content
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 0);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.sizeDelta = Vector2.zero;

            // ChatText
            GameObject chatTextGO = new GameObject("ChatText");
            chatTextGO.transform.SetParent(content.transform, false);
            RectTransform chatTextRect = chatTextGO.AddComponent<RectTransform>();
            chatTextRect.anchorMin = Vector2.zero;
            chatTextRect.anchorMax = Vector2.one;
            chatTextRect.sizeDelta = Vector2.zero;

            Text chatText = chatTextGO.AddComponent<Text>();
            chatText.font = GetDefaultFont();
            chatText.fontSize = 16;
            chatText.color = textColor;
            chatText.alignment = TextAnchor.LowerLeft;
            chatText.verticalOverflow = VerticalWrapMode.Overflow;
            chatText.text = "[系统] 欢迎使用 PulseRPC 聊天系统\n点击下方按钮连接服务器开始游戏！";

            // 配置ScrollRect
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.vertical = true;
            scrollRect.horizontal = false;
        }

        private void CreateInputArea(GameObject parent)
        {
            // 输入框
            GameObject inputGO = new GameObject("InputField");
            inputGO.transform.SetParent(parent.transform, false);

            RectTransform inputRect = inputGO.AddComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0.05f, 0.02f);
            inputRect.anchorMax = new Vector2(0.65f, 0.15f);
            inputRect.sizeDelta = Vector2.zero;

            Image inputBg = inputGO.AddComponent<Image>();
            inputBg.color = Color.white;

            InputField inputField = inputGO.AddComponent<InputField>();

            // 输入框文本
            GameObject inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(inputGO.transform, false);
            RectTransform inputTextRect = inputTextGO.AddComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.sizeDelta = Vector2.zero;
            inputTextRect.offsetMin = new Vector2(10, 5);
            inputTextRect.offsetMax = new Vector2(-10, -5);

            Text inputText = inputTextGO.AddComponent<Text>();
            inputText.font = GetDefaultFont();
            inputText.fontSize = 16;
            inputText.color = Color.black;
            inputText.supportRichText = false;

            // 占位符
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(inputGO.transform, false);
            RectTransform placeholderRect = placeholderGO.AddComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.sizeDelta = Vector2.zero;
            placeholderRect.offsetMin = new Vector2(10, 5);
            placeholderRect.offsetMax = new Vector2(-10, -5);

            Text placeholder = placeholderGO.AddComponent<Text>();
            placeholder.font = GetDefaultFont();
            placeholder.fontSize = 16;
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            placeholder.text = "输入坐标 (x,z) 或消息...";

            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;

            // 发送按钮
            CreateSendButton(parent);
        }

        private void CreateSendButton(GameObject parent)
        {
            GameObject sendBtnGO = new GameObject("SendButton");
            sendBtnGO.transform.SetParent(parent.transform, false);

            RectTransform btnRect = sendBtnGO.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.67f, 0.02f);
            btnRect.anchorMax = new Vector2(0.95f, 0.15f);
            btnRect.sizeDelta = Vector2.zero;

            Image btnImg = sendBtnGO.AddComponent<Image>();
            btnImg.color = buttonColor;

            Button sendBtn = sendBtnGO.AddComponent<Button>();

            // 按钮文本
            GameObject btnTextGO = new GameObject("Text");
            btnTextGO.transform.SetParent(sendBtnGO.transform, false);
            RectTransform btnTextRect = btnTextGO.AddComponent<RectTransform>();
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            btnTextRect.sizeDelta = Vector2.zero;

            Text btnText = btnTextGO.AddComponent<Text>();
            btnText.font = GetDefaultFont();
            btnText.fontSize = 16;
            btnText.color = Color.white;
            btnText.text = "发送";
            btnText.alignment = TextAnchor.MiddleCenter;

            sendBtn.targetGraphic = btnImg;
        }

        private GameObject CreateControlPanel(GameObject parent)
        {
            GameObject controlPanel = new GameObject("ControlPanel");
            controlPanel.transform.SetParent(parent.transform, false);

            RectTransform rect = controlPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.72f, 0.2f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image img = controlPanel.AddComponent<Image>();
            img.color = panelColor;

            // 连接控制区域
            CreateConnectionControls(controlPanel);

            // 移动控制区域
            CreateMovementControls(controlPanel);

            // 玩家信息区域
            CreatePlayerInfo(controlPanel);

            return controlPanel;
        }

        private void CreateConnectionControls(GameObject parent)
        {
            // 连接按钮
            GameObject connectBtnGO = CreateButton("JoinOrLeaveButton", parent,
                new Vector2(0.1f, 0.85f), new Vector2(0.9f, 0.95f), "连接服务器");

            // 断开按钮
            GameObject disconnectBtnGO = CreateButton("DisconnectButton", parent,
                new Vector2(0.1f, 0.75f), new Vector2(0.9f, 0.83f), "断开连接");
        }

        private void CreateMovementControls(GameObject parent)
        {
            // 移动标题
            GameObject titleGO = new GameObject("MovementTitle");
            titleGO.transform.SetParent(parent.transform, false);
            RectTransform titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0.65f);
            titleRect.anchorMax = new Vector2(0.9f, 0.72f);
            titleRect.sizeDelta = Vector2.zero;

            Text titleText = titleGO.AddComponent<Text>();
            titleText.font = GetDefaultFont();
            titleText.fontSize = 14;
            titleText.color = textColor;
            titleText.text = "移动控制 (或使用WASD)";
            titleText.alignment = TextAnchor.MiddleCenter;

            // 前进按钮
            CreateButton("MoveForwardButton", parent,
                new Vector2(0.4f, 0.55f), new Vector2(0.6f, 0.62f), "↑");

            // 左移和右移按钮
            CreateButton("MoveLeftButton", parent,
                new Vector2(0.25f, 0.47f), new Vector2(0.4f, 0.54f), "←");
            CreateButton("MoveRightButton", parent,
                new Vector2(0.6f, 0.47f), new Vector2(0.75f, 0.54f), "→");

            // 后退按钮
            CreateButton("MoveBackwardButton", parent,
                new Vector2(0.4f, 0.39f), new Vector2(0.6f, 0.46f), "↓");
        }

        private void CreatePlayerInfo(GameObject parent)
        {
            // 玩家信息标题
            GameObject infoTitleGO = new GameObject("PlayerInfoTitle");
            infoTitleGO.transform.SetParent(parent.transform, false);
            RectTransform infoTitleRect = infoTitleGO.AddComponent<RectTransform>();
            infoTitleRect.anchorMin = new Vector2(0.1f, 0.32f);
            infoTitleRect.anchorMax = new Vector2(0.9f, 0.37f);
            infoTitleRect.sizeDelta = Vector2.zero;

            Text infoTitleText = infoTitleGO.AddComponent<Text>();
            infoTitleText.font = GetDefaultFont();
            infoTitleText.fontSize = 12;
            infoTitleText.color = textColor;
            infoTitleText.text = "玩家信息";
            infoTitleText.alignment = TextAnchor.MiddleCenter;

            // 玩家信息文本
            GameObject playerInfoGO = new GameObject("PlayerInfoText");
            playerInfoGO.transform.SetParent(parent.transform, false);
            RectTransform playerInfoRect = playerInfoGO.AddComponent<RectTransform>();
            playerInfoRect.anchorMin = new Vector2(0.1f, 0.22f);
            playerInfoRect.anchorMax = new Vector2(0.9f, 0.31f);
            playerInfoRect.sizeDelta = Vector2.zero;

            Text playerInfoText = playerInfoGO.AddComponent<Text>();
            playerInfoText.font = GetDefaultFont();
            playerInfoText.fontSize = 11;
            playerInfoText.color = textColor;
            playerInfoText.text = "未登录";
            playerInfoText.alignment = TextAnchor.UpperLeft;

            // 位置信息文本
            GameObject positionGO = new GameObject("PositionText");
            positionGO.transform.SetParent(parent.transform, false);
            RectTransform positionRect = positionGO.AddComponent<RectTransform>();
            positionRect.anchorMin = new Vector2(0.1f, 0.12f);
            positionRect.anchorMax = new Vector2(0.9f, 0.21f);
            positionRect.sizeDelta = Vector2.zero;

            Text positionText = positionGO.AddComponent<Text>();
            positionText.font = GetDefaultFont();
            positionText.fontSize = 11;
            positionText.color = textColor;
            positionText.text = "位置: (0, 0, 0)";
            positionText.alignment = TextAnchor.UpperLeft;
        }

        private GameObject CreateStatusPanel(GameObject parent)
        {
            GameObject statusPanel = new GameObject("StatusPanel");
            statusPanel.transform.SetParent(parent.transform, false);

            RectTransform rect = statusPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0.18f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image img = statusPanel.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            // 状态文本
            GameObject statusTextGO = new GameObject("StatusText");
            statusTextGO.transform.SetParent(statusPanel.transform, false);
            RectTransform statusRect = statusTextGO.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.02f, 0.1f);
            statusRect.anchorMax = new Vector2(0.98f, 0.9f);
            statusRect.sizeDelta = Vector2.zero;

            Text statusText = statusTextGO.AddComponent<Text>();
            statusText.font = GetDefaultFont();
            statusText.fontSize = 12;
            statusText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            statusText.text = "提示: 使用WASD键移动，或在左侧输入框输入坐标 (如: 5,3) 进行精确移动";
            statusText.alignment = TextAnchor.MiddleLeft;

            return statusPanel;
        }

        private GameObject CreateButton(string btnName, GameObject parent, Vector2 anchorMin, Vector2 anchorMax, string text)
        {
            GameObject btnGO = new GameObject(btnName);
            btnGO.transform.SetParent(parent.transform, false);

            RectTransform btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.anchorMin = anchorMin;
            btnRect.anchorMax = anchorMax;
            btnRect.sizeDelta = Vector2.zero;

            Image btnImg = btnGO.AddComponent<Image>();
            btnImg.color = buttonColor;

            Button btn = btnGO.AddComponent<Button>();

            // 按钮文本
            if (!string.IsNullOrEmpty(text))
            {
                GameObject btnTextGO = new GameObject("Text");
                btnTextGO.transform.SetParent(btnGO.transform, false);
                RectTransform btnTextRect = btnTextGO.AddComponent<RectTransform>();
                btnTextRect.anchorMin = Vector2.zero;
                btnTextRect.anchorMax = Vector2.one;
                btnTextRect.sizeDelta = Vector2.zero;

                var btnText = btnTextGO.AddComponent<Text>();
                btnText.font = GetDefaultFont();
                btnText.fontSize = btnName.Contains("Movement") || btnName.Contains("Move") ? 18 : 14;
                btnText.color = Color.white;
                btnText.text = text;
                btnText.alignment = TextAnchor.MiddleCenter;

                // 如果是连接按钮，保存文本组件引用
                if (btnName == "JoinOrLeaveButton")
                {
                    btnTextGO.name = "JoinOrLeaveButtonText";
                }
            }

            btn.targetGraphic = btnImg;
            return btnGO;
        }

        private void ConfigureChatComponent()
        {
            if (!chatComponent) return;

            // 获取所有UI组件引用
            chatComponent.ChatText = GameObject.Find("ChatText")?.GetComponent<Text>();
            chatComponent.JoinOrLeaveButton = GameObject.Find("JoinOrLeaveButton")?.GetComponent<Button>();
            chatComponent.JoinOrLeaveButtonText = GameObject.Find("JoinOrLeaveButtonText")?.GetComponent<Text>();
            chatComponent.SendMessageButton = GameObject.Find("SendButton")?.GetComponent<Button>();
            chatComponent.Input = GameObject.Find("InputField")?.GetComponent<InputField>();
            chatComponent.DisconnectButton = GameObject.Find("DisconnectButton")?.GetComponent<Button>();

            // 移动控制UI
            chatComponent.MoveForwardButton = GameObject.Find("MoveForwardButton")?.GetComponent<Button>();
            chatComponent.MoveBackwardButton = GameObject.Find("MoveBackwardButton")?.GetComponent<Button>();
            chatComponent.MoveLeftButton = GameObject.Find("MoveLeftButton")?.GetComponent<Button>();
            chatComponent.MoveRightButton = GameObject.Find("MoveRightButton")?.GetComponent<Button>();
            chatComponent.PlayerInfoText = GameObject.Find("PlayerInfoText")?.GetComponent<Text>();
            chatComponent.PositionText = GameObject.Find("PositionText")?.GetComponent<Text>();

            Debug.Log("[ChatSceneSetup] ChatComponent UI引用配置完成");
        }

        private Font GetDefaultFont()
        {
            if (defaultFont is not null) return defaultFont;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        [ContextMenu("清理现有UI")]
        public void ClearExistingUI()
        {
            // 清理现有的UI元素（慎用）
            var children = new Transform[mainCanvas.transform.childCount];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = mainCanvas.transform.GetChild(i);
            }

            foreach (var child in children)
            {
                if (child.name != "EventSystem")
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        private void OnValidate()
        {
            // 确保颜色值在合理范围内
            backgroundColor.a = Mathf.Clamp01(backgroundColor.a);
            panelColor.a = Mathf.Clamp01(panelColor.a);
        }
    }
}
