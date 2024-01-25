﻿using System.Windows;
using System.Reflection;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using System.IO;

namespace SimpleLauncher
{
    public partial class BugReport : Window
    {
        private static readonly HttpClient _httpClient = new();

        public BugReport()
        {
            InitializeComponent();
            DataContext = this; // Set the data context for data binding
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public static string ApplicationVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return "Version: " + (version?.ToString() ?? "Unknown");
            }
        }

        private async void SendBugReport_Click(object sender, RoutedEventArgs e)
        {
            string bugReportText = BugReportTextBox.Text;

            if (string.IsNullOrWhiteSpace(bugReportText))
            {
                MessageBox.Show("Please enter the details of the bug.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await SendBugReportToApiAsync(bugReportText);
        }

        public async Task SendBugReportToApiAsync(string bugReportText)
        {
            // Append the application version to the bug report
            string messageWithVersion = bugReportText + Environment.NewLine + Environment.NewLine + ApplicationVersion;

            // Prepare the POST data with the bug report text and application version
            var formData = new MultipartFormDataContent
    {
        { new StringContent("contact@purelogiccode.com"), "recipient" },
        { new StringContent("Bug Report from SimpleLauncher"), "subject" },
        { new StringContent("SimpleLauncher User"), "name" },
        { new StringContent(messageWithVersion), "message" }
    };

            // Set the API Key
            if (!_httpClient.DefaultRequestHeaders.Contains("X-API-KEY"))
            {
                _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e");
            }

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync("https://purelogiccode.com/simplelauncher/send_email.php", formData);

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Bug report sent successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    BugReportTextBox.Clear();
                }
                else
                {
                    string errorMessage = "An error occurred while sending the bug report.";
                    Task logTask = LogErrors.LogErrorAsync(new FileNotFoundException(errorMessage), "An error occurred while sending the bug report.");
                    MessageBox.Show("An error occurred while sending the bug report.", "Error", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await LogErrors.LogErrorAsync(ex, "Error sending bug report from Bug Report Window");
                MessageBox.Show($"An error occurred while sending the bug report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

        }

    }
}
