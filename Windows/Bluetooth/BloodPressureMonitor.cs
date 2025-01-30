using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace BpMon.Bluetooth
{
    internal struct BloodPressureReading
    {
        public ushort Systolic;             // mmHg
        public ushort Diastolic;            // mmHg
        public ushort ArterialPressure;     // mmHg
        public ushort PulseRate;            // bpm
    }

    // WireShark filter: _ws.col.def_src contains "Qardio_03:11:be" or _ws.col.def_dst contains "Qardio_03:11:be"
    // or: bluetooth
    // or: btatt
    internal class BloodPressureMonitor
    {
        public BloodPressureMonitor()
        {
            m_device = new BluetoothDevice();
            m_bloodPressureReadingCompletionSource = new TaskCompletionSource<bool>();
            m_bloodPressureReading = default(BloodPressureReading);
        }

        // Connects to Bluetooth scale and saves off relevant Bluetooth services and characteristics. Bluetooth connection is active as long as there is an outstanding reference to services/characteristics
        // Must pair first with Bluetooth device in Settings -> Bluetooth & devices -> Devices -> Add device -> Bluetooth -> Show all devices -> QuardioArm -> Pair
        public async Task ConnectAsync()
        {
            // Get Bluetooth battery service and characteristic
            m_batteryService = await m_device.GetServiceFromId(BatteryServiceId, BatteryServiceName);
            m_batteryCharacteristic = m_device.GetCharacteristic(m_batteryService, 0);        // Only one characteristic in battery service

            // Get Bluetooth blood pressure service and characteristics
            m_bloodPressureService = await m_device.GetServiceFromId(BloodPressureServiceId, BloodPressureServiceName);
            //var bloodPressureCharacteristics = m_bloodPressureService.GetAllCharacteristics();      // Debugging
            //foreach (var characteristic in bloodPressureCharacteristics)
            //{
            //    // Properties: Read (0x2), Write (0x8), Notify (0x10), Indicate (0x20)
            //    // ProtectionLevel: Plain (0x0), AuthenticationRequired (0x1), EncryptionRequired (0x2), EncryptionAndAuthenticationRequired (0x3)
            //    System.Diagnostics.Debug.WriteLine($"Uuid: {characteristic.Uuid}, AttributeHandle: {characteristic.AttributeHandle}, CharacteristicProperties: {characteristic.CharacteristicProperties}, ProtectionLevel: {characteristic.ProtectionLevel}");
            //}
            m_bloodPressureMeasurementCharacteristic = m_device.GetCharacteristicFromShortId(m_bloodPressureService, BloodPressureMeasurementCharacteristicId);
            m_bloodPressureFeatureCharacteristic = m_device.GetCharacteristic(m_bloodPressureService, BloodPressureFeatureCharacteristicGuid);
            m_connected = true;
        }

        public void Disconnect()
        {
            // Disconnect from Bluetooth device
            m_device.UnregisterForCharacteristicNotifications(m_bloodPressureMeasurementCharacteristic, BloodPressureCharacteristic_ValueChanged);

            m_bloodPressureService?.Dispose();
            m_bloodPressureService = null;
            m_bloodPressureMeasurementCharacteristic = null;
            m_bloodPressureFeatureCharacteristic = null;

            m_batteryService?.Dispose();
            m_batteryService = null;
            m_batteryCharacteristic = null;
            m_connected = false;
        }

        public async Task<int> GetBatteryLevelAsync()
        {
            // Get battery percent remaining for blood pressure monitor
            var batteryLevelBytes = await m_device.ReadCharacteristic(m_batteryCharacteristic);
            if (batteryLevelBytes == null)
            {
                return 0;
            }

            return batteryLevelBytes[0];
        }

        public async Task<string> GetBloodPressureReadingAsync()
        {
            if (!m_connected)
            {
                await ConnectAsync();
            }
            await RegisterForBloodPressureNotificationsAsync();

            var result = await m_device.WriteToCharacteristic(m_bloodPressureFeatureCharacteristic, BitConverter.GetBytes(BloodPressureFeatureStartReadingValue));
            if (result != GattCommunicationStatus.Success)
            {
                // Check that monitor is still paired, monitor is not too far from device. Try closing and reopening monitor flap. Try re-pairing monitor
                return "Disconnected";
            }

            await m_bloodPressureReadingCompletionSource.Task;

            // Wait 3s for last blood pressure reading to complete
            await Task.Delay(3000);
            return m_bloodPressureReadingStr;
        }

        private async Task RegisterForBloodPressureNotificationsAsync()
        {
            await m_device.RegisterForCharacteristicNotificationsAsync(m_bloodPressureMeasurementCharacteristic, BloodPressureCharacteristic_ValueChanged);
        }

        private void BloodPressureCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // Blood pressure reading format is (bytes): Flags (1), Systolic (2), Diastolic (2), Arterial Pressure(2), Pulse Rate (2), Measurement Status (2)
            // We don't care about flags for now (Measurement status [T/F], pulse rate [T/F], Unit: mmHg vs kPA) - mmHg assumed
            // Measurement status reports extra info such as incorrect readings (improper measurement position, pulse rate range detection [in range or not], irregular pulse, cuff fit too loose, body movement). Ignore for now
            var bytes = m_device.ReadCharacteristicNotificationValue(args);
            if (bytes.Length > 7)
            {
                // Qardio only gives systolic readings while cuff is inflating and BP reading in progress. Once we have a pulse rate, the reading is complete
                m_bloodPressureReading.Systolic = BitConverter.ToUInt16(bytes, 1);
                m_bloodPressureReading.Diastolic = BitConverter.ToUInt16(bytes, 3);
                m_bloodPressureReading.ArterialPressure = BitConverter.ToUInt16(bytes, 5);
                m_bloodPressureReading.PulseRate = BitConverter.ToUInt16(bytes, 7);
                m_bloodPressureReadingStr = $"Systolic: {m_bloodPressureReading.Systolic} mmHg \nDiastolic: {m_bloodPressureReading.Diastolic} mmHg \nArterial Pressure: {m_bloodPressureReading.ArterialPressure} mmHg \nPulse Rate: {m_bloodPressureReading.PulseRate} bpm";
                m_bloodPressureReadingCompletionSource.TrySetResult(true);
            }
        }

        public async Task<string> CancelBloodPressureReadingAsync()
        {
            var result = await m_device.WriteToCharacteristic(m_bloodPressureFeatureCharacteristic, BitConverter.GetBytes(BloodPressureFeatureCancelReadingValue));
            m_device.UnregisterForCharacteristicNotifications(m_bloodPressureMeasurementCharacteristic, BloodPressureCharacteristic_ValueChanged);
            if (result != GattCommunicationStatus.Success)
            {
                return "Disconnected";
            }
            return "Canceled";
        }

        public BloodPressureReading m_bloodPressureReading;
        private string m_bloodPressureReadingStr = "";

        private readonly ushort BatteryServiceId = 0x180F;
        private readonly string BatteryServiceName = "Battery";
        private GattDeviceService m_batteryService;
        private GattCharacteristic m_batteryCharacteristic;

        private readonly ushort BloodPressureServiceId = 0x1810;
        private readonly string BloodPressureServiceName = "Bloody Pressure";       // Looks like Qardio named this service "Bloody Pressure" instead of "Blood Pressure" from the BLE spec
        private readonly ushort BloodPressureMeasurementCharacteristicId = 0x2a35;
        private readonly Guid BloodPressureFeatureCharacteristicGuid = Guid.Parse("583cb5b3-875d-40ed-9098-c39eb0c1983d");
        private readonly ushort BloodPressureFeatureStartReadingValue = 0x01f1;
        private readonly ushort BloodPressureFeatureCancelReadingValue = 0x02f1;
        private GattDeviceService m_bloodPressureService;
        private GattCharacteristic m_bloodPressureMeasurementCharacteristic;
        private GattCharacteristic m_bloodPressureFeatureCharacteristic;

        private bool m_connected = false;        
        private TaskCompletionSource<bool> m_bloodPressureReadingCompletionSource;
        private BluetoothDevice m_device;
    }
}
