using Microsoft.UI.Xaml;
using Microsoft.VisualBasic;
using System;
using System.Threading.Tasks;
using Windows.Media.AppBroadcasting;

namespace BpMon
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            m_BloodPressureMonitor = new Bluetooth.BloodPressureMonitor();
        }

        private async void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (m_firstActivaton)
            {
                m_firstActivaton = false;
                StartButton.IsEnabled = true;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            MeasureBloodPresure();
        }

        private void MeasureBloodPresure()
        {
            if (m_IsRunning)
            {
                StartButton.IsEnabled = false;      // Prevent multiple button presses
                CancelBloodPressureMonitorInflation();
                StartButton.Content = "Start";
                m_IsRunning = false;
                StartButton.IsEnabled = true;
            }
            else
            {
                StartButton.IsEnabled = false;
                m_IsRunning = true;
                StartButton.Content = "Cancel";
                StartBloodPressureMonitorInflation();
                StartButton.IsEnabled = true;
            }
        }

        private async void StartBloodPressureMonitorInflation()
        {
            string bloodPressureReading = await m_BloodPressureMonitor.GetBloodPressureReadingAsync();
            ReadingsTextBox.Text = bloodPressureReading;
            m_batteryPercentageRemaining = await m_BloodPressureMonitor.GetBatteryLevelAsync();
            BatteryTextBlock.Text = m_batteryPercentageRemaining.ToString() + "%";
            m_BloodPressureMonitor.Disconnect();
            await UploadBpCsvToDropboxAsync();
            StartButton.Content = "Start";
            m_IsRunning = false;
        }

        private async void CancelBloodPressureMonitorInflation()
        {
            string result = await m_BloodPressureMonitor.CancelBloodPressureReadingAsync();
            ReadingsTextBox.Text = result;
            m_BloodPressureMonitor.Disconnect();
            StartButton.Content = "Start";
            m_IsRunning = false;
        }

        private async Task UploadBpCsvToDropboxAsync()
        {
            string[] headers = { "Date", "Systolic", "Diastolic", "Bpm" };
            string[][] rows = { new string[] { DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"), m_BloodPressureMonitor.m_bloodPressureReading.Systolic.ToString(), m_BloodPressureMonitor.m_bloodPressureReading.Diastolic.ToString(), m_BloodPressureMonitor.m_bloodPressureReading.PulseRate.ToString() } };
            var uploader = new DropboxUploader();
            string result = await uploader.UploadCsvFileAsync(headers, rows, m_dropboxFolderPath, m_dropboxBpFileName);
            if (result != "")
            {
                ReadingsTextBox.Text += "\n" + result;
            }
        }

        private bool m_firstActivaton = true;
        private bool m_IsRunning = false;
        private int m_batteryPercentageRemaining = 0;
        private Bluetooth.BloodPressureMonitor m_BloodPressureMonitor;
        string m_dropboxFolderPath = "";        // Dropbox will scope to /Apps/BpMon so no need to specify a subfolder
        string m_dropboxBpFileName = "bp.csv";
    }
}
