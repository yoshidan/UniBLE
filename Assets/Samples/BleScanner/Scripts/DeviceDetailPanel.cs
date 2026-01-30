using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UniBLE.Samples
{
    /// <summary>
    /// Panel that shows device details: connect/disconnect and characteristic list
    /// </summary>
    public class DeviceDetailPanel : MonoBehaviour
    {
        [SerializeField] private Text deviceNameText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button backButton;
        [SerializeField] private Button connectButton;
        [SerializeField] private Text connectButtonText;
        [SerializeField] private Transform characteristicListContent;

        private IBleDevice _device;
        private Action _onBack;
        private bool _connected;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly HashSet<BleUuid> _subscribedUuids = new HashSet<BleUuid>();

        public void Setup(IBleDevice device, Action onBack)
        {
            _device = device;
            _onBack = onBack;
            _connected = false;

            deviceNameText.text = $"{device.Name}\n<size=24><color=#aaaaaa>{device.Id}</color></size>";
            UpdateStatus("Tap Connect to start");
            UpdateConnectButton();
            ClearRows();

            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
            connectButton.onClick.RemoveAllListeners();
            connectButton.onClick.AddListener(OnConnectClicked);
        }

        private void OnBackClicked()
        {
            if (_connected)
            {
                DisconnectAndBack();
            }
            else
            {
                _onBack?.Invoke();
            }
        }

        private async void DisconnectAndBack()
        {
            try
            {
                UpdateStatus("Disconnecting...");
                await _device.DisconnectAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Disconnect error: {e.Message}");
            }
            _connected = false;
            ClearRows();
            _onBack?.Invoke();
        }

        private async void OnConnectClicked()
        {
            if (_connected)
            {
                // Disconnect
                try
                {
                    connectButton.interactable = false;
                    UpdateStatus("Disconnecting...");
                    await _device.DisconnectAsync();
                    _connected = false;
                    ClearRows();
                    UpdateConnectButton();
                    UpdateStatus("Disconnected");
                    Debug.Log($"[BleScanner] Disconnected from {_device.Name}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Disconnect error: {e.Message}");
                    UpdateStatus($"Disconnect failed: {e.Message}");
                }
                finally
                {
                    connectButton.interactable = true;
                }
                return;
            }

            // Connect
            try
            {
                connectButton.interactable = false;
                UpdateStatus("Connecting...");
                await _device.ConnectAsync();
                _connected = true;
                UpdateConnectButton();
                Debug.Log($"[BleScanner] Connected to {_device.Name}");

                UpdateStatus("Discovering services...");
                var services = await _device.GetServicesAsync();

                int totalChars = 0;
                foreach (var service in services)
                {
                    var characteristics = await service.GetCharacteristicsAsync();
                    totalChars += characteristics.Count;

                    foreach (var c in characteristics)
                    {
                        CreateCharacteristicRow(service, c);
                    }
                }
                UpdateStatus($"{services.Count} services, {totalChars} characteristics");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Connection error: {e.Message}");
                UpdateStatus($"Error: {e.Message}");
                _connected = false;
                UpdateConnectButton();
            }
            finally
            {
                connectButton.interactable = true;
            }
        }

        private void CreateCharacteristicRow(IBleService service, IBleCharacteristic c)
        {
            var props = c.Properties;
            bool hasRead = props.HasFlag(BleCharacteristicProperties.Read);
            bool hasWrite = props.HasFlag(BleCharacteristicProperties.Write) ||
                            props.HasFlag(BleCharacteristicProperties.WriteWithoutResponse);
            bool hasNotify = props.HasFlag(BleCharacteristicProperties.Notify) ||
                             props.HasFlag(BleCharacteristicProperties.Indicate);

            // Row container - horizontal: [UUID info (left)] [buttons (right)]
            var rowGo = new GameObject($"Char_{c.Uuid}");
            rowGo.transform.SetParent(characteristicListContent, false);
            _rows.Add(rowGo);

            var rowImage = rowGo.AddComponent<Image>();
            rowImage.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(15, 15, 10, 10);
            rowLayout.spacing = 10;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.minHeight = 110;
            rowLe.preferredHeight = 110;

            // Info column (flexible width, pushes buttons to right)
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childControlWidth = true;
            infoVlg.childControlHeight = true;
            infoVlg.childForceExpandWidth = true;
            infoVlg.childForceExpandHeight = false;
            var infoLe = infoGo.AddComponent<LayoutElement>();
            infoLe.flexibleWidth = 1;

            // UUID label
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(infoGo.transform, false);
            labelGo.AddComponent<RectTransform>();
            var labelText = labelGo.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 22;
            labelText.color = Color.white;
            labelText.text = c.Uuid.ToString();

            // Properties label
            var propsGo = new GameObject("Props");
            propsGo.transform.SetParent(infoGo.transform, false);
            propsGo.AddComponent<RectTransform>();
            var propsText = propsGo.AddComponent<Text>();
            propsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            propsText.fontSize = 20;
            propsText.color = Color.gray;
            var propsList = new List<string>();
            if (hasRead) propsList.Add("Read");
            if (hasWrite) propsList.Add("Write");
            if (hasNotify) propsList.Add("Notify");
            propsText.text = string.Join(" / ", propsList);

            // Buttons on the right
            if (hasRead)
                CreateSmallButton(rowGo.transform, "Read", new Color(0.4f, 0.7f, 0.4f), () => OnRead(c));
            if (hasWrite)
                CreateSmallButton(rowGo.transform, "Write", new Color(0.7f, 0.7f, 0.4f), () => OnWrite(c));
            if (hasNotify)
            {
                var notifyBtnGo = CreateSmallButton(rowGo.transform, "Notify", new Color(0.4f, 0.5f, 0.8f), null);
                var notifyBtn = notifyBtnGo.GetComponent<Button>();
                notifyBtn.onClick.AddListener(() => OnNotify(c, notifyBtnGo));
            }
        }

        private GameObject CreateSmallButton(Transform parent, string label, Color color, Action onClick)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var image = go.AddComponent<Image>();
            image.color = color;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = image;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 130;
            le.preferredWidth = 130;
            le.minHeight = 70;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return go;
        }

        private async void OnRead(IBleCharacteristic c)
        {
            try
            {
                UpdateStatus($"Reading...");
                var data = await c.ReadAsync();
                var hex = BitConverter.ToString(data).Replace("-", " ");
                Debug.Log($"[BleScanner] Read {c.Uuid}: [{hex}]");
                UpdateStatus($"Read: {hex}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Read error: {e.Message}");
                UpdateStatus($"Read failed: {e.Message}");
            }
        }

        private async void OnWrite(IBleCharacteristic c)
        {
            try
            {
                var unixSeconds = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var data = BitConverter.GetBytes(unixSeconds);
                UpdateStatus($"Writing timestamp {unixSeconds}...");
                await c.WriteAsync(data);
                Debug.Log($"[BleScanner] Write to {c.Uuid} succeeded");
                UpdateStatus($"Write OK: {unixSeconds}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BleScanner] Write error: {e.Message}");
                UpdateStatus($"Write failed: {e.Message}");
            }
        }

        private async void OnNotify(IBleCharacteristic c, GameObject btnGo)
        {
            var isSubscribed = _subscribedUuids.Contains(c.Uuid);
            if (isSubscribed)
            {
                try
                {
                    UpdateStatus("Unsubscribing...");
                    await c.UnsubscribeAsync();
                    _subscribedUuids.Remove(c.Uuid);
                    UpdateNotifyButton(btnGo, false);
                    UpdateStatus("Unsubscribed");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Unsubscribe error: {e.Message}");
                    UpdateStatus($"Unsubscribe failed: {e.Message}");
                }
            }
            else
            {
                try
                {
                    UpdateStatus("Subscribing...");
                    await c.SubscribeAsync(data =>
                    {
                        var hex = BitConverter.ToString(data).Replace("-", " ");
                        Debug.Log($"[BleScanner] Notify {c.Uuid}: [{hex}]");
                        MainThreadDispatcher.Enqueue(() => UpdateStatus($"Notify: {hex}"));
                    });
                    _subscribedUuids.Add(c.Uuid);
                    UpdateNotifyButton(btnGo, true);
                    UpdateStatus("Subscribed");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[BleScanner] Subscribe error: {e.Message}");
                    UpdateStatus($"Subscribe failed: {e.Message}");
                }
            }
        }

        private void UpdateNotifyButton(GameObject btnGo, bool subscribed)
        {
            var image = btnGo.GetComponent<Image>();
            var text = btnGo.GetComponentInChildren<Text>();
            if (subscribed)
            {
                image.color = new Color(0.8f, 0.4f, 0.4f);
                text.text = "Unsub";
            }
            else
            {
                image.color = new Color(0.4f, 0.5f, 0.8f);
                text.text = "Notify";
            }
        }

        private void UpdateConnectButton()
        {
            if (_connected)
            {
                connectButtonText.text = "Disconnect";
                connectButton.GetComponent<Image>().color = new Color(0.9f, 0.5f, 0.5f);
            }
            else
            {
                connectButtonText.text = "Connect";
                connectButton.GetComponent<Image>().color = new Color(0.3f, 0.7f, 0.3f);
            }
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            Debug.Log($"[BleScanner] {message}");
        }

        private void ClearRows()
        {
            foreach (var go in _rows) Destroy(go);
            _rows.Clear();
            _subscribedUuids.Clear();
        }
    }
}
