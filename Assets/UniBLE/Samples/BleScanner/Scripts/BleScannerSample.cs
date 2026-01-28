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

                if (deviceItemPrefab != null && deviceListContent != null)
                {
                    var itemGo = Instantiate(deviceItemPrefab, deviceListContent);
                    var item = itemGo.GetComponent<DeviceListItem>();
                    item.Setup(device, OnDeviceSelected);
                    _deviceItems[device.Id] = item;
                }
                else
                {
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
