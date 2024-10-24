using System.Diagnostics;

namespace Updater
{
    public partial class UpdateForm : Form
    {
        private readonly string[] _args;
        private delegate void LogDelegate(string message);
        
        public UpdateForm(string[] args)
        {
            InitializeComponent();
            _args = args ?? throw new ArgumentNullException(nameof(args));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var updateThread = new Thread(RunUpdateProcess)
            {
                IsBackground = true
            };
            updateThread.Start();
        }
        
        private void RunUpdateProcess()
        {
            UpdateProcess().Wait(); // Run the async method synchronously in the thread
        }

        private async Task UpdateProcess()
        {
            if (_args.Length < 4) // Expecting 4 arguments now: appExePath, updateSourcePath, updateZipPath, assetUrl
            {
                Log("Invalid arguments. Usage: Updater <appExePath> <updateSourcePath> <updateZipPath> <assetUrl>");
                return;
            }

            var appExePath = _args[0];
            var updateSourcePath = _args[1];
            var updateZipPath = _args[2];
            var assetUrl = _args[3];  // URL is now passed as a parameter

            if (string.IsNullOrEmpty(appExePath) || string.IsNullOrEmpty(updateSourcePath) || string.IsNullOrEmpty(updateZipPath) || string.IsNullOrEmpty(assetUrl))
            {
                Log("Invalid file paths or URL provided.");
                return;
            }

            var appDirectory = Path.GetDirectoryName(appExePath) ?? string.Empty;
            if (string.IsNullOrEmpty(appDirectory))
            {
                Log("Could not determine the application directory.");
                return;
            }

            try
            {
                // Wait for the main application to exit
                Log("Waiting for the main application to exit...");
                Thread.Sleep(3000);
        
                // Check if update.zip exists. If not, download it.
                if (!File.Exists(updateZipPath))
                {
                    Log("update.zip not found. Downloading...");
            
                    await DownloadUpdateFile(assetUrl, updateZipPath);  // Use the URL passed as an argument
                }

                // Check if the updateSourcePath exists. If not, extract updateZipPath
                if (!Directory.Exists(updateSourcePath))
                {
                    Log("updateSourcePath not found. Extracting updateZipPath...");
                    ExtractUpdateFile(updateZipPath, updateSourcePath);
                }

                // Ensure the updateSourcePath exists after extraction
                if (!Directory.Exists(updateSourcePath))
                {
                    Log("Failed to extract update files. Update process aborted.");
                    MessageBox.Show("Failed to extract update files. Please update manually.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Files to be ignored during the update
                var ignoredFiles = new[]
                {
                    "Updater.deps.json",
                    "Updater.dll",
                    "Updater.exe",
                    "Updater.pdb",
                    "Updater.runtimeconfig.json"
                };

                // Copy new files to the application directory
                foreach (var file in Directory.GetFiles(updateSourcePath))
                {
                    var fileName = Path.GetFileName(file);
                    if (!ignoredFiles.Contains(fileName))
                    {
                        var destFile = Path.Combine(appDirectory, fileName);
                        Log($"Copying {fileName}...");
                        File.Copy(file, destFile, true);
                    }
                }

                // Delete the temporary update files and the update.zip file
                Log("Deleting temporary update files...");
                Directory.Delete(updateSourcePath, true);
                File.Delete(updateZipPath);

                // Notify the user of a successful update
                Log("Update installed successfully. The application will now restart.");
                MessageBox.Show("Update installed successfully. The application will now restart.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Restart the main application
                var simpleLauncherExePath = Path.Combine(appDirectory, "SimpleLauncher.exe");
                var startInfo = new ProcessStartInfo
                {
                    FileName = simpleLauncherExePath,
                    UseShellExecute = false,
                    WorkingDirectory = appDirectory
                };

                Process.Start(startInfo);

                // Close the update Window
                Close();
            }
            catch (Exception ex)
            {
                Log($"Automatic update failed: {ex.Message}\nPlease update manually.");
                MessageBox.Show("Automatic update failed.\nPlease update manually.", "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Close the update Window
                Close();
            }
        }

        private void ExtractUpdateFile(string zipFilePath, string destinationDirectory)
        {
            string sevenZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe");
            if (!File.Exists(sevenZipPath))
            {
                Log("7z.exe not found in the application directory.");
                MessageBox.Show("7z.exe not found in the application directory.\n\nPlease reinstall Simple Launcher.", "7z.exe not found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                Arguments = $"x \"{zipFilePath}\" -o\"{destinationDirectory}\" -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit();

            if (process != null && process.ExitCode != 0)
            {
                Log($"7z.exe exited with code {process.ExitCode}. Extraction failed.");
                MessageBox.Show("7z.exe could not extract the compressed file.\n\nMaybe the compressed file is corrupt.", "Error extracting the file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task DownloadUpdateFile(string url, string destinationPath)  // Make it async
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetAsync(url);  // Now awaited
                response.EnsureSuccessStatusCode();

                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);  // Now awaited

                Log("update.zip downloaded successfully.");
            }
            catch (Exception ex)
            {
                Log($"Failed to download update.zip: {ex.Message}");
                MessageBox.Show("Failed to download update.zip. Please check your internet connection and try again.", "Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new LogDelegate(Log), message);
                return;
            }
            logTextBox.AppendText($"{DateTime.Now:HH:mm:ss} - {message}{Environment.NewLine}");
        }
    }
}
