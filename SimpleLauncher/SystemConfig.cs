﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using System.IO;
using System.Threading.Tasks;

namespace SimpleLauncher;

public class SystemConfig
{
    public string SystemName { get; set; }
    public string SystemFolder { get; set; }
    public string SystemImageFolder { get; set; }
    public bool SystemIsMame { get; set; }
    public List<string> FileFormatsToSearch { get; set; }
    public bool ExtractFileBeforeLaunch { get; set; }
    public List<string> FileFormatsToLaunch { get; set; }
    public List<Emulator> Emulators { get; set; }
        
    public class Emulator
    {
        public string EmulatorName { get; init; }
        public string EmulatorLocation { get; init; }
        public string EmulatorParameters { get; init; }
    }
        
    private static readonly string XmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.xml");

    public static List<SystemConfig> LoadSystemConfigs()
    {
        if (string.IsNullOrEmpty(XmlPath))
        {
            throw new ArgumentNullException(nameof(XmlPath), @"The path to the XML file cannot be null or empty.");
        }

        try
        {
            // Check for the existence of the system.xml
            if (!File.Exists(XmlPath))
            {
                // Search for backup files in the application directory
                string directoryPath = Path.GetDirectoryName(XmlPath);
                    
                try
                {
                    if (directoryPath != null)
                    {
                        var backupFiles = Directory.GetFiles(directoryPath, "system_backup*.xml").ToList();
                        if (backupFiles.Count > 0)
                        {
                            // Sort the backup files by their creation time to find the most recent one
                            var mostRecentBackupFile = backupFiles.MaxBy(File.GetCreationTime);
                            MessageBoxResult restoreResult = MessageBox.Show("I could not find the file system.xml, which is required to start the application.\n\nBut I found a backup system file.\n\nWould you like to restore the last backup?", "Restore Backup?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (restoreResult == MessageBoxResult.Yes)
                            {
                                // Rename the most recent backup file to system.xml
                                File.Copy(mostRecentBackupFile, XmlPath, false);
                            }
                        }
                        else
                        {
                            // Create 'system.xml' using a prefilled 'system_model.xml'
                            string systemModel = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system_model.xml");

                            if (File.Exists(systemModel))
                            {
                                File.Copy(systemModel, XmlPath, false);
                            }
                            else
                            {
                                string contextMessage = $"'system_model.xml' was not found in the application folder.";
                                Exception ex = new Exception(contextMessage);
                                Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
                                logTask.Wait(TimeSpan.FromSeconds(2));
                                        
                                // Ask the user if they want to automatically reinstall Simple Launcher
                                var messageBoxResult = MessageBox.Show(
                                    "The file 'system_model.xml' is missing.\n\nSimple Launcher cannot work properly without this file.\n\nDo you want to automatically reinstall Simple Launcher to fix the problem?",
                                    "Missing File",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Warning);

                                if (messageBoxResult == MessageBoxResult.Yes)
                                {
                                    ReinstallSimpleLauncher.StartUpdaterAndShutdown();
                                }
                                else
                                {
                                    MessageBox.Show("Please reinstall Simple Launcher manually to fix the problem.\n\nSimple Launcher will now shut down.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);

                                    // Shutdown the application and exit
                                    Application.Current.Shutdown();
                                    Environment.Exit(0);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    string contextMessage = $"The file 'system.xml' is corrupted or could not be open.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                    Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
                    logTask.Wait(TimeSpan.FromSeconds(2));
                        
                    MessageBox.Show($"'system.xml' is corrupted or could not be open.\n" +
                                    $"Please fix it manually or delete it.\n" +
                                    $"If you choose to delete it then Simple Launcher will create a new one for you.\n\n" +
                                    $"If you want to debug the error yourself, check the file 'error_user.log' inside Simple Launcher folder.\n\n" +
                                    $"Simple Launcher will now shut down.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    // Shutdown the application and exit
                    Application.Current.Shutdown();
                    Environment.Exit(0);
                    
                    return null;
                }
            }
            
            var doc = XDocument.Load(XmlPath);
            var systemConfigs = new List<SystemConfig>();
            var invalidConfigs = new List<XElement>();

            if (doc.Root != null)
            {
                foreach (var sysConfigElement in doc.Root.Elements("SystemConfig"))
                {

                    try
                    {
                        // Attempt to parse each system configuration.
                        if (sysConfigElement.Element("SystemName") == null ||
                            string.IsNullOrEmpty(sysConfigElement.Element("SystemName")?.Value))
                            throw new InvalidOperationException("Missing or empty SystemName in XML.");

                        if (sysConfigElement.Element("SystemFolder") == null ||
                            string.IsNullOrEmpty(sysConfigElement.Element("SystemFolder")?.Value))
                            throw new InvalidOperationException("Missing or empty SystemFolder in XML.");

                        if (!bool.TryParse(sysConfigElement.Element("SystemIsMAME")?.Value, out bool systemIsMame))
                            throw new InvalidOperationException("Invalid or missing value for SystemIsMAME.");

                        // Validate FileFormatsToSearch
                        var formatsToSearch = sysConfigElement.Element("FileFormatsToSearch")
                            ?.Elements("FormatToSearch")
                            .Select(e => e.Value.Trim())
                            .Where(value =>
                                !string.IsNullOrWhiteSpace(value)) // Ensure no empty or whitespace-only entries
                            .ToList();
                        if (formatsToSearch == null || formatsToSearch.Count == 0)
                            throw new InvalidOperationException("FileFormatsToSearch should have at least one value.");

                        // Validate ExtractFileBeforeLaunch
                        if (!bool.TryParse(sysConfigElement.Element("ExtractFileBeforeLaunch")?.Value,
                                out bool extractFileBeforeLaunch))
                            throw new InvalidOperationException(
                                "Invalid or missing value for ExtractFileBeforeLaunch.");

                        // Validate FileFormatsToLaunch
                        var formatsToLaunch = sysConfigElement.Element("FileFormatsToLaunch")
                            ?.Elements("FormatToLaunch")
                            .Select(e => e.Value.Trim())
                            .Where(value =>
                                !string.IsNullOrWhiteSpace(value)) // Ensure no empty or whitespace-only entries
                            .ToList();
                        if (extractFileBeforeLaunch && (formatsToLaunch == null || formatsToLaunch.Count == 0))
                            throw new InvalidOperationException(
                                "FileFormatsToLaunch should have at least one value when ExtractFileBeforeLaunch is true.");

                        // Validate emulator configurations
                        var emulators = sysConfigElement.Element("Emulators")?.Elements("Emulator").Select(
                            emulatorElement =>
                            {
                                if (string.IsNullOrEmpty(emulatorElement.Element("EmulatorName")?.Value))
                                    throw new InvalidOperationException("EmulatorName should not be empty or null.");

                                return new Emulator
                                {
                                    EmulatorName = emulatorElement.Element("EmulatorName")?.Value,
                                    EmulatorLocation = emulatorElement.Element("EmulatorLocation")?.Value,
                                    EmulatorParameters =
                                        emulatorElement.Element("EmulatorParameters")
                                            ?.Value // It's okay if this is null or empty
                                };
                            }).ToList();

                        if (emulators == null || emulators.Count == 0)
                            throw new InvalidOperationException("Emulators list should not be empty or null.");

                        systemConfigs.Add(new SystemConfig
                        {
                            SystemName = sysConfigElement.Element("SystemName")?.Value,
                            SystemFolder = sysConfigElement.Element("SystemFolder")?.Value,
                            SystemImageFolder = sysConfigElement.Element("SystemImageFolder")?.Value,
                            SystemIsMame = systemIsMame,
                            ExtractFileBeforeLaunch = extractFileBeforeLaunch,
                            FileFormatsToSearch = formatsToSearch,
                            FileFormatsToLaunch = formatsToLaunch,
                            Emulators = emulators
                        });
                    }
                    catch (Exception)
                    {
                        // Collect invalid configurations for removal
                        invalidConfigs.Add(sysConfigElement);
                        // Display a message to user for each invalid system removed
                        MessageBox.Show($"The system '{sysConfigElement.Element("SystemName")?.Value}' was removed due to invalid values.", "Invalid System Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
               
                }
            
                // Remove any invalid configurations from the XML document
                foreach (var invalidConfig in invalidConfigs)
                {
                    invalidConfig.Remove();
                }
            
                // Save the corrected XML back to disk
                doc.Save(XmlPath);
            }

            return systemConfigs;
        }
        catch (Exception ex)
        {
            string contextMessage = $"Error loading system configurations from 'system.xml'.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
            Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
            logTask.Wait(TimeSpan.FromSeconds(2));

            MessageBox.Show($"'system.xml' is corrupted or could not be open.\n\n" +
                            $"Please fix it manually or delete it.\n\n" +
                            $"If you choose to delete it then Simple Launcher will create a new one for you.\n\n" +
                            $"If you want to debug the error yourself, check the file 'error_user.log' inside Simple Launcher folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
    
    public static void SaveSystemConfigs(List<SystemConfig> systemConfigs)
    {
        if (systemConfigs == null || systemConfigs.Count == 0)
            throw new ArgumentNullException(nameof(systemConfigs), @"System configurations list cannot be null or empty.");

        try
        {
            // Sort the systemConfigs list alphabetically by SystemName
            var sortedSystemConfigs = systemConfigs.OrderBy(config => config.SystemName).ToList();

            var doc = new XDocument(
                new XElement("Systems",
                    sortedSystemConfigs.Select(systemConfig =>
                        new XElement("SystemConfig",
                            new XElement("SystemName", systemConfig.SystemName),
                            new XElement("SystemFolder", systemConfig.SystemFolder),
                            new XElement("SystemImageFolder", systemConfig.SystemImageFolder),
                            new XElement("SystemIsMAME", systemConfig.SystemIsMame),
                            new XElement("FileFormatsToSearch",
                                systemConfig.FileFormatsToSearch.Select(format =>
                                    new XElement("FormatToSearch", format))
                            ),
                            new XElement("ExtractFileBeforeLaunch", systemConfig.ExtractFileBeforeLaunch),
                            new XElement("FileFormatsToLaunch",
                                systemConfig.FileFormatsToLaunch.Select(format =>
                                    new XElement("FormatToLaunch", format))
                            ),
                            new XElement("Emulators",
                                systemConfig.Emulators.Select(emulator =>
                                    new XElement("Emulator",
                                        new XElement("EmulatorName", emulator.EmulatorName),
                                        new XElement("EmulatorLocation", emulator.EmulatorLocation),
                                        new XElement("EmulatorParameters", emulator.EmulatorParameters)
                                    )
                                )
                            )
                        )
                    )
                )
            );

            doc.Save(XmlPath);
        }
        catch (Exception ex)
        {
            string contextMessage = $"Error saving system configurations to system.xml.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
            Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
            logTask.Wait(TimeSpan.FromSeconds(2));

            MessageBox.Show($"There was an error saving system configurations.\n\nPlease check 'error_user.log' in the Simple Launcher folder for more details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}