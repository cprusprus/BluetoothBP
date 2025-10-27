using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using System.Diagnostics;

namespace Bluetooth
{
    public class BluetoothDevice
    {
        public BluetoothDevice()
        {
            m_bluetoothLE = CrossBluetoothLE.Current;
            m_adapter = CrossBluetoothLE.Current.Adapter;            
        }

        public async Task RequestBluetoothPermissionsAsync()
        {
            // Check and request Android Bluetooth permissions
            var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
            if (status == PermissionStatus.Granted)
            {
                return;
            }

            status = await Permissions.RequestAsync<Permissions.Bluetooth>();
            if (status != PermissionStatus.Granted)
            {
                throw new Exception("Bluetooth permissions are not granted");
            }
        }

        public async Task ConnectAsync(string deviceName)
        {
            if (!m_bluetoothLE.IsOn)
            {
                throw new Exception("Bluetooth is not enabled");
            }

            // Scan for devices
            m_device = await FindDeviceAsync(deviceName);
            if (m_device == null)
            {
                throw new Exception($"Device with name containing '{deviceName}' not found");
            }

            // Connect to the device
            await m_adapter.ConnectToDeviceAsync(m_device);
        }

        public async Task DisconnectAsync()
        {
            if (m_device != null)
            {
                await m_adapter.DisconnectDeviceAsync(m_device);
                m_device.Dispose();
                m_device = null;
            }
        }

        public async Task<IService> GetServiceAsync(Guid uuid)
        {
            if (m_device == null)
            {
                throw new Exception($"Device with UUID '{uuid}' not found");
            }

            IService service = await m_device.GetServiceAsync(uuid);
            if (service == null)
            {
                throw new Exception($"Service with UUID '{uuid}' not found");
            }

            return service;
        }

        public async Task<IService> GetServiceFromIdAsync(ushort id)
        {
            return await GetServiceAsync(ConvertShortIdToUuid(id));
        }

        public async Task<ICharacteristic> GetCharacteristicAsync(IService service, Guid uuid)
        {
            var characteristic = await service.GetCharacteristicAsync(uuid);
            if (characteristic == null)
            {
                throw new Exception($"Characteristic with UUID '{uuid}' not found in service '{service.Name}'");
            }

            return characteristic;
        }

        public async Task<ICharacteristic> GetCharacteristicFromShortIdAsync(IService service, ushort characteristicId)
        {
            return await GetCharacteristicAsync(service, ConvertShortIdToUuid(characteristicId));
        }

        public async Task RegisterForCharacteristicNotificationsAsync(ICharacteristic characteristic, Action<byte[]> onNotificationReceived)
        {
            if (!characteristic.CanUpdate)
            {
                throw new Exception("This characteristic does not support notifications");
            }

            characteristic.ValueUpdated += (s, e) =>
            {
                Debug.WriteLine($"Notification received: {BitConverter.ToString(e.Characteristic.Value)}");
                onNotificationReceived?.Invoke(e.Characteristic.Value);
            };

            await characteristic.StartUpdatesAsync();
        }

        public async Task UnregisterForCharacteristicNotificationsAsync(ICharacteristic characteristic)
        {
            if (!characteristic.CanUpdate)
            {
                throw new Exception("This characteristic does not support notifications");
            }

            await characteristic.StopUpdatesAsync();
            characteristic.ValueUpdated -= null;        // Unsubscribe from all handlers
        }

        private async Task<IDevice> FindDeviceAsync(string deviceName)
        {
            IDevice foundDevice = null;
            TaskCompletionSource m_deviceDiscoveredCompletionSource = new TaskCompletionSource();

            m_adapter.DeviceDiscovered += (s, a) =>
            {
                Debug.WriteLine($"Device discovered: {a.Device.Name}");
                foundDevice = a.Device;
                m_deviceDiscoveredCompletionSource.TrySetResult();
            };

            Func<IDevice, bool> deviceFilter = device => device.Name == deviceName;
            await m_adapter.StartScanningForDevicesAsync(deviceFilter: deviceFilter);
            await m_deviceDiscoveredCompletionSource.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await m_adapter.StopScanningForDevicesAsync();            

            return foundDevice;
        }

        // Convert Bluetooth ShortId to UUID
        private Guid ConvertShortIdToUuid(ushort shortId)
        {
            // Bluetooth assigned number + Bluetooth_Base_UUID: https://www.bluetooth.com/specifications/specs/core-specification-5-0
            return new Guid($"0000{shortId:X4}-0000-1000-8000-00805F9B34FB");
        }

        private readonly IAdapter m_adapter;
        private readonly IBluetoothLE m_bluetoothLE;
        private IDevice m_device;        
    }
}
