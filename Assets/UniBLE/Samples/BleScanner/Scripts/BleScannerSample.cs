using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using ScrollRect = UnityEngine.UI.ScrollRect;

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
        private IBleDevice _connectedDevice;
        private readonly List<GameObject> _characteristicButtons = new List<GameObject>();
        private readonly HashSet<string> _subscribedCharacteristics = new HashSet<string>();
        private GameObject _disconnectButton;

        private async void Start()
        {
            BleManager.DebugLogging = true;
            scanButton.onClick.AddListener(OnScanButtonClicked);
            stopButton.onClick.AddListener(OnStopButtonClicked);

            stopButton.interactable = false;

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
                    serviceUuids: null, // Scan for all devices
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
                    layoutElement.minHeight = 120;
                    layoutElement.preferredHeight = 120;

                    // Create child text object
                    var textGo = new GameObject("Text");
                    textGo.transform.SetParent(buttonGo.transform, false);
                    var text = textGo.AddComponent<Text>();
                    text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    text.fontSize = 34;
                    text.color = Color.black;
                    text.alignment = TextAnchor.MiddleLeft;
                    text.text = $"  {device.Name ?? "Unknown"}\n  {device.Id}";

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
                device.OnConnectionStateChanged += (state) =>                                                                                                                                          
                {                                                                                                                                                                                      
                    Debug.Log($"Connection state: {state}"); // Connected, Disconnecting, Disconnected                                                                                                 
                };           
                await device.ConnectAsync();
                _connectedDevice = device;
                UpdateStatus($"Connected to {device.Name}!");
                Debug.Log($"[BleScanner] Connected to {device.Name} ({device.Id})");

                // Clear previous characteristic buttons
                ClearCharacteristicButtons();

                // Add Disconnect button
                CreateDisconnectButton();

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
                        CreateCharacteristicUI(service, characteristic);
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

        private void ClearCharacteristicButtons()
        {
            foreach (var go in _characteristicButtons)
            {
                Destroy(go);
            }
            _characteristicButtons.Clear();
            _subscribedCharacteristics.Clear();
            _disconnectButton = null;
        }

        private void CreateCharacteristicUI(IBleService service, IBleCharacteristic characteristic)
        {
            if (deviceListContent == null) return;

            var props = characteristic.Properties;
            bool hasRead = props.HasFlag(BleCharacteristicProperties.Read);
            bool hasWrite = props.HasFlag(BleCharacteristicProperties.Write) ||
                           props.HasFlag(BleCharacteristicProperties.WriteWithoutResponse);
            bool hasNotify = props.HasFlag(BleCharacteristicProperties.Notify) ||
                            props.HasFlag(BleCharacteristicProperties.Indicate);

            // Create container
            var containerGo = new GameObject($"Char_{characteristic.Uuid}");
            containerGo.transform.SetParent(deviceListContent, false);
            _characteristicButtons.Add(containerGo);

            var containerLayout = containerGo.AddComponent<HorizontalLayoutGroup>();
            containerLayout.spacing = 10;
            containerLayout.childForceExpandWidth = false;
            containerLayout.childForceExpandHeight = true;
            containerLayout.padding = new RectOffset(15, 15, 8, 8);

            var containerLayoutElement = containerGo.AddComponent<LayoutElement>();
            containerLayoutElement.minHeight = 100;

            var containerImage = containerGo.AddComponent<Image>();
            containerImage.color = new Color(0.95f, 0.95f, 0.95f, 1f);

            // Label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(containerGo.transform, false);
            var labelText = labelGo.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 28;
            labelText.color = Color.black;
            labelText.text = characteristic.Uuid;
            var labelLayout = labelGo.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1;

            // Read button
            if (hasRead)
            {
                CreateActionButton(containerGo.transform, "Read", new Color(0.6f, 0.8f, 0.6f),
                    () => OnReadClicked(characteristic));
            }

            // Write button
            if (hasWrite)
            {
                CreateActionButton(containerGo.transform, "Write", new Color(0.8f, 0.8f, 0.6f),
                    () => OnWriteClicked(characteristic));
            }

            // Notify/Unsubscribe button
            if (hasNotify)
            {
                var isSubscribed = _subscribedCharacteristics.Contains(characteristic.Uuid);
                var notifyButtonGo = CreateActionButton(containerGo.transform,
                    isSubscribed ? "Unsub" : "Notify",
                    isSubscribed ? new Color(0.9f, 0.6f, 0.6f) : new Color(0.6f, 0.7f, 0.9f),
                    null);
                var notifyButton = notifyButtonGo.GetComponent<Button>();
                notifyButton.onClick.AddListener(() => OnNotifyClicked(characteristic, notifyButtonGo));
            }
        }

        private GameObject CreateActionButton(Transform parent, string label, Color color, Action onClick)
        {
            var buttonGo = new GameObject(label);
            buttonGo.transform.SetParent(parent, false);

            var image = buttonGo.AddComponent<Image>();
            image.color = color;

            var button = buttonGo.AddComponent<Button>();
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var layoutElement = buttonGo.AddComponent<LayoutElement>();
            layoutElement.minWidth = 150;
            layoutElement.preferredWidth = 150;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(buttonGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 32;
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return buttonGo;
        }

        private async void OnReadClicked(IBleCharacteristic characteristic)
        {
            try
            {
                UpdateStatus($"Reading {characteristic.Uuid}...");
                var data = await characteristic.ReadAsync();
                var hex = BitConverter.ToString(data).Replace("-", " ");
                var text = Encoding.UTF8.GetString(data);
                Debug.Log($"[BleScanner] Read {characteristic.Uuid}: [{hex}] \"{text}\"");
                UpdateStatus($"Read: {hex}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Read error: {e.Message}");
                UpdateStatus($"Read failed: {e.Message}");
            }
        }

        private async void OnWriteClicked(IBleCharacteristic characteristic)
        {
            try
            {
                // Write current unix timestamp in seconds (little-endian)
                var unixSeconds = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var data = BitConverter.GetBytes(unixSeconds);
                Debug.Log($"[BleScanner] Writing unix timestamp: {unixSeconds} (bytes: {BitConverter.ToString(data)})");
                UpdateStatus($"Writing timestamp to {characteristic.Uuid}...");
                await characteristic.WriteAsync(data);
                Debug.Log($"[BleScanner] Write to {characteristic.Uuid} succeeded");
                UpdateStatus($"Write succeeded: {unixSeconds}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Write error: {e.Message}");
                UpdateStatus($"Write failed: {e.Message}");
            }
        }

        private async void OnNotifyClicked(IBleCharacteristic characteristic, GameObject buttonGo)
        {
            var isSubscribed = _subscribedCharacteristics.Contains(characteristic.Uuid);

            if (isSubscribed)
            {
                // Unsubscribe
                try
                {
                    UpdateStatus($"Unsubscribing from {characteristic.Uuid}...");
                    await characteristic.UnsubscribeAsync();
                    _subscribedCharacteristics.Remove(characteristic.Uuid);
                    Debug.Log($"[BleScanner] Unsubscribed from {characteristic.Uuid}");
                    UpdateStatus($"Unsubscribed");

                    // Update button appearance
                    UpdateNotifyButton(buttonGo, false);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Unsubscribe error: {e.Message}");
                    UpdateStatus($"Unsubscribe failed: {e.Message}");
                }
            }
            else
            {
                // Subscribe
                try
                {
                    UpdateStatus($"Subscribing to {characteristic.Uuid}...");
                    await characteristic.SubscribeAsync(data =>
                    {
                        // This callback may fire on a non-Unity thread (background delivery).
                        // Debug.Log is thread-safe, but UI updates must be dispatched to the main thread.
                        var hex = BitConverter.ToString(data).Replace("-", " ");
                        Debug.Log($"[BleScanner] Notify from {characteristic.Uuid}: [{hex}]");
                        MainThreadDispatcher.Enqueue(() => UpdateStatus($"Notify: {hex}"));
                    });
                    _subscribedCharacteristics.Add(characteristic.Uuid);
                    Debug.Log($"[BleScanner] Subscribed to {characteristic.Uuid}");
                    UpdateStatus($"Subscribed to notifications");

                    // Update button appearance
                    UpdateNotifyButton(buttonGo, true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Subscribe error: {e.Message}");
                    UpdateStatus($"Subscribe failed: {e.Message}");
                }
            }
        }

        private void UpdateNotifyButton(GameObject buttonGo, bool isSubscribed)
        {
            var image = buttonGo.GetComponent<Image>();
            var text = buttonGo.GetComponentInChildren<Text>();

            if (isSubscribed)
            {
                image.color = new Color(0.9f, 0.6f, 0.6f); // Red for unsubscribe
                text.text = "Unsub";
            }
            else
            {
                image.color = new Color(0.6f, 0.7f, 0.9f); // Blue for subscribe
                text.text = "Notify";
            }
        }

        private void CreateDisconnectButton()
        {
            if (deviceListContent == null || _connectedDevice == null) return;

            var containerGo = new GameObject("DisconnectContainer");
            containerGo.transform.SetParent(deviceListContent, false);
            containerGo.transform.SetAsFirstSibling();
            _disconnectButton = containerGo;
            _characteristicButtons.Add(containerGo);

            var containerLayout = containerGo.AddComponent<HorizontalLayoutGroup>();
            containerLayout.spacing = 15;
            containerLayout.childForceExpandWidth = false;
            containerLayout.childForceExpandHeight = true;
            containerLayout.padding = new RectOffset(15, 15, 10, 10);

            var containerLayoutElement = containerGo.AddComponent<LayoutElement>();
            containerLayoutElement.minHeight = 110;

            var containerImage = containerGo.AddComponent<Image>();
            containerImage.color = new Color(0.85f, 0.85f, 0.85f, 1f);

            // Device name label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(containerGo.transform, false);
            var labelText = labelGo.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 32;
            labelText.color = Color.black;
            labelText.text = $"Connected: {_connectedDevice.Name}";
            var labelLayout = labelGo.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1;

            // Disconnect button
            CreateActionButton(containerGo.transform, "Disconnect", new Color(0.9f, 0.5f, 0.5f),
                () => OnDisconnectClicked());
        }

        private async void OnDisconnectClicked()
        {
            if (_connectedDevice == null) return;

            try
            {
                UpdateStatus($"Disconnecting from {_connectedDevice.Name}...");
                await _connectedDevice.DisconnectAsync();
                Debug.Log($"[BleScanner] Disconnected from {_connectedDevice.Name}");
                UpdateStatus("Disconnected");

                _connectedDevice = null;
                ClearCharacteristicButtons();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Disconnect error: {e.Message}");
                UpdateStatus($"Disconnect failed: {e.Message}");
            }
        }
    }
}
