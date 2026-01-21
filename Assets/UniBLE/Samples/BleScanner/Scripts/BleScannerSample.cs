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

                if (deviceItemPrefab == null || deviceListContent == null)
                {
                    // No UI setup, just track the device count
                    _deviceItems[device.Id] = null;
                    UpdateStatus($"Scanning... Found {_deviceItems.Count} devices");
                    return;
                }

                var itemGo = Instantiate(deviceItemPrefab, deviceListContent);
                var item = itemGo.GetComponent<DeviceListItem>();
                item.Setup(device, OnDeviceSelected);
                _deviceItems[device.Id] = item;

                UpdateStatus($"Scanning... Found {_deviceItems.Count} devices");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[BleScanner] OnDeviceDiscovered exception: {e}");
            }
        }

        private void OnDeviceSelected(IBleDevice device)
        {
            Debug.Log($"Selected device: {device.Name} ({device.Id})");
            // You can implement connection logic here
        }

        private void ClearDeviceList()
        {
            foreach (var item in _deviceItems.Values)
            {
                Destroy(item.gameObject);
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
