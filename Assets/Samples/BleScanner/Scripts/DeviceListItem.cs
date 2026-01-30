using System;
using UnityEngine;
using UnityEngine.UI;

namespace UniBLE.Samples
{
    /// <summary>
    /// UI component for displaying a BLE device in the list
    /// </summary>
    public class DeviceListItem : MonoBehaviour
    {
        [SerializeField] private Text deviceNameText;
        [SerializeField] private Text deviceIdText;
        [SerializeField] private Button selectButton;

        private IBleDevice _device;
        private Action<IBleDevice> _onSelected;

        public void Setup(IBleDevice device, Action<IBleDevice> onSelected)
        {
            _device = device;
            _onSelected = onSelected;

            var displayName = string.IsNullOrEmpty(device.Name) ? "Unknown Device" : device.Name;
            deviceNameText.text = displayName;
            deviceIdText.text = device.Id;

            selectButton.onClick.AddListener(OnSelectButtonClicked);
        }

        private void OnSelectButtonClicked()
        {
            _onSelected?.Invoke(_device);
        }

        private void OnDestroy()
        {
            selectButton.onClick.RemoveListener(OnSelectButtonClicked);
        }
    }
}
