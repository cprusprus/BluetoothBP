namespace BpMonMaui
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            m_bloodPressureMonitor = new Bluetooth.BloodPressureMonitor();
        }

        private async void StartButton_Clicked(object sender, EventArgs e)
        {
            if (m_isRunning)
            {
                await CancelBloodPressureMonitorInflation();
            }
            else
            {
                await StartBloodPressureMonitorInflation();
            }
        }

        private async Task StartBloodPressureMonitorInflation()
        {
            m_isRunning = true;
            StartButton.Text = "Cancel";
            ReadingsEditor.Text = "";

            try
            {
                m_batteryPercentageRemaining = await m_bloodPressureMonitor.GetBatteryLevelAsync();
                BatteryLabel.Text = $"{m_batteryPercentageRemaining}%";

                ///string bloodPressureReading = await m_bloodPressureMonitor.GetBloodPressureReadingAsync();
                ///ReadingsEditor.Text = bloodPressureReading;
                ReadingsEditor.Text = "";///

                await m_bloodPressureMonitor.DisconnectAsync();
                await UploadBpCsvToDropboxAsync();
            }
            catch (TimeoutException ex)
            {
                ReadingsEditor.Text += $"Can't connect to blodd pressure monitor. Ensure it has charged batteries and close and reopen the cuff to ensure it is on: {ex.Message}";
            }
            catch (Exception ex)
            {
                ReadingsEditor.Text += $"Error: {ex.Message}";
            }
            finally
            {
                m_isRunning = false;
                StartButton.Text = "Start";
            }
        }

        private async Task CancelBloodPressureMonitorInflation()
        {
            try
            {
                string result = await m_bloodPressureMonitor.CancelBloodPressureReadingAsync();
                ReadingsEditor.Text = result;
                await m_bloodPressureMonitor.DisconnectAsync();
            }
            catch (Exception ex)
            {
                ReadingsEditor.Text += $"Error: {ex.Message}";
            }
            finally
            {
                m_isRunning = false;
                StartButton.Text = "Start";
            }
        }

        private async Task UploadBpCsvToDropboxAsync()
        {
            string[] headers = { "Date", "Systolic", "Diastolic", "Bpm" };
            string[][] rows = {
                new string[] {
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"),
                    m_bloodPressureMonitor.m_bloodPressureReading.Systolic.ToString(),
                    m_bloodPressureMonitor.m_bloodPressureReading.Diastolic.ToString(),
                    m_bloodPressureMonitor.m_bloodPressureReading.PulseRate.ToString()
                }
            };

            var uploader = new DropboxUploader();
            string result = await uploader.UploadCsvFileAsync(headers, rows, m_dropboxFolderPath, m_dropboxBpFileName);
            if (!string.IsNullOrEmpty(result))
            {
                ReadingsEditor.Text += $"\n{result}";
            }
        }

        private bool m_isRunning = false;
        private int m_batteryPercentageRemaining = 0;
        private Bluetooth.BloodPressureMonitor m_bloodPressureMonitor;
        string m_dropboxFolderPath = "";        // Dropbox will scope to /Apps/BpMon so no need to specify a subfolder
        string m_dropboxBpFileName = "bp.csv";
    }
}
