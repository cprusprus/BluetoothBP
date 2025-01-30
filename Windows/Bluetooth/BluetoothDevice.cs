using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace BpMon.Bluetooth
{
    internal class BluetoothDevice
    {
        public async Task<GattDeviceService> GetServiceFromId(ushort id, string serviceName)
        {
            var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromShortId(id), null);
            return await GetServiceFromDeviceInfoCollection(services, serviceName);
        }

        public async Task<GattDeviceService> GetServiceFromUuid(Guid uuid, string serviceName)
        {
            var services = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(uuid), null);
            return await GetServiceFromDeviceInfoCollection(services, serviceName);
        }

        // Returns characteristic at index index in service's characteristic collection
        public GattCharacteristic GetCharacteristic(GattDeviceService service, int index)
        {
            var characteristics = service.GetAllCharacteristics();
            return characteristics[index];
        }

        // Returns characteristic with given UUID in service's characteristic collection
        public GattCharacteristic GetCharacteristic(GattDeviceService service, Guid characteristicUuid)
        {
            var characteristics = service.GetCharacteristics(characteristicUuid);
            if (characteristics.Count != 1)
            {
                throw new IndexOutOfRangeException($"Unexpected characteristic count: {characteristics.Count} for characteristic UUID: {characteristicUuid} in service: {service.Uuid}");
            }
            return characteristics[0];      // Assume there's only one characteristic with given UUID
        }

        // Returns characteristic with given short id (standard BLE characteristic) in service's characteristic collection
        public GattCharacteristic GetCharacteristicFromShortId(GattDeviceService service, ushort characteristicId)
        {
            return GetCharacteristic(service, ConvertShortIdToUuid(characteristicId));
        }

        // Read value from Bluetooth service characteristic. Returns null on service unreachable
        public async Task<byte[]> ReadCharacteristic(GattCharacteristic characteristic)
        {
            var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (result.Status == GattCommunicationStatus.Success)
            {
                byte[] bytes = new byte[result.Value.Length];
                DataReader.FromBuffer(result.Value).ReadBytes(bytes);

                return bytes;
            }

            return null;
        }

        // Write value to Bluetooth service characteristic
        public async Task<GattCommunicationStatus> WriteToCharacteristic(GattCharacteristic characteristic, byte[] value)
        {
            DataWriter writer = new DataWriter();
            writer.WriteBytes(value);
            IBuffer buffer = writer.DetachBuffer();
            var result = await characteristic.WriteValueAsync(buffer);
            return result;
        }

        public async Task RegisterForCharacteristicNotificationsAsync(GattCharacteristic characteristic, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> eventHandler)
        {
            var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (result == GattCommunicationStatus.Success)
            {
                // If service is still reachable, register for notifications
                characteristic.ValueChanged += eventHandler;
            }
        }

        public void UnregisterForCharacteristicNotifications(GattCharacteristic characteristic, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> eventHandler)
        {
            //var result = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
            //if (result == GattCommunicationStatus.Success)
            //{
            // If service is still reachable, unregister for notifications
            characteristic.ValueChanged -= eventHandler;
            //}
        }

        public byte[] ReadCharacteristicNotificationValue(GattValueChangedEventArgs args)
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            return bytes;
        }

        private async Task<GattDeviceService> GetServiceFromDeviceInfoCollection(DeviceInformationCollection services, string serviceName)
        {
            if (services == null || services.Count == 0)
            {
                throw new IndexOutOfRangeException("Services collection empty");
            }

            foreach (var service in services)
            {
                if (service.Name == serviceName)
                {
                    //if (!service.IsEnabled)
                    //{
                    //    throw new InvalidOperationException("Service " + service.Name + " is not enabled");
                    //}
                    return await GattDeviceService.FromIdAsync(service.Id);
                }
            }

            return null;
        }

        // Convert Bluetooth ShortId to UUID
        private Guid ConvertShortIdToUuid(ushort shortId)
        {
            // Bluetooth assigned number + Bluetooth_Base_UUID: https://www.bluetooth.com/specifications/specs/core-specification-5-0
            return new Guid($"0000{shortId:X4}-0000-1000-8000-00805F9B34FB");
        }
    }
}
