using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using ScrollRect = UnityEngine.UI.ScrollRect;

namespace UniBLE.Samples
{
    /// <summary>
    /// Sample script for scanning BLE devices.
    /// Scan panel shows device list. Selecting a device opens DeviceDetailPanel.
    /// </summary>
    public class BleScannerSample : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button scanButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Transform deviceListContent;
        [SerializeField] private GameObject deviceItemPrefab;

        [Header("Panels")]
        [SerializeField] private GameObject scanPanel;
        [SerializeField] private DeviceDetailPanel deviceDetailPanel;

        private IBleAdapter _adapter;
        private readonly Dictionary<string, DeviceListItem> _deviceItems = new Dictionary<string, DeviceListItem>();
        private CancellationTokenSource _scanCts;
        private bool _isScanning;

        private async void Start()
        {
            BleManager.DebugLogging = true;
            scanButton.onClick.AddListener(OnScanButtonClicked);
            stopButton.onClick.AddListener(OnStopButtonClicked);

            stopButton.interactable = false;

            // Show scan panel, hide detail panel
            scanPanel.SetActive(true);
            deviceDetailPanel.gameObject.SetActive(false);

            // Increase scroll speed
            if (deviceListContent != null)
            {
                var scrollRect = deviceListContent.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.scrollSensitivity = 50f;
                }
            }

            try
            {
                _adapter = BleManager.Adapter;
                UpdateStatus("Checking BLE availability...");

                var isAvailable = await _adapter.IsAvailableAsync();
                if (!isAvailable)
                {
                    UpdateStatus($"BLE is not available (State: {_adapter.State})");
                    scanButton.interactable = false;
                    return;
                }

                UpdateStatus("Requesting permissions...");
                var hasPermission = await _adapter.RequestPermissionAsync();
                if (!hasPermission)
                {
                    UpdateStatus("BLE permission denied");
                    scanButton.interactable = false;
                    return;
                }

                UpdateStatus("Ready to scan");
            }
            catch (BleException e)
            {
                UpdateStatus($"Error: {e.Message}");
                scanButton.interactable = false;
            }
        }

        private async void OnScanButtonClicked()
        {
            if (_isScanning) return;

            ClearDeviceList();
            _isScanning = true;
            scanButton.interactable = false;
            stopButton.interactable = true;

            _scanCts = new CancellationTokenSource();

            try
            {
                UpdateStatus("Scanning...");
                await _adapter.StartScanAsync(
                    serviceUuids: null,
                    onDeviceDiscovered: OnDeviceDiscovered,
                    cancellationToken: _scanCts.Token
                );
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Scan stopped");
            }
            catch (BleException e)
            {
                UpdateStatus($"Scan error: {e.Message}");
            }
        }

        private async void OnStopButtonClicked()
        {
            if (!_isScanning) return;
            _scanCts?.Cancel();

            try
            {
                await _adapter.StopScanAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error stopping scan: {e.Message}");
            }

            _isScanning = false;
            scanButton.interactable = true;
            stopButton.interactable = false;
            UpdateStatus($"Scan stopped. Found {_deviceItems.Count} devices.");
        }

        private void OnDeviceDiscovered(IBleDevice device)
        {
            try
            {
                if (_deviceItems.ContainsKey(device.Id)) return;

                Debug.Log($"[BleScanner] Discovered: {device.Name} ({device.Id})");

                if (deviceListContent == null)
                {
                    _deviceItems[device.Id] = null;
                }
                else if (deviceItemPrefab != null)
                {
                    var itemGo = Instantiate(deviceItemPrefab, deviceListContent);
                    var item = itemGo.GetComponent<DeviceListItem>();
                    item.Setup(device, OnDeviceSelected);
                    _deviceItems[device.Id] = item;
                }
                else
                {
                    // Fallback: create device row without prefab
                    var rowGo = new GameObject($"Device_{device.Id}");
                    rowGo.transform.SetParent(deviceListContent, false);

                    var image = rowGo.AddComponent<Image>();
                    image.color = new Color(0.25f, 0.25f, 0.25f, 1f);

                    var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
                    layout.padding = new RectOffset(20, 20, 10, 10);
                    layout.spacing = 15;
                    layout.childAlignment = TextAnchor.MiddleLeft;
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = false;
                    layout.childForceExpandHeight = true;

                    var le = rowGo.AddComponent<LayoutElement>();
                    le.minHeight = 110;
                    le.preferredHeight = 110;

                    // Info
                    var infoGo = new GameObject("Info");
                    infoGo.transform.SetParent(rowGo.transform, false);
                    infoGo.AddComponent<RectTransform>();
                    var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
                    infoVlg.childControlWidth = true;
                    infoVlg.childControlHeight = true;
                    var infoLe = infoGo.AddComponent<LayoutElement>();
                    infoLe.flexibleWidth = 1;

                    var nameGo = new GameObject("Name");
                    nameGo.transform.SetParent(infoGo.transform, false);
                    nameGo.AddComponent<RectTransform>();
                    var nameText = nameGo.AddComponent<Text>();
                    nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    nameText.fontSize = 32;
                    nameText.fontStyle = FontStyle.Bold;
                    nameText.color = Color.white;
                    nameText.text = string.IsNullOrEmpty(device.Name) ? "Unknown" : device.Name;

                    var idGo = new GameObject("Id");
                    idGo.transform.SetParent(infoGo.transform, false);
                    idGo.AddComponent<RectTransform>();
                    var idText = idGo.AddComponent<Text>();
                    idText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    idText.fontSize = 24;
                    idText.color = Color.gray;
                    idText.text = device.Id;

                    // Connect button
                    var btnGo = new GameObject("ConnectBtn");
                    btnGo.transform.SetParent(rowGo.transform, false);
                    btnGo.AddComponent<RectTransform>();
                    var btnImage = btnGo.AddComponent<Image>();
                    btnImage.color = new Color(0.3f, 0.3f, 0.8f);
                    var btn = btnGo.AddComponent<Button>();
                    btn.targetGraphic = btnImage;
                    var btnLe = btnGo.AddComponent<LayoutElement>();
                    btnLe.minWidth = 180;
                    btnLe.preferredWidth = 180;

                    var btnTextGo = new GameObject("Text");
                    btnTextGo.transform.SetParent(btnGo.transform, false);
                    var btnText = btnTextGo.AddComponent<Text>();
                    btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    btnText.fontSize = 32;
                    btnText.color = Color.white;
                    btnText.alignment = TextAnchor.MiddleCenter;
                    btnText.text = "Detail";
                    var btnTextRect = btnTextGo.GetComponent<RectTransform>();
                    btnTextRect.anchorMin = Vector2.zero;
                    btnTextRect.anchorMax = Vector2.one;
                    btnTextRect.offsetMin = Vector2.zero;
                    btnTextRect.offsetMax = Vector2.zero;

                    var capturedDevice = device;
                    btn.onClick.AddListener(() => OnDeviceSelected(capturedDevice));
                    _deviceItems[device.Id] = null;
                }

                UpdateStatus($"Scanning... Found {_deviceItems.Count} devices");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] OnDeviceDiscovered exception: {e}");
            }
        }

        private async void OnDeviceSelected(IBleDevice device)
        {
            Debug.Log($"[BleScanner] Selected device: {device.Name} ({device.Id})");

            // Stop scanning
            if (_isScanning)
            {
                _scanCts?.Cancel();
                try { await _adapter.StopScanAsync(); } catch { }
                _isScanning = false;
                scanButton.interactable = true;
                stopButton.interactable = false;
            }

            // Switch to detail panel
            scanPanel.SetActive(false);
            deviceDetailPanel.gameObject.SetActive(true);
            deviceDetailPanel.Setup(device, () =>
            {
                // Back: return to scan panel
                deviceDetailPanel.gameObject.SetActive(false);
                scanPanel.SetActive(true);
                UpdateStatus($"Found {_deviceItems.Count} devices");
            });
        }

        private void ClearDeviceList()
        {
            if (deviceListContent != null)
            {
                foreach (Transform child in deviceListContent)
                {
                    Destroy(child.gameObject);
                }
            }
            _deviceItems.Clear();
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log($"[BleScanner] {message}");
        }

        private void OnDestroy()
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
        }
    }
}
