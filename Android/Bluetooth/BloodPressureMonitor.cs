using Plugin.BLE.Abstractions.Contracts;
using System.Diagnostics;

// Package release build by first generating a keystore:
// & "C:\Program Files\Android\openjdk\jdk-21.0.8\bin\keytool.exe" -genkey -v -keystore filename.keystore -alias keystore.alias -keyalg RSA -keysize 2048 -validity 10000
// Then set singing properties: https://learn.microsoft.com/en-us/previous-versions/xamarin/android/deploy-test/building-apps/build-process#signing-properties
//   Edit BpMonMaui.csproj file and add:
//      <PropertyGroup>
//        <AndroidKeyStore>True</AndroidKeyStore>
//        <AndroidSigningKeyStore>filename.keystore</AndroidSigningKeyStore>
//        <AndroidSigningStorePass>password</AndroidSigningStorePass>
//        <AndroidSigningKeyAlias>keystore.alias</AndroidSigningKeyAlias>
//        <AndroidSigningKeyPass>password</AndroidSigningKeyPass>
//      </PropertyGroup>	
// Rebuild and Publish to generate apk
// Install with adb to phone connected to computer over USB (With Android Developer Mode enabled and USB Debugging enabled): adb.exe install com.companyname.bpmonmaui.apk (may need to also pass -s <PhoneSerial> from output of adb.exe devices if there's more than one or Android Emulator is installed)

namespace Bluetooth
{
    public struct BloodPressureReading
    {
        public ushort Systolic;             // mmHg
        public ushort Diastolic;            // mmHg
        public ushort ArterialPressure;     // mmHg
        public ushort PulseRate;            // bpm
    }

    // WireShark filter: _ws.col.def_src contains "Qardio_03:11:be" or _ws.col.def_dst contains "Qardio_03:11:be"
    // or: bluetooth
    // or: btatt
    public class BloodPressureMonitor
    {
        public BloodPressureMonitor()
        {
            m_bluetoothDevice = new BluetoothDevice();
            m_bloodPressureReadingCompletionSource = new TaskCompletionSource<bool>();
            m_bloodPressureReading = default(BloodPressureReading);
        }

        public async Task ConnectAsync()
        {
            await m_bluetoothDevice.RequestBluetoothPermissionsAsync();
            await m_bluetoothDevice.ConnectAsync(DeviceName);

            m_batteryService = await m_bluetoothDevice.GetServiceFromIdAsync(BatteryServiceId);
            m_batteryCharacteristic = await m_bluetoothDevice.GetCharacteristicAsync(m_batteryService, Guid.Parse(BatteryLevelCharacteristicUuid));

            m_bloodPressureService = await m_bluetoothDevice.GetServiceFromIdAsync(BloodPressureServiceId);
            m_bloodPressureMeasurementCharacteristic = await m_bluetoothDevice.GetCharacteristicFromShortIdAsync(m_bloodPressureService, BloodPressureMeasurementCharacteristicId);
            m_bloodPressureFeatureCharacteristic = await m_bluetoothDevice.GetCharacteristicAsync(m_bloodPressureService, BloodPressureFeatureCharacteristicGuid);
            m_connected = true;
        }

        public async Task DisconnectAsync()
        {
            await m_bluetoothDevice.UnregisterForCharacteristicNotificationsAsync(m_bloodPressureMeasurementCharacteristic);

            m_bloodPressureService?.Dispose();
            m_bloodPressureService = null;
            m_bloodPressureMeasurementCharacteristic = null;
            m_bloodPressureFeatureCharacteristic = null;

            m_batteryService?.Dispose();
            m_batteryService = null;
            m_batteryCharacteristic = null;

            await m_bluetoothDevice.DisconnectAsync();
            m_connected = false;
        }

        public async Task<int> GetBatteryLevelAsync()
        {
            if (!m_connected)
            {
                await ConnectAsync();
            }

            if (m_batteryCharacteristic == null)
            {
                throw new Exception("Not connected to a blood pressure monitor");
            }

            var (data, result) = await m_batteryCharacteristic.ReadAsync();
            if (result != 0)
            {
                throw new Exception($"Failed to read battery level: {result}");
            }
            return data[0];
        }

        public async Task<string> GetBloodPressureReadingAsync()
        {
            if (!m_connected)
            {
                await ConnectAsync();
            }

            if (m_bloodPressureMeasurementCharacteristic == null || m_bloodPressureFeatureCharacteristic == null)
            {
                throw new Exception("Not connected to a blood pressure monitor");
            }

            await RegisterForBloodPressureNotificationsAsync();

            var result = await m_bloodPressureFeatureCharacteristic.WriteAsync(BitConverter.GetBytes(BloodPressureFeatureStartReadingValue));
            if (result != 0)
            {
                // Check that monitor is still paired, monitor is not too far from device. Try closing and reopening monitor flap. Try re-pairing monitor
                throw new Exception($"Failed to write to blood pressure feature characteristic");
            }

            await m_bloodPressureReadingCompletionSource.Task;

            // Wait 3s for last blood pressure reading to complete
            await Task.Delay(3000);
            return m_bloodPressureReadingStr;
        }

        public async Task<string> CancelBloodPressureReadingAsync()
        {
            var result = await m_bloodPressureFeatureCharacteristic.WriteAsync(BitConverter.GetBytes(BloodPressureFeatureCancelReadingValue));
            await m_bluetoothDevice.UnregisterForCharacteristicNotificationsAsync(m_bloodPressureMeasurementCharacteristic);
            if (result != 0)
            {
                // Check that monitor is still paired, monitor is not too far from device. Try closing and reopening monitor flap. Try re-pairing monitor
                throw new Exception($"Failed to write to blood pressure feature characteristic");
            }

            return "Canceled";
        }

        private async Task RegisterForBloodPressureNotificationsAsync()
        {
            await m_bluetoothDevice.RegisterForCharacteristicNotificationsAsync(m_bloodPressureMeasurementCharacteristic, bytes =>
            {
                Debug.WriteLine($"Received data: {BitConverter.ToString(bytes)}");

                // Blood pressure reading format is (bytes): Flags (1), Systolic (2), Diastolic (2), Arterial Pressure(2), Pulse Rate (2), Measurement Status (2)
                // We don't care about flags for now (Measurement status [T/F], pulse rate [T/F], Unit: mmHg vs kPA) - mmHg assumed
                // Measurement status reports extra info such as incorrect readings (improper measurement position, pulse rate range detection [in range or not], irregular pulse, cuff fit too loose, body movement). Ignore for now
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
            });
        }

        public BloodPressureReading m_bloodPressureReading;
        private string m_bloodPressureReadingStr = "";
        
        private IService m_batteryService;
        private ICharacteristic m_batteryCharacteristic;
        private IService m_bloodPressureService;
        private ICharacteristic m_bloodPressureMeasurementCharacteristic;
        private ICharacteristic m_bloodPressureFeatureCharacteristic;

        private const string DeviceName = "QardioARM";
        private readonly ushort BatteryServiceId = 0x180F;
        private const string BatteryLevelCharacteristicUuid = "00002a19-0000-1000-8000-00805f9b34fb";

        private readonly ushort BloodPressureServiceId = 0x1810;
        private readonly ushort BloodPressureMeasurementCharacteristicId = 0x2a35;
        private readonly Guid BloodPressureFeatureCharacteristicGuid = Guid.Parse("583cb5b3-875d-40ed-9098-c39eb0c1983d");
        private readonly ushort BloodPressureFeatureStartReadingValue = 0x01f1;
        private readonly ushort BloodPressureFeatureCancelReadingValue = 0x02f1;

        private bool m_connected = false;
        private TaskCompletionSource<bool> m_bloodPressureReadingCompletionSource;
        private BluetoothDevice m_bluetoothDevice;
    }
}
