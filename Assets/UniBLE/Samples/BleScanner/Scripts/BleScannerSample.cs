using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace UniBLE.Samples
{
    /// <summary>
    /// Sample script for scanning BLE devices
    /// </summary>
    public class BleScannerSample : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button scanButton;
        [SerializeField] private Button stopButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Transform deviceListContent;
        [SerializeField] private GameObject deviceItemPrefab;

        private IBleAdapter _adapter;
        private readonly Dictionary<string, DeviceListItem> _deviceItems = new Dictionary<string, DeviceListItem>();
        private CancellationTokenSource _scanCts;
        private bool _isScanning;

        private async void Start()
        {
            scanButton.onClick.AddListener(OnScanButtonClicked);
            stopButton.onClick.AddListener(OnStopButtonClicked);

            stopButton.interactable = false;

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
                var devices = new List<String>();
                devices.Add("e2b23000-cd58-6f80-f2b0-b9bd830480be".ToUpper());
                await _adapter.StartScanAsync(
                    serviceUuids: devices, // Scan for all devices
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
                // This is called from the main thread via MainThreadDispatcher
                if (_deviceItems.ContainsKey(device.Id))
                {
                    return;
                }

                // Log discovered device
                Debug.Log($"[BleScanner] Discovered: {device.Name} ({device.Id})");

                if (deviceItemPrefab != null && deviceListContent != null)
                {
                    var itemGo = Instantiate(deviceItemPrefab, deviceListContent);
                    var item = itemGo.GetComponent<DeviceListItem>();
                    item.Setup(device, OnDeviceSelected);
                    _deviceItems[device.Id] = item;
                }
                else if (deviceListContent != null)
                {
                    // Create button programmatically if no prefab
                    var buttonGo = new GameObject($"Device_{device.Id}");
                    buttonGo.transform.SetParent(deviceListContent, false);

                    // Add background image
                    var image = buttonGo.AddComponent<Image>();
                    image.color = new Color(0.9f, 0.9f, 0.9f, 1f); // Light gray background

                    // Add button
                    var button = buttonGo.AddComponent<Button>();
                    var colors = button.colors;
                    colors.highlightedColor = new Color(0.7f, 0.85f, 1f, 1f); // Light blue on hover
                    colors.pressedColor = new Color(0.5f, 0.7f, 0.9f, 1f);
                    button.colors = colors;

                    // Add layout element for sizing
                    var layoutElement = buttonGo.AddComponent<LayoutElement>();
                    layoutElement.minHeight = 40;
                    layoutElement.preferredHeight = 40;

                    // Create child text object
                    var textGo = new GameObject("Text");
                    textGo.transform.SetParent(buttonGo.transform, false);
                    var text = textGo.AddComponent<Text>();
                    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    text.fontSize = 14;
                    text.color = Color.black;
                    text.alignment = TextAnchor.MiddleLeft;
                    text.text = $"  {device.Name ?? "Unknown"}\n  {device.Id.Substring(0, 8)}...";

                    // Stretch text to fill button
                    var textRect = textGo.GetComponent<RectTransform>();
                    textRect.anchorMin = Vector2.zero;
                    textRect.anchorMax = Vector2.one;
                    textRect.offsetMin = Vector2.zero;
                    textRect.offsetMax = Vector2.zero;

                    var capturedDevice = device;
                    button.onClick.AddListener(() => OnDeviceSelected(capturedDevice));

                    _deviceItems[device.Id] = null;
                }
                else
                {
                    // No UI setup, just track the device count
                    _deviceItems[device.Id] = null;
                }

                UpdateStatus($"Scanning... Found {_deviceItems.Count} devices");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BleScanner] OnDeviceDiscovered exception: {e}");
            }
        }

        private async void OnDeviceSelected(IBleDevice device)
        {
            Debug.Log($"[BleScanner] Selected device: {device.Name} ({device.Id})");

            // Stop scanning before connecting
            if (_isScanning)
            {
                _scanCts?.Cancel();
                try
                {
                    await _adapter.StopScanAsync();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Error stopping scan: {e.Message}");
                }
                _isScanning = false;
                scanButton.interactable = true;
                stopButton.interactable = false;
            }

            try
            {
                UpdateStatus($"Connecting to {device.Name}...");
                await device.ConnectAsync();
                UpdateStatus($"Connected to {device.Name}!");
                Debug.Log($"[BleScanner] Connected to {device.Name} ({device.Id})");

                // Discover services after connection
                UpdateStatus($"Discovering services...");
                var services = await device.GetServicesAsync();
                Debug.Log($"[BleScanner] Found {services.Count} services:");

                int totalCharacteristics = 0;
                foreach (var service in services)
                {
                    Debug.Log($"[BleScanner]   Service: {service.Uuid}");

                    // Discover characteristics for each service
                    var characteristics = await service.GetCharacteristicsAsync();
                    totalCharacteristics += characteristics.Count;
                    foreach (var characteristic in characteristics)
                    {
                        Debug.Log($"[BleScanner]     Characteristic: {characteristic.Uuid} (Properties: {characteristic.Properties})");
                    }
                }
                UpdateStatus($"Connected. {services.Count} services, {totalCharacteristics} characteristics.");
            }
            catch (BleException e)
            {
                Debug.LogError($"[BleScanner] Connection error: {e.Message}");
                UpdateStatus($"Connection failed: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Unexpected error: {e}");
                UpdateStatus($"Error: {e.Message}");
            }
        }

        private void ClearDeviceList()
        {
            foreach (var item in _deviceItems.Values)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }
            // Also clear programmatically created buttons
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
            {
                statusText.text = message;
            }
            Debug.Log($"[BleScanner] {message}");
        }

        private void OnDestroy()
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
        }
    }
}
