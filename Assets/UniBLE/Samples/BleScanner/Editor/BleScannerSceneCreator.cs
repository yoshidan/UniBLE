#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UniBLE.Samples.Editor
{
    /// <summary>
    /// Editor utility to create the BLE Scanner sample scene
    /// </summary>
    public static class BleScannerSceneCreator
    {
        [MenuItem("UniBLE/Create BLE Scanner Sample Scene")]
        public static void CreateSampleScene()
        {
            // Create new scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Create Canvas
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var canvasScaler = canvasGo.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1080, 1920);
            canvasScaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Create EventSystem
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            // ========== Scan Panel ==========
            var scanPanelGo = CreatePanel("ScanPanel", canvasGo.transform, new Color(0.15f, 0.15f, 0.15f, 1f));

            // Title
            CreateLabel("Title", scanPanelGo.transform, "BLE Scanner", 48, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(0, 80));

            // Status Text
            var statusGo = CreateLabel("StatusText", scanPanelGo.transform, "Initializing...", 30, Color.yellow, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -120), new Vector2(0, 50));
            var statusText = statusGo.GetComponent<Text>();

            // Button container
            var btnContainerGo = CreateUIElement("ButtonContainer", scanPanelGo.transform);
            var hlg = btnContainerGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            var btnContainerRect = btnContainerGo.GetComponent<RectTransform>();
            btnContainerRect.anchorMin = new Vector2(0.5f, 1);
            btnContainerRect.anchorMax = new Vector2(0.5f, 1);
            btnContainerRect.pivot = new Vector2(0.5f, 1);
            btnContainerRect.anchoredPosition = new Vector2(0, -180);
            btnContainerRect.sizeDelta = new Vector2(500, 90);

            var scanButtonGo = CreateButton("ScanButton", btnContainerGo.transform, "Scan", new Color(0.3f, 0.3f, 0.8f), new Vector2(220, 80));
            var stopButtonGo = CreateButton("StopButton", btnContainerGo.transform, "Stop", new Color(0.3f, 0.3f, 0.8f), new Vector2(220, 80));

            // Scroll view for device list
            var (scrollViewGo, contentGo) = CreateScrollView("DeviceList", scanPanelGo.transform,
                new Vector2(20, 20), new Vector2(-20, -290));

            // ========== Device Detail Panel ==========
            var detailPanelGo = CreatePanel("DeviceDetailPanel", canvasGo.transform, new Color(0.15f, 0.15f, 0.15f, 1f));

            // Back button (top-left)
            var backBtnGo = CreateButton("BackButton", detailPanelGo.transform, "< Back", new Color(0.35f, 0.35f, 0.35f), new Vector2(200, 70));
            var backBtnRect = backBtnGo.GetComponent<RectTransform>();
            backBtnRect.anchorMin = new Vector2(0, 1);
            backBtnRect.anchorMax = new Vector2(0, 1);
            backBtnRect.pivot = new Vector2(0, 1);
            backBtnRect.anchoredPosition = new Vector2(20, -20);

            // Device name
            var detailNameGo = CreateLabel("DeviceName", detailPanelGo.transform, "Device Name", 40, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -100), new Vector2(0, 90));
            var detailNameText = detailNameGo.GetComponent<Text>();
            detailNameText.supportRichText = true;

            // Detail status text
            var detailStatusGo = CreateLabel("StatusText", detailPanelGo.transform, "", 28, Color.yellow, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(0, 40));
            var detailStatusText = detailStatusGo.GetComponent<Text>();

            // Connect/Disconnect button (centered)
            var connectBtnGo = CreateButton("ConnectButton", detailPanelGo.transform, "Connect", new Color(0.3f, 0.7f, 0.3f), new Vector2(300, 80));
            var connectBtnRect = connectBtnGo.GetComponent<RectTransform>();
            connectBtnRect.anchorMin = new Vector2(0.5f, 1);
            connectBtnRect.anchorMax = new Vector2(0.5f, 1);
            connectBtnRect.pivot = new Vector2(0.5f, 1);
            connectBtnRect.anchoredPosition = new Vector2(0, -250);
            var connectBtnText = connectBtnGo.GetComponentInChildren<Text>();

            // Characteristic list scroll view
            var (_, detailContentGo) = CreateScrollView("CharacteristicList", detailPanelGo.transform,
                new Vector2(20, 20), new Vector2(-20, -350));

            // ========== Wire up DeviceDetailPanel component ==========
            var detailPanel = detailPanelGo.AddComponent<DeviceDetailPanel>();
            var detailSo = new SerializedObject(detailPanel);
            detailSo.FindProperty("deviceNameText").objectReferenceValue = detailNameText;
            detailSo.FindProperty("statusText").objectReferenceValue = detailStatusText;
            detailSo.FindProperty("backButton").objectReferenceValue = backBtnGo.GetComponent<Button>();
            detailSo.FindProperty("connectButton").objectReferenceValue = connectBtnGo.GetComponent<Button>();
            detailSo.FindProperty("connectButtonText").objectReferenceValue = connectBtnText;
            detailSo.FindProperty("characteristicListContent").objectReferenceValue = detailContentGo.GetComponent<RectTransform>();
            detailSo.ApplyModifiedProperties();

            // ========== Device List Item Prefab ==========
            var prefabGo = CreateDeviceItemPrefab();

            // ========== Wire up BleScannerSample ==========
            var scannerGo = new GameObject("BleScanner");
            var scanner = scannerGo.AddComponent<BleScannerSample>();

            var so = new SerializedObject(scanner);
            so.FindProperty("scanButton").objectReferenceValue = scanButtonGo.GetComponent<Button>();
            so.FindProperty("stopButton").objectReferenceValue = stopButtonGo.GetComponent<Button>();
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("deviceListContent").objectReferenceValue = contentGo.GetComponent<RectTransform>();
            so.FindProperty("deviceItemPrefab").objectReferenceValue = prefabGo;
            so.FindProperty("scanPanel").objectReferenceValue = scanPanelGo;
            so.FindProperty("deviceDetailPanel").objectReferenceValue = detailPanel;
            so.ApplyModifiedProperties();

            // ========== Save ==========
            var scenePath = "Assets/UniBLE/Samples~/BleScanner/BleScannerSample.unity";
            EnsureDirectory(scenePath);
            EditorSceneManager.SaveScene(scene, scenePath);

            var prefabPath = "Assets/UniBLE/Samples~/BleScanner/Prefabs/DeviceListItem.prefab";
            EnsureDirectory(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(prefabGo, prefabPath);
            Object.DestroyImmediate(prefabGo);

            // Re-assign saved prefab
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            so = new SerializedObject(scanner);
            so.FindProperty("deviceItemPrefab").objectReferenceValue = savedPrefab;
            so.ApplyModifiedProperties();

            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log("BLE Scanner Sample Scene created at: " + scenePath);
            EditorUtility.DisplayDialog("Success", "BLE Scanner Sample Scene created!\n\nPath: " + scenePath, "OK");
        }

        // ---- Helpers ----

        private static GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static GameObject CreatePanel(string name, Transform parent, Color bgColor)
        {
            var go = CreateUIElement(name, parent);
            var image = go.AddComponent<Image>();
            image.color = bgColor;
            SetStretchAll(go.GetComponent<RectTransform>());
            return go;
        }

        private static GameObject CreateLabel(string name, Transform parent, string text, int fontSize, Color color,
            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = CreateUIElement(name, parent);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = alignment;
            t.color = color;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string text, Color color, Vector2 size)
        {
            var go = CreateUIElement(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = image;
            go.GetComponent<RectTransform>().sizeDelta = size;

            var textGo = CreateUIElement("Text", go.transform);
            var t = textGo.AddComponent<Text>();
            t.text = text;
            t.fontSize = 32;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            SetStretchAll(textGo.GetComponent<RectTransform>());

            return go;
        }

        private static (GameObject scrollView, GameObject content) CreateScrollView(string name, Transform parent,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var scrollViewGo = CreateUIElement(name + "ScrollView", parent);
            var scrollRect = scrollViewGo.AddComponent<ScrollRect>();
            var scrollImage = scrollViewGo.AddComponent<Image>();
            scrollImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            var svRect = scrollViewGo.GetComponent<RectTransform>();
            svRect.anchorMin = Vector2.zero;
            svRect.anchorMax = Vector2.one;
            svRect.pivot = new Vector2(0.5f, 0.5f);
            svRect.offsetMin = offsetMin;
            svRect.offsetMax = offsetMax;

            var viewportGo = CreateUIElement("Viewport", scrollViewGo.transform);
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = Color.white;
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            SetStretchAll(viewportGo.GetComponent<RectTransform>());

            var contentGo = CreateUIElement("Content", viewportGo.transform);
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportGo.GetComponent<RectTransform>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            return (scrollViewGo, contentGo);
        }

        private static GameObject CreateDeviceItemPrefab()
        {
            var itemGo = new GameObject("DeviceListItem");
            var itemRect = itemGo.AddComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0, 110);

            var itemImage = itemGo.AddComponent<Image>();
            itemImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            var hlg = itemGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(20, 20, 10, 10);
            hlg.spacing = 15;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Info container
            var infoGo = CreateUIElement("Info", itemGo.transform);
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childControlWidth = true;
            infoVlg.childControlHeight = true;
            var infoLe = infoGo.AddComponent<LayoutElement>();
            infoLe.flexibleWidth = 1;

            var nameGo = CreateUIElement("DeviceName", infoGo.transform);
            var nameText = nameGo.AddComponent<Text>();
            nameText.text = "Device Name";
            nameText.fontSize = 32;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = Color.white;
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var idGo = CreateUIElement("DeviceId", infoGo.transform);
            var idText = idGo.AddComponent<Text>();
            idText.text = "00:00:00:00:00:00";
            idText.fontSize = 24;
            idText.color = Color.gray;
            idText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var selectBtnGo = CreateButton("SelectButton", itemGo.transform, "Connect", new Color(0.3f, 0.3f, 0.8f), new Vector2(180, 80));
            var selectBtnLe = selectBtnGo.AddComponent<LayoutElement>();
            selectBtnLe.minWidth = 180;
            selectBtnLe.preferredWidth = 180;

            var deviceListItem = itemGo.AddComponent<DeviceListItem>();
            var so = new SerializedObject(deviceListItem);
            so.FindProperty("deviceNameText").objectReferenceValue = nameText;
            so.FindProperty("deviceIdText").objectReferenceValue = idText;
            so.FindProperty("selectButton").objectReferenceValue = selectBtnGo.GetComponent<Button>();
            so.ApplyModifiedProperties();

            return itemGo;
        }

        private static void SetStretchAll(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = System.IO.Path.GetDirectoryName(filePath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
        }
    }
}
#endif
