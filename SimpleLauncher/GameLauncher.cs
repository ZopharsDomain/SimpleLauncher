﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SimpleLauncher
{
    public static class GameLauncher
    {
        public static async Task HandleButtonClick(string filePath, ComboBox emulatorComboBox, ComboBox systemComboBox, List<SystemConfig> systemConfigs)
        {
            ProcessStartInfo psi = null;

            try
            {
                string fileExtension = Path.GetExtension(filePath).ToUpperInvariant();

                // Check if the file is a .bat file
                if (fileExtension == ".BAT")
                {
                    bool wasGamePadControllerRunning = GamePadController.Instance2.IsRunning;

                    // If the GamePadController is running, stop it before executing the .BAT
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Stop();
                    }

                    psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    Process process = new() { StartInfo = psi };
                    process.Start();

                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    // If the GamePadController was running, restart it after the .BAT execution
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Start();
                    }

                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show($"Error launching batch file:\n\nExit code: {process.ExitCode}\n\nOutput: {output}\n\nError: {error}\n\nFileName: {psi.FileName}\n", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        string errorMessage = $"Error launching batch file:\n\nExit code {process.ExitCode}\n\nOutput: {output}\n\nError: {error}\n\nFileName: {psi.FileName}\n";
                        await LogErrors.LogErrorAsync(new Exception(errorMessage));
                    }

                    return;
                }

                // Check if the file is a .lnk (shortcut) file
                if (fileExtension == ".LNK")
                {
                    bool wasGamePadControllerRunning = GamePadController.Instance2.IsRunning;

                    // If the GamePadController is running, stop it before launching the .LNK
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Stop();
                    }

                    psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };

                    try
                    {
                        Process process = Process.Start(psi); // Start the process without redirecting output/error
                        if (process != null) await process.WaitForExitAsync(); // Wait for the process to exit

                        // If the GamePadController was running, restart it after the .LNK exits
                        if (wasGamePadControllerRunning)
                        {
                            GamePadController.Instance2.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle the exception
                        MessageBox.Show($"Error executing the shortcut: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Log the error with more details
                        await LogErrors.LogErrorAsync(new Exception(ex.Message));
                    }

                    return;
                }

                // Check if the file is a .exe (executable) file
                if (fileExtension == ".EXE")
                {
                    bool wasGamePadControllerRunning = GamePadController.Instance2.IsRunning;

                    // If the GamePadController is running, stop it before launching the .EXE
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Stop();
                    }

                    psi = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };

                    try
                    {
                        Process process = Process.Start(psi); // Start the process without redirecting output/error
                        if (process != null)
                            await process
                                .WaitForExitAsync(); // Use WaitForExitAsync to asynchronously wait for the process to exit

                        // If the GamePadController was running, restart it after the .EXE exits
                        if (wasGamePadControllerRunning)
                        {
                            GamePadController.Instance2.Start();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error running the executable: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        await LogErrors.LogErrorAsync(new Exception(ex.Message));
                    }

                    return;
                }

                // Regular call of the method
                if (emulatorComboBox.SelectedItem != null)
                {
                    
                    bool wasGamePadControllerRunning = GamePadController.Instance2.IsRunning;

                    // If the GamePadController is running, stop it before proceeding
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Stop();
                    }
                    
                    string selectedEmulatorName = emulatorComboBox.SelectedItem.ToString();
                    string selectedSystem = systemComboBox.SelectedItem.ToString();

                    var systemConfig = systemConfigs.FirstOrDefault(config => config.SystemName == selectedSystem);

                    if (systemConfig == null)
                    {
                        MessageBox.Show("Please select a valid system", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var emulatorConfig = systemConfig.Emulators.FirstOrDefault(e => e.EmulatorName == selectedEmulatorName);

                    if (emulatorConfig == null)
                    {
                        MessageBox.Show("Selected emulator configuration not found", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string gamePathToLaunch = filePath;  // Default to the original path

                    // Determine if extraction is needed based on system configuration
                    if (systemConfig.ExtractFileBeforeLaunch)
                    {
                        if (fileExtension == ".ZIP" || fileExtension == ".7Z")
                        {
                            // Extract the archive to a temporary location
                            string tempExtractLocation = ExtractCompressedFile.Instance2.ExtractArchiveToTemp(filePath);

                            if (string.IsNullOrEmpty(tempExtractLocation))
                            {
                                MessageBox.Show("Failed to extract the archive.\nCheck if the compressed file is corrupt.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            // Iterate through the formats to launch and find the first file with the specified extension
                            bool fileFound = false;
                            foreach (string formatToLaunch in systemConfig.FileFormatsToLaunch)
                            {
                                string[] files = Directory.GetFiles(tempExtractLocation, $"*{formatToLaunch}");

                                if (files.Length > 0)
                                {
                                    gamePathToLaunch = files[0];
                                    fileFound = true;
                                    break;
                                }
                            }

                            if (!fileFound)
                            {
                                MessageBox.Show("Couldn't find a file with the specified extensions after extraction.\nEdit System to fix it.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }
                        }
                    }

                    string programLocation = emulatorConfig.EmulatorLocation;
                    string parameters = emulatorConfig.EmulatorParameters;
                    string arguments = $"{parameters} \"{gamePathToLaunch}\"";
                    // Create ProcessStartInfo
                    psi = new ProcessStartInfo
                    {
                        FileName = programLocation,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    // Launch the external program
                    Process process = new() { StartInfo = psi };
                    process.Start();

                    // Read the output streams
                    await process.StandardOutput.ReadToEndAsync();
                    await process.StandardError.ReadToEndAsync();

                    // Wait for the process to exit
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0 && process.ExitCode != -1073741819)
                    {
                        string errorMessage = $"Error launching external program:\n\nExit code {process.ExitCode}\n\n";
                        errorMessage += $"Emulator: {psi.FileName}\n\nArguments: {psi.Arguments}\n";
                        await LogErrors.LogErrorAsync(new Exception(errorMessage));
                        MessageBox.Show($"The emulator could not open this file.\n\n{errorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    
                    // If the GamePadController was running, restart it after the psi exits
                    if (wasGamePadControllerRunning)
                    {
                        GamePadController.Instance2.Start();
                    }
                }
                else
                {
                    MessageBox.Show("Please select an emulator first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                string errorDetails = $"\nException Details: {ex}\n";
                if (psi != null)
                {
                    errorDetails += $"\nEmulator: {psi.FileName}\n\nArguments: {psi.Arguments}\n";
                }
                await LogErrors.LogErrorAsync(ex, errorDetails);
                // MessageBox.Show($"An error occurred: {ex.Message}\n{errorDetails}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                MessageBox.Show($"{ex.Message}\n{errorDetails}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}