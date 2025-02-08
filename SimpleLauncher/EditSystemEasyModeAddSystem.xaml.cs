﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Navigation;
using System.Xml.Linq;
using Application = System.Windows.Application;

namespace SimpleLauncher;

public partial class EditSystemEasyModeAddSystem
{
    // Create instance of ExtractCompressedFile
    private readonly ExtractCompressedFile _extractCompressedFile = new();

    private EasyModeConfig _config;
    private bool _isEmulatorDownloaded;
    private bool _isCoreDownloaded;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly HttpClient _httpClient = new();
    private bool _isDownloadCompleted;
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), "SimpleLauncher");
        
    public EditSystemEasyModeAddSystem()
    {
        InitializeComponent();

        // Apply Theme
        App.ApplyThemeToWindow(this);

        // Load Config
        _config = EasyModeConfig.Load();
        
        PopulateSystemDropdown();
            
        // Subscribe to the Closed event
        Closed += EditSystemEasyModeAddSystem_Closed;

    }

    private void PopulateSystemDropdown()
    {
        if (_config?.Systems != null)
        {
            var sortedSystemNames = _config.Systems
                .Where(system => !string.IsNullOrEmpty(system.Emulators?.Emulator?.EmulatorDownloadLink)) // only if EmulatorDownloadLink is not null
                .Select(system => system.SystemName)
                .OrderBy(name => name) // order by name
                .ToList();

            SystemNameDropdown.ItemsSource = sortedSystemNames;
        }
    }

    private void SystemNameDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SystemNameDropdown.SelectedItem != null)
        {
            var selectedSystem = _config.Systems.FirstOrDefault(system => system.SystemName == SystemNameDropdown.SelectedItem.ToString());
            if (selectedSystem != null)
            {
                DownloadEmulatorButton.IsEnabled = true;
                DownloadCoreButton.IsEnabled = !string.IsNullOrEmpty(selectedSystem.Emulators.Emulator.CoreDownloadLink);
                DownloadExtrasButton.IsEnabled = !string.IsNullOrEmpty(selectedSystem.Emulators.Emulator.ExtrasDownloadLink);

                // Reset download status
                _isEmulatorDownloaded = false;
                _isCoreDownloaded = !DownloadCoreButton.IsEnabled;

                UpdateAddSystemButtonState();
            }
        }
    }
        
    private async void DownloadEmulatorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _isDownloadCompleted = false;
            _isEmulatorDownloaded = false;
            DownloadEmulatorButton.IsEnabled = true;
            UpdateAddSystemButtonState();
    
            var selectedSystem = _config.Systems.FirstOrDefault(system => system.SystemName == SystemNameDropdown.SelectedItem.ToString());
            if (selectedSystem != null)
            {
                // string emulatorLocation = selectedSystem.Emulators.Emulator.EmulatorLocation;
                string emulatorDownloadUrl = selectedSystem.Emulators.Emulator.EmulatorDownloadLink;
                string emulatorsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emulators");
                Directory.CreateDirectory(emulatorsFolderPath);
                string downloadFilePath = Path.Combine(_tempFolder, Path.GetFileName(emulatorDownloadUrl) ?? throw new InvalidOperationException("Simple Launcher could not get emulatorDownloadUrl"));
                Directory.CreateDirectory(_tempFolder);
                string destinationPath = selectedSystem.Emulators.Emulator.EmulatorDownloadExtractPath;
                // string latestVersionString = selectedSystem.Emulators.Emulator.EmulatorLatestVersion;

                try
                {
                    // Display progress bar
                    DownloadProgressBar.Visibility = Visibility.Visible;
                    DownloadProgressBar.Value = 0;
                    StopDownloadButton.IsEnabled = true;

                    // Initialize cancellation token source
                    _cancellationTokenSource = new CancellationTokenSource();

                    await DownloadWithProgressAsync(emulatorDownloadUrl, downloadFilePath, _cancellationTokenSource.Token);

                    if (_isDownloadCompleted)
                    {
                        // Rename the file to .7z if EmulatorDownloadRename is true
                        downloadFilePath = ChangeFileExtensionFunction(selectedSystem, downloadFilePath);

                        // Show the PleaseWaitExtraction window
                        PleaseWaitExtraction pleaseWaitWindow = new PleaseWaitExtraction();
                        pleaseWaitWindow.Show();

                        bool extractionSuccess = await _extractCompressedFile
                            .ExtractDownloadFilesAsync2(downloadFilePath, destinationPath);
                        
                        // Close the PleaseWaitExtraction window
                        pleaseWaitWindow.Close();

                        if (extractionSuccess)
                        {
                            // Notify user
                            MessageBoxLibrary.DownloadAndExtrationWereSuccessfulMessageBox();

                            // Clean up the downloaded file only if extraction is successful
                            DeleteDownloadFilePath(downloadFilePath);
                           
                            // Mark as downloaded and disable button
                            _isEmulatorDownloaded = true;
                            DownloadEmulatorButton.IsEnabled = false;
                            
                            // Update AddSystemButton state
                            UpdateAddSystemButtonState();
                        }
                        else // extraction fail
                        {
                            // Notify developer
                            string formattedException = $"Emulator extraction failed.\n\n" +
                                                        $"File: {downloadFilePath}";
                            Exception ex = new Exception(formattedException);
                            await LogErrors.LogErrorAsync(ex, formattedException);

                            // Notify user
                            MessageBoxLibrary.ExtractionFailedMessageBox();
                        }
                    }
                    else // download fail
                    {
                        // Notify developer
                        string formattedException = $"Emulator download failed.\n\n" +
                                                    $"File: {downloadFilePath}";
                        Exception ex = new Exception(formattedException);
                        await LogErrors.LogErrorAsync(ex, formattedException);

                        // Notify user
                        MessageBoxLibrary.DownloadFailedMessageBox();
                    }
                }
                catch (TaskCanceledException) // Download canceled
                {
                    DeleteDownloadFilePath(downloadFilePath);

                    // Notify user
                    MessageBoxLibrary.DownloadCanceledMessageBox();
                }
                catch (Exception ex) //Error downloading
                {
                    // Notify developer
                    string formattedException = $"Error downloading emulator.\n\n" +
                                                $"File: {downloadFilePath}" +
                                                $"Exception type: {ex.GetType().Name}\n" +
                                                $"Exception details: {ex.Message}";
                    await LogErrors.LogErrorAsync(ex, formattedException);
            
                    // Notify user
                    await MessageBoxLibrary.EmulatorDownloadErrorMessageBox(selectedSystem, ex);
                }
                finally
                {
                    StopDownloadButton.IsEnabled = false;
                    DeleteDownloadFilePath(downloadFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            string formattedException = $"General error downloading the emulator.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);

            // Notify user
            MessageBoxLibrary.DownloadExtractionFailedMessageBox();
        }

        string ChangeFileExtensionFunction(EasyModeSystemConfig selectedSystem, string downloadFilePath)
        {
            if (selectedSystem.Emulators.Emulator.EmulatorDownloadRename)
            {
                string newFilePath = Path.ChangeExtension(downloadFilePath, ".7z");
                if (File.Exists(downloadFilePath) && !File.Exists(newFilePath))
                {
                    try
                    {
                        File.Move(downloadFilePath, newFilePath);
                    }
                    catch (Exception)
                    {
                        // ignore
                    }
                }
                if (!File.Exists(newFilePath))
                {
                    // Update the downloadFilePath to the new file path
                    downloadFilePath = newFilePath;                                
                }
            }

            return downloadFilePath;
        }
    }

    private async void DownloadCoreButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _isDownloadCompleted = false;
            _isCoreDownloaded = true;
            DownloadCoreButton.IsEnabled = false;
            UpdateAddSystemButtonState();
            
            var selectedSystem = _config.Systems.FirstOrDefault(system => system.SystemName == SystemNameDropdown.SelectedItem.ToString());
            if (selectedSystem != null)
            {
                // string coreLocation = selectedSystem.Emulators.Emulator.CoreLocation;
                string coreDownloadUrl = selectedSystem.Emulators.Emulator.CoreDownloadLink;
                string emulatorsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emulators");
                Directory.CreateDirectory(emulatorsFolderPath);
                string downloadFilePath = Path.Combine(_tempFolder, Path.GetFileName(coreDownloadUrl) ?? throw new InvalidOperationException("'Simple Launcher' could not get coreDownloadUrl"));
                Directory.CreateDirectory(_tempFolder);
                string destinationPath = selectedSystem.Emulators.Emulator.CoreDownloadExtractPath;
                // string latestVersionString = selectedSystem.Emulators.Emulator.CoreLatestVersion;

                try
                {
                    // Display progress bar
                    DownloadProgressBar.Visibility = Visibility.Visible;
                    DownloadProgressBar.Value = 0;
                    StopDownloadButton.IsEnabled = true;

                    // Initialize cancellation token source
                    _cancellationTokenSource = new CancellationTokenSource();

                    await DownloadWithProgressAsync(coreDownloadUrl, downloadFilePath, _cancellationTokenSource.Token);

                    // Only proceed with extraction if the download completed successfully
                    if (_isDownloadCompleted)
                    {
                        // Show the PleaseWaitExtraction window
                        PleaseWaitExtraction pleaseWaitWindow = new PleaseWaitExtraction();
                        pleaseWaitWindow.Show();

                        bool extractionSuccess = await _extractCompressedFile
                            .ExtractDownloadFilesAsync2(downloadFilePath, destinationPath);
                        
                        // Close the PleaseWaitExtraction window
                        pleaseWaitWindow.Close();

                        if (extractionSuccess)
                        {
                            // Notify user
                            MessageBoxLibrary.DownloadAndExtrationWereSuccessfulMessageBox();

                            // Clean up the downloaded file only if extraction is successful
                            DeleteDownloadFilePath(downloadFilePath);

                            // Mark as downloaded and disable button
                            _isCoreDownloaded = true;
                            DownloadCoreButton.IsEnabled = false;
                            
                            // Update AddSystemButton state
                            UpdateAddSystemButtonState();
                        }
                        else // extraction failed
                        {
                            // Notify developer
                            string formattedException = $"Core extraction failed.\n\n" +
                                                        $"File: {downloadFilePath}";
                            Exception ex = new Exception(formattedException);
                            await LogErrors.LogErrorAsync(ex, formattedException);

                            // Notify user
                            MessageBoxLibrary.ExtractionFailedMessageBox();
                        }
                    }
                    else // download failed
                    {
                        // Notify developer
                        string formattedException = $"Core download failed.";
                        Exception ex = new Exception(formattedException);
                        await LogErrors.LogErrorAsync(ex, formattedException);

                        // Notify user
                        MessageBoxLibrary.DownloadFailedMessageBox();
                    }
                }
                catch (TaskCanceledException)
                {
                    DeleteDownloadFilePath(downloadFilePath);
                    
                    // Notify user
                    MessageBoxLibrary.DownloadCanceledMessageBox();
                }
                catch (Exception ex) //Error downloading
                {
                    // Notify developer
                    string formattedException = $"Error downloading the core.\n\n" +
                                                $"File: {downloadFilePath}" +
                                                $"Exception type: {ex.GetType().Name}\n" +
                                                $"Exception details: {ex.Message}";
                    await LogErrors.LogErrorAsync(ex, formattedException);
                    
                    // Notify user
                    await MessageBoxLibrary.CoreDownloadErrorMessageBox(selectedSystem, ex);
                }
                finally
                {
                    StopDownloadButton.IsEnabled = false;
                    DeleteDownloadFilePath(downloadFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            string formattedException = $"General error downloading the core.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);
            
            // Notify user
            MessageBoxLibrary.DownloadExtractionFailedMessageBox();
        }
    }

    private async void DownloadImagePackButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Reset the flag at the start of the download
            _isDownloadCompleted = false;

            var selectedSystem = _config.Systems.FirstOrDefault(system => system.SystemName == SystemNameDropdown.SelectedItem.ToString());
            if (selectedSystem != null)
            {
                string extrasDownloadUrl = selectedSystem.Emulators.Emulator.ExtrasDownloadLink;
                string extrasFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");
                Directory.CreateDirectory(extrasFolderPath);
                string downloadFilePath = Path.Combine(_tempFolder, Path.GetFileName(extrasDownloadUrl) ?? throw new InvalidOperationException("'Simple Launcher' could not get extrasDownloadUrl"));
                Directory.CreateDirectory(_tempFolder);
                string destinationPath = selectedSystem.Emulators.Emulator.ExtrasDownloadExtractPath;

                try
                {
                    // Display progress bar
                    DownloadProgressBar.Visibility = Visibility.Visible;
                    DownloadProgressBar.Value = 0;
                    StopDownloadButton.IsEnabled = true;

                    // Initialize cancellation token source
                    _cancellationTokenSource = new CancellationTokenSource();

                    await DownloadWithProgressAsync(extrasDownloadUrl, downloadFilePath, _cancellationTokenSource.Token);

                    // Only proceed with extraction if the download completed successfully
                    if (_isDownloadCompleted)
                    {
                        // Show the PleaseWaitExtraction window
                        PleaseWaitExtraction pleaseWaitWindow = new PleaseWaitExtraction();
                        pleaseWaitWindow.Show();

                        bool extractionSuccess = await _extractCompressedFile
                            .ExtractDownloadFilesAsync2(downloadFilePath, destinationPath);

                        // Close the PleaseWaitExtraction window
                        pleaseWaitWindow.Close();
                        
                        if (extractionSuccess)
                        {
                            // Notify user
                            MessageBoxLibrary.DownloadAndExtrationWereSuccessfulMessageBox();

                            // Clean up the downloaded file only if extraction is successful
                            DeleteDownloadFilePath(downloadFilePath);
                            
                            // Mark as downloaded and disable button
                            DownloadExtrasButton.IsEnabled = false;
                            
                            // Update AddSystemButton state
                            UpdateAddSystemButtonState();
                        }
                        else // Extraction failed
                        {
                            // Notify developer
                            string formattedException = $"Image Pack extraction failed.\n\n" +
                                                        $"File: {extrasDownloadUrl}";
                            Exception ex = new Exception(formattedException);
                            await LogErrors.LogErrorAsync(ex, formattedException);

                            // Notify user
                            MessageBoxLibrary.ExtractionFailedMessageBox();
                        }
                    }
                    else // Download Failed
                    {
                        // Notify developer
                        string formattedException = $"Image Pack download failed.\n\n" +
                                                    $"File: {extrasDownloadUrl}";
                        Exception ex = new Exception(formattedException);
                        await LogErrors.LogErrorAsync(ex, formattedException);

                        // Notify user
                        MessageBoxLibrary.DownloadFailedMessageBox();
                    }
                }
                catch (TaskCanceledException)
                {
                    DeleteDownloadFilePath(downloadFilePath);

                    // Notify user
                    MessageBoxLibrary.DownloadCanceledMessageBox();
                }
                catch (Exception ex) //Error downloading
                {
                    // Notify developer
                    string formattedException = $"Error downloading the Image Pack.\n\n" +
                                                $"File: {downloadFilePath}" +
                                                $"Exception type: {ex.GetType().Name}\n" +
                                                $"Exception details: {ex.Message}";
                    await LogErrors.LogErrorAsync(ex, formattedException);
                    
                    // Notify user
                    await MessageBoxLibrary.ImagePackDownloadErrorMessageBox(selectedSystem, ex);
                }
                finally
                {
                    StopDownloadButton.IsEnabled = false;
                    DeleteDownloadFilePath(downloadFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            string formattedException = $"General error downloading the Image Pack.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);
            
            // Notify user
            MessageBoxLibrary.DownloadExtractionFailedMessageBox();
        }
    }

    private async Task DownloadWithProgressAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 8192, true);
            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalBytesRead += bytesRead;

                if (totalBytes.HasValue)
                {
                    DownloadProgressBar.Value = (double)totalBytesRead / totalBytes.Value * 100;
                }
            }

            // Check if the file was fully downloaded
            if (totalBytes.HasValue && totalBytesRead == totalBytes.Value)
            {
                _isDownloadCompleted = true;
            }
            else
            {
                _isDownloadCompleted = false;
                throw new IOException("Download incomplete. Bytes downloaded do not match the expected file size.");
            }

        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Notify developer
            string formattedException = $"The requested file was not available on the server.\n\n" +
                                        $"URL: {downloadUrl}\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);

            // Notify user
            MessageBoxLibrary.DownloadFailedMessageBox();
        }
        catch (HttpRequestException ex)
        {
            // Notify developer
            string formattedException = $"Network error during file download.\n\n" +
                                        $"URL: {downloadUrl}\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);

            // Notify user
            MessageBoxLibrary.DownloadFailedMessageBox();
        }
        catch (IOException ex)
        {
            // Notify developer
            string formattedException = $"File read/write error after file download.\n\n" +
                                        $"URL: {downloadUrl}\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, formattedException);

            // Notify user
            MessageBoxLibrary.IoExceptionMessageBox(_tempFolder);
        }
        catch (TaskCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Delete temp files
                DeleteDownloadedFile();
                
                // Notify developer
                string formattedException = $"Download was canceled by the user. User was not notified.\n\n" +
                                            $"URL: {downloadUrl}\n" +
                                            $"Exception type: {ex.GetType().Name}\n" +
                                            $"Exception details: {ex.Message}";
                await LogErrors.LogErrorAsync(ex, formattedException);
                
                // Notify user
                // Ignore
            }
            else
            {
                // Delete temp files
                DeleteDownloadedFile();
                    
                // Notify developer
                string formattedException = $"Download timed out or was canceled unexpectedly.\n\n" +
                                            $"URL: {downloadUrl}\n" +
                                            $"Exception type: {ex.GetType().Name}\n" +
                                            $"Exception details: {ex.Message}";
                await LogErrors.LogErrorAsync(ex, formattedException);
                    
                // Notify user
                MessageBoxLibrary.DownloadFailedMessageBox();
            }
        }

        void DeleteDownloadedFile()
        {
            if (File.Exists(destinationPath))
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }
    }

    private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel(); // Cancel the ongoing download
            StopDownloadButton.IsEnabled = false; // Disable the stop button once the download is canceled

            // Reset completion flag and progress
            _isDownloadCompleted = false; 
            DownloadProgressBar.Value = 0;
                
            // Reinitialize the cancellation token source for the next download
            _cancellationTokenSource = new CancellationTokenSource();
        }
    }

    private void AddSystemButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedSystem = _config.Systems.FirstOrDefault(system => system.SystemName == SystemNameDropdown.SelectedItem.ToString());
        if (selectedSystem != null)
        {
            // Determine the system folder to use
            string systemFolder = SystemFolderTextBox.Text;
            if (string.IsNullOrEmpty(systemFolder))
            {
                systemFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "roms", selectedSystem.SystemName);
                SystemFolderTextBox.Text = systemFolder;
            }
            
            // Remove the leading dot from the SystemImageFolder for the message
            string systemImageFolderForMessage = selectedSystem.SystemImageFolder.TrimStart('.').TrimStart('\\', '/');

            // Combine with the base directory for the message
            string fullImageFolderPathForMessage = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, systemImageFolderForMessage);

            // Path to the system.xml file
            string systemXmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.xml");

            try
            {
                // Load existing system configurations
                XDocument xmlDoc = XDocument.Load(systemXmlPath);
                var systemConfigs = xmlDoc.Descendants("SystemConfig").ToList();

                // Check if the system already exists
                var existingSystem = systemConfigs.FirstOrDefault(config => config.Element("SystemName")?.Value == selectedSystem.SystemName);
                if (existingSystem != null)
                {
                    // Ask user if they want to overwrite the existing system
                    if (MessageBoxLibrary.OverwriteSystemMessageBox(selectedSystem)) return;

                    // Overwrite existing system
                    existingSystem.SetElementValue("SystemName", selectedSystem.SystemName);
                    existingSystem.SetElementValue("SystemFolder", systemFolder);
                    existingSystem.SetElementValue("SystemImageFolder", selectedSystem.SystemImageFolder);
                    existingSystem.SetElementValue("SystemIsMAME", selectedSystem.SystemIsMame);
                    existingSystem.Element("FileFormatsToSearch")?.Remove();
                    existingSystem.Add(new XElement("FileFormatsToSearch", selectedSystem.FileFormatsToSearch.Select(format => new XElement("FormatToSearch", format))));
                    existingSystem.SetElementValue("ExtractFileBeforeLaunch", selectedSystem.ExtractFileBeforeLaunch);
                    existingSystem.Element("FileFormatsToLaunch")?.Remove();
                    existingSystem.Add(new XElement("FileFormatsToLaunch", selectedSystem.FileFormatsToLaunch.Select(format => new XElement("FormatToLaunch", format))));
                    existingSystem.Element("Emulators")?.Remove();
                    existingSystem.Add(new XElement("Emulators",
                        new XElement("Emulator",
                            new XElement("EmulatorName", selectedSystem.Emulators.Emulator.EmulatorName),
                            new XElement("EmulatorLocation", selectedSystem.Emulators.Emulator.EmulatorLocation),
                            new XElement("EmulatorParameters", selectedSystem.Emulators.Emulator.EmulatorParameters)
                        )
                    ));
                }
                else
                {
                    // Create a new XElement for the selected system
                    var newSystemElement = new XElement("SystemConfig",
                        new XElement("SystemName", selectedSystem.SystemName),
                        new XElement("SystemFolder", systemFolder),
                        new XElement("SystemImageFolder", selectedSystem.SystemImageFolder),
                        new XElement("SystemIsMAME", selectedSystem.SystemIsMame),
                        new XElement("FileFormatsToSearch", selectedSystem.FileFormatsToSearch.Select(format => new XElement("FormatToSearch", format))),
                        new XElement("ExtractFileBeforeLaunch", selectedSystem.ExtractFileBeforeLaunch),
                        new XElement("FileFormatsToLaunch", selectedSystem.FileFormatsToLaunch.Select(format => new XElement("FormatToLaunch", format))),
                        new XElement("Emulators",
                            new XElement("Emulator",
                                new XElement("EmulatorName", selectedSystem.Emulators.Emulator.EmulatorName),
                                new XElement("EmulatorLocation", selectedSystem.Emulators.Emulator.EmulatorLocation),
                                new XElement("EmulatorParameters", selectedSystem.Emulators.Emulator.EmulatorParameters)
                            )
                        )
                    );

                    // Add the new system to the XML document
                    xmlDoc.Root?.Add(newSystemElement);
                }

                // Sort the systems alphabetically by SystemName
                if (xmlDoc.Root != null)
                    xmlDoc.Root.ReplaceNodes(xmlDoc.Root.Elements("SystemConfig")
                        .OrderBy(systemElement => systemElement.Element("SystemName")?.Value));

                // Save the updated XML document
                xmlDoc.Save(systemXmlPath);

                // Create the necessary folders for the system
                CreateSystemFolders(selectedSystem.SystemName, systemFolder, fullImageFolderPathForMessage);

                // Notify user
                MessageBoxLibrary.SystemAddedMessageBox(systemFolder, fullImageFolderPathForMessage, selectedSystem);

                // Disable Add System Button
                AddSystemButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                // Notify developer
                string formattedException = $"Error adding system.\n\n" +
                                            $"Exception type: {ex.GetType().Name}\n" +
                                            $"Exception details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));
 
                // Notify user
                MessageBoxLibrary.AddSystemFailedMessageBox();
            }
        }
    }
        
    private void UpdateAddSystemButtonState()
    {
        AddSystemButton.IsEnabled = _isEmulatorDownloaded && _isCoreDownloaded;
    }
        
    private void CreateSystemFolders(string systemName, string systemFolder, string systemImageFolder)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Paths for the primary system folder and image folder
        string systemFolderPath = Path.Combine(baseDirectory, systemFolder);
        string imagesFolderPath = Path.Combine(baseDirectory, systemImageFolder);

        // List of additional folders to create
        string[] additionalFolders = ["roms", "images", "title_snapshots", "gameplay_snapshots", "videos", "manuals", "walkthrough", "cabinets", "carts", "flyers", "pcbs"];

        try
        {
            // Create the primary system folder if it doesn't exist
            if (!Directory.Exists(systemFolderPath))
            {
                Directory.CreateDirectory(systemFolderPath);
            }

            // Create the primary image folder if it doesn't exist
            if (!Directory.Exists(imagesFolderPath))
            {
                Directory.CreateDirectory(imagesFolderPath);
            }

            // Create each additional folder
            foreach (var folder in additionalFolders)
            {
                string folderPath = Path.Combine(baseDirectory, folder, systemName);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            string formattedException = $"The application failed to create the necessary folders for the newly added system.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
            logTask.Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.FolderCreationFailedMessageBox();

            throw;
        }
    }
        
    private void EditSystemEasyModeAddSystem_Closed(object sender, EventArgs e)
    {
        _config = null;
            
        // Prepare the process start info
        var processModule = Process.GetCurrentProcess().MainModule;
        if (processModule != null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processModule.FileName,
                UseShellExecute = true
            };

            // Start the new application instance
            Process.Start(startInfo);

            // Shutdown the current application instance
            Application.Current.Shutdown();
            Environment.Exit(0);
        }
    }
        
    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string chooseaFolderwithRoMsorIsOs2 = (string)Application.Current.TryFindResource("ChooseaFolderwithROMsorISOs") ?? "Choose a folder with 'ROMs' or 'ISOs' for this system";
        using var dialog = new FolderBrowserDialog();
        dialog.Description = chooseaFolderwithRoMsorIsOs2;
        dialog.ShowNewFolderButton = true;

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SystemFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
    
    private static void DeleteDownloadFilePath(string downloadFilePath)
    {
        if (File.Exists(downloadFilePath))
        {
            try
            {
                File.Delete(downloadFilePath);
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }
}