﻿using System;
using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Reflection;

namespace SimpleLauncher;

public partial class About
{
    public About()
    {
        InitializeComponent();

        // Apply theme
        App.ApplyThemeToWindow(this);
            
        // Set the data context for data binding
        DataContext = this;
            
        // Set the AppVersionTextBlock 
        AppVersionTextBlock.Text = ApplicationVersion;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private async void CheckForUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await UpdateChecker.CheckForUpdatesAsync2(this);
        }
        catch (Exception ex)
        {
            // Notify developer
            string formattedException = $"Error in the CheckForUpdate_Click method.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);
        }
    }

    private static string ApplicationVersion
    {
        get
        {
            string version2 = (string)Application.Current.TryFindResource("Version") ?? "Version:";
            string unknown2 = (string)Application.Current.TryFindResource("Unknown") ?? "Unknown";
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"{version2} " + (version?.ToString() ?? unknown2);
        }
    }

    private void UpdateHistory_Click(object sender, RoutedEventArgs e)
    {
        UpdateHistory updateHistoryWindow = new UpdateHistory();
        updateHistoryWindow.ShowDialog();
    }
}