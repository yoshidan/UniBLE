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
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Create EventSystem
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            // Create Panel (background)
            var panelGo = CreateUIElement("Panel", canvasGo.transform);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            SetStretchAll(panelGo.GetComponent<RectTransform>());

            // Create Title
            var titleGo = CreateUIElement("Title", panelGo.transform);
            var titleText = titleGo.AddComponent<Text>();
            titleText.text = "BLE Scanner Sample";
            titleText.fontSize = 32;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -20);
            titleRect.sizeDelta = new Vector2(0, 50);

            // Create Status Text
            var statusGo = CreateUIElement("StatusText", panelGo.transform);
            var statusText = statusGo.AddComponent<Text>();
            statusText.text = "Initializing...";
            statusText.fontSize = 18;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = Color.yellow;
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var statusRect = statusGo.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 1);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.pivot = new Vector2(0.5f, 1);
            statusRect.anchoredPosition = new Vector2(0, -80);
            statusRect.sizeDelta = new Vector2(0, 30);

            // Create Button Container
            var buttonContainerGo = CreateUIElement("ButtonContainer", panelGo.transform);
            var hlg = buttonContainerGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            var buttonContainerRect = buttonContainerGo.GetComponent<RectTransform>();
            buttonContainerRect.anchorMin = new Vector2(0.5f, 1);
            buttonContainerRect.anchorMax = new Vector2(0.5f, 1);
            buttonContainerRect.pivot = new Vector2(0.5f, 1);
            buttonContainerRect.anchoredPosition = new Vector2(0, -120);
            buttonContainerRect.sizeDelta = new Vector2(300, 50);

            // Create Scan Button
            var scanButtonGo = CreateButton("ScanButton", buttonContainerGo.transform, "Scan");
            var scanButtonRect = scanButtonGo.GetComponent<RectTransform>();
            scanButtonRect.sizeDelta = new Vector2(120, 40);

            // Create Stop Button
            var stopButtonGo = CreateButton("StopButton", buttonContainerGo.transform, "Stop");
            var stopButtonRect = stopButtonGo.GetComponent<RectTransform>();
            stopButtonRect.sizeDelta = new Vector2(120, 40);

            // Create Scroll View for device list
            var scrollViewGo = CreateUIElement("DeviceListScrollView", panelGo.transform);
            var scrollRect = scrollViewGo.AddComponent<ScrollRect>();
            var scrollImage = scrollViewGo.AddComponent<Image>();
            scrollImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);
            var scrollRectTransform = scrollViewGo.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0, 0);
            scrollRectTransform.anchorMax = new Vector2(1, 1);
            scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
            scrollRectTransform.offsetMin = new Vector2(20, 20);
            scrollRectTransform.offsetMax = new Vector2(-20, -180);

            // Create Viewport
            var viewportGo = CreateUIElement("Viewport", scrollViewGo.transform);
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = Color.white;
            var viewportMask = viewportGo.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            SetStretchAll(viewportRect);

            // Create Content
            var contentGo = CreateUIElement("Content", viewportGo.transform);
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 5;
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
            contentRect.sizeDelta = new Vector2(0, 0);

            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.vertical = true;
            scrollRect.horizontal = false;

            // Create Device Item Prefab
            var prefabGo = CreateDeviceItemPrefab();

            // Create Scanner GameObject
            var scannerGo = new GameObject("BleScanner");
            var scanner = scannerGo.AddComponent<BleScannerSample>();

            // Assign references using SerializedObject
            var so = new SerializedObject(scanner);
            so.FindProperty("scanButton").objectReferenceValue = scanButtonGo.GetComponent<Button>();
            so.FindProperty("stopButton").objectReferenceValue = stopButtonGo.GetComponent<Button>();
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("deviceListContent").objectReferenceValue = contentRect;
            so.FindProperty("deviceItemPrefab").objectReferenceValue = prefabGo;
            so.ApplyModifiedProperties();

            // Save scene
            var scenePath = "Assets/UniBLE/Samples~/BleScanner/BleScannerSample.unity";
            var directory = System.IO.Path.GetDirectoryName(scenePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            EditorSceneManager.SaveScene(scene, scenePath);

            // Save prefab
            var prefabPath = "Assets/UniBLE/Samples~/BleScanner/Prefabs/DeviceListItem.prefab";
            var prefabDir = System.IO.Path.GetDirectoryName(prefabPath);
            if (!System.IO.Directory.Exists(prefabDir))
            {
                System.IO.Directory.CreateDirectory(prefabDir);
            }
            PrefabUtility.SaveAsPrefabAsset(prefabGo, prefabPath);
            Object.DestroyImmediate(prefabGo);

            // Re-assign prefab reference
            var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            so = new SerializedObject(scanner);
            so.FindProperty("deviceItemPrefab").objectReferenceValue = savedPrefab;
            so.ApplyModifiedProperties();

            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log("BLE Scanner Sample Scene created at: " + scenePath);
            EditorUtility.DisplayDialog("Success", "BLE Scanner Sample Scene created!\n\nPath: " + scenePath, "OK");
        }

        private static GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static GameObject CreateButton(string name, Transform parent, string text)
        {
            var buttonGo = CreateUIElement(name, parent);
            var buttonImage = buttonGo.AddComponent<Image>();
            buttonImage.color = new Color(0.3f, 0.3f, 0.8f, 1f);
            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            var textGo = CreateUIElement("Text", buttonGo.transform);
            var textComp = textGo.AddComponent<Text>();
            textComp.text = text;
            textComp.fontSize = 18;
            textComp.alignment = TextAnchor.MiddleCenter;
            textComp.color = Color.white;
            textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            SetStretchAll(textGo.GetComponent<RectTransform>());

            return buttonGo;
        }

        private static GameObject CreateDeviceItemPrefab()
        {
            var itemGo = new GameObject("DeviceListItem");
            var itemRect = itemGo.AddComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0, 60);

            var itemImage = itemGo.AddComponent<Image>();
            itemImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            var hlg = itemGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 5, 5);
            hlg.spacing = 10;
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

            // Device Name
            var nameGo = CreateUIElement("DeviceName", infoGo.transform);
            var nameText = nameGo.AddComponent<Text>();
            nameText.text = "Device Name";
            nameText.fontSize = 18;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = Color.white;
            nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Device ID
            var idGo = CreateUIElement("DeviceId", infoGo.transform);
            var idText = idGo.AddComponent<Text>();
            idText.text = "00:00:00:00:00:00";
            idText.fontSize = 12;
            idText.color = Color.gray;
            idText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Select Button
            var selectButtonGo = CreateButton("SelectButton", itemGo.transform, "Connect");
            var selectButtonLe = selectButtonGo.AddComponent<LayoutElement>();
            selectButtonLe.minWidth = 80;
            selectButtonLe.preferredWidth = 80;

            // Add DeviceListItem component
            var deviceListItem = itemGo.AddComponent<DeviceListItem>();
            var so = new SerializedObject(deviceListItem);
            so.FindProperty("deviceNameText").objectReferenceValue = nameText;
            so.FindProperty("deviceIdText").objectReferenceValue = idText;
            so.FindProperty("selectButton").objectReferenceValue = selectButtonGo.GetComponent<Button>();
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
    }
}
#endif
