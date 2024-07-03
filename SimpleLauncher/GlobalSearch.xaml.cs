using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimpleLauncher
{
    public partial class GlobalSearch
    {
        private readonly List<SystemConfig> _systemConfigs;
        private readonly List<MameConfig> _machines;
        private readonly AppSettings _settings;
        private ObservableCollection<SearchResult> _searchResults;
        private PleaseWaitSearch _pleaseWaitWindow;
        private DispatcherTimer _closeTimer;

        public GlobalSearch(List<SystemConfig> systemConfigs, List<MameConfig> machines, AppSettings settings)
        {
            InitializeComponent();
            _systemConfigs = systemConfigs;
            _machines = machines;
            _settings = settings;
            _searchResults = new ObservableCollection<SearchResult>();
            ResultsDataGrid.ItemsSource = _searchResults;
            Closed += GlobalSearch_Closed;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            string searchTerm = SearchTextBox.Text;
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                MessageBox.Show("Please enter a search term.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LaunchButton.IsEnabled = false;
            _searchResults.Clear();

            _pleaseWaitWindow = new PleaseWaitSearch
            {
                Owner = this
            };
            _pleaseWaitWindow.Show();

            _closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _closeTimer.Tick += (_, _) => _closeTimer.Stop();

            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += (_, args) => args.Result = PerformSearch(searchTerm);
            backgroundWorker.RunWorkerCompleted += (_, args) =>
            {
                if (args.Error != null)
                {
                    MessageBox.Show($"An error occurred during the search: {args.Error.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    if (args.Result is List<SearchResult> results && results.Any())
                    {
                        foreach (var result in results)
                        {
                            _searchResults.Add(result);
                        }
                        LaunchButton.IsEnabled = true;
                    }
                    else
                    {
                        _searchResults.Add(new SearchResult
                        {
                            FileName = "No results found.",
                            FolderName = "",
                            Size = 0
                        });
                    }
                }

                if (!_closeTimer.IsEnabled)
                {
                    _pleaseWaitWindow.Close();
                }
                else
                {
                    _closeTimer.Tick += (_, _) => _pleaseWaitWindow.Close();
                }
            };

            _closeTimer.Start();
            backgroundWorker.RunWorkerAsync();
        }

        private List<SearchResult> PerformSearch(string searchTerm)
        {
            var results = new List<SearchResult>();

            var searchTerms = ParseSearchTerms(searchTerm);

            foreach (var systemConfig in _systemConfigs)
            {
                string systemFolderPath = GetFullPath(systemConfig.SystemFolder);

                if (Directory.Exists(systemFolderPath))
                {
                    var files = Directory.GetFiles(systemFolderPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => systemConfig.FileFormatsToSearch.Contains(Path.GetExtension(file).TrimStart('.').ToLower()))
                        .Where(file => MatchesSearchQuery(Path.GetFileName(file).ToLower(), searchTerms))
                        .Select(file => new SearchResult
                        {
                            FileName = Path.GetFileName(file),
                            FolderName = Path.GetDirectoryName(file)?.Split(Path.DirectorySeparatorChar).Last(),
                            FilePath = file,
                            Size = Math.Round(new FileInfo(file).Length / 1024.0, 2),
                            MachineName = GetMachineDescription(Path.GetFileNameWithoutExtension(file)),
                            SystemName = systemConfig.SystemName,
                            EmulatorConfig = systemConfig.Emulators.FirstOrDefault()
                        })
                        .ToList();

                    results.AddRange(files);
                }
            }

            var scoredResults = ScoreResults(results, searchTerms);
            return scoredResults;
        }

        private List<SearchResult> ScoreResults(List<SearchResult> results, List<string> searchTerms)
        {
            foreach (var result in results)
            {
                result.Score = CalculateScore(result.FileName.ToLower(), searchTerms);
            }

            return results.OrderByDescending(r => r.Score).ThenBy(r => r.FileName).ToList();
        }

        private int CalculateScore(string text, List<string> searchTerms)
        {
            int score = 0;

            foreach (var term in searchTerms)
            {
                int index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    score += 10;
                    score += (text.Length - index);
                }
            }

            return score;
        }

        private bool MatchesSearchQuery(string text, List<string> searchTerms)
        {
            bool hasAnd = searchTerms.Contains("and");
            bool hasOr = searchTerms.Contains("or");

            if (hasAnd)
            {
                return searchTerms.Where(term => term != "and").All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            if (hasOr)
            {
                return searchTerms.Where(term => term != "or").Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return searchTerms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> ParseSearchTerms(string searchTerm)
        {
            var terms = new List<string>();
            var matches = Regex.Matches(searchTerm, @"[\""].+?[\""]|[^ ]+");

            foreach (Match match in matches)
            {
                terms.Add(match.Value.Trim('"').ToLower());
            }

            return terms;
        }

        private string GetMachineDescription(string fileNameWithoutExtension)
        {
            var machine = _machines.FirstOrDefault(m => m.MachineName.Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));
            return machine?.Description ?? string.Empty;
        }

        private string GetFullPath(string path)
        {
            if (path.StartsWith(@".\"))
            {
                path = path.Substring(2);
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private async void LaunchGameFromSearchResult(string filePath, string systemName, SystemConfig.Emulator emulatorConfig)
        {
            try
            {
                if (string.IsNullOrEmpty(systemName) || emulatorConfig == null)
                {
                    MessageBox.Show("There is no System or Emulator associated with that file. I cannot launch that file from this window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var systemConfig = _systemConfigs.FirstOrDefault(config =>
                    config.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));

                if (systemConfig == null)
                {
                    MessageBox.Show("System configuration not found for the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var mockSystemComboBox = new ComboBox();
                var mockEmulatorComboBox = new ComboBox();

                mockSystemComboBox.ItemsSource = _systemConfigs.Select(config => config.SystemName).ToList();
                mockSystemComboBox.SelectedItem = systemConfig.SystemName;

                mockEmulatorComboBox.ItemsSource = systemConfig.Emulators.Select(emulator => emulator.EmulatorName).ToList();
                mockEmulatorComboBox.SelectedItem = emulatorConfig.EmulatorName;

                await GameLauncher.HandleButtonClick(filePath, mockEmulatorComboBox, mockSystemComboBox, _systemConfigs);
            }
            catch (Exception ex)
            {
                string formattedException = $"There was an error launching the game from Global Search Window.\n\nException Details: {ex.Message}\n\nFile Path: {filePath}\n\nSystem Name: {systemName}";
                await LogErrors.LogErrorAsync(ex, formattedException);
                MessageBox.Show($"{formattedException}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                }
                else
                {
                    MessageBox.Show("Please select a game to launch.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResultsDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    var contextMenu = new ContextMenu();

                    var launchMenuItem = new MenuItem
                    {
                        Header = "Launch Game"
                    };
                    launchMenuItem.Click += (_, _) => LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                    contextMenu.Items.Add(launchMenuItem);

                    AddMenuItem(contextMenu, "Open Video Link", () => OpenVideoLink(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Open Info Link", () => OpenInfoLink(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Cover", () => OpenCover(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Title Snapshot", () => OpenTitleSnapshot(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Gameplay Snapshot", () => OpenGameplaySnapshot(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Cart", () => OpenCart(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Video", () => PlayVideo(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Manual", () => OpenManual(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Walkthrough", () => OpenWalkthrough(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Cabinet", () => OpenCabinet(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "Flyer", () => OpenFlyer(selectedResult.SystemName, selectedResult.FileName));
                    AddMenuItem(contextMenu, "PCB", () => OpenPcb(selectedResult.SystemName, selectedResult.FileName));

                    contextMenu.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddMenuItem(ContextMenu contextMenu, string header, Action action)
        {
            var menuItem = new MenuItem
            {
                Header = header
            };
            menuItem.Click += (_, _) => action();
            contextMenu.Items.Add(menuItem);
        }

        private void OpenVideoLink(string systemName, string fileName)
        {
            string searchTerm = $"{Path.GetFileNameWithoutExtension(fileName)} {systemName}";
            string searchUrl = $"{_settings.VideoUrl}{Uri.EscapeDataString(searchTerm)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = searchUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show($"There was a problem opening the Video Link.\n\nException details: {exception.Message}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenInfoLink(string systemName, string fileName)
        {
            string searchTerm = $"{Path.GetFileNameWithoutExtension(fileName)} {systemName}";
            string searchUrl = $"{_settings.InfoUrl}{Uri.EscapeDataString(searchTerm)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = searchUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show($"There was a problem opening the Info Link.\n\nException details: {exception.Message}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenCover(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var systemConfig = _systemConfigs.FirstOrDefault(config => config.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (systemConfig == null)
            {
                MessageBox.Show("System configuration not found for the selected file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
    
            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
    
            // Specific image path
            string systemImageFolder = systemConfig.SystemImageFolder ?? string.Empty;
            string systemSpecificDirectory = Path.Combine(baseDirectory, systemImageFolder);

            // Global image path
            string globalDirectory = Path.Combine(baseDirectory, "images", systemName);

            // Image extensions to look for
            string[] imageExtensions = [".png", ".jpg", ".jpeg"];

            // Search for the image file
            bool TryFindImage(string directory, out string foundPath)
            {
                foreach (var extension in imageExtensions)
                {
                    string imagePath = Path.Combine(directory, fileNameWithoutExtension + extension);
                    if (File.Exists(imagePath))
                    {
                        foundPath = imagePath;
                        return true;
                    }
                }
                foundPath = null;
                return false;
            }

            // First try to find the image in the specific directory
            if (TryFindImage(systemSpecificDirectory, out string foundImagePath))
            {
                var imageViewerWindow = new OpenImageFiles();
                imageViewerWindow.LoadImage(foundImagePath);
                imageViewerWindow.Show();
            }
            // If not found, try the global directory
            else if (TryFindImage(globalDirectory, out foundImagePath))
            {
                var imageViewerWindow = new OpenImageFiles();
                imageViewerWindow.LoadImage(foundImagePath);
                imageViewerWindow.Show();
            }
            else
            {
                MessageBox.Show("There is no cover associated with this file or button.", "Cover Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OpenTitleSnapshot(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string titleSnapshotDirectory = Path.Combine(baseDirectory, "title_snapshots", systemName);
            string[] titleSnapshotExtensions = [".png", ".jpg", ".jpeg"];
            
            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            foreach (var extension in titleSnapshotExtensions)
            {
                string titleSnapshotPath = Path.Combine(titleSnapshotDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(titleSnapshotPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(titleSnapshotPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no title snapshot associated with this file or button.", "Title Snapshot Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenGameplaySnapshot(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string gameplaySnapshotDirectory = Path.Combine(baseDirectory, "gameplay_snapshots", systemName);
            string[] gameplaySnapshotExtensions = [".png", ".jpg", ".jpeg"];
            
            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            foreach (var extension in gameplaySnapshotExtensions)
            {
                string gameplaySnapshotPath = Path.Combine(gameplaySnapshotDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(gameplaySnapshotPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(gameplaySnapshotPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no gameplay snapshot associated with this file or button.", "Gameplay Snapshot Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenCart(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string cartDirectory = Path.Combine(baseDirectory, "carts", systemName);
            string[] cartExtensions = [".png", ".jpg", ".jpeg"];

            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            
            foreach (var extension in cartExtensions)
            {
                string cartPath = Path.Combine(cartDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(cartPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(cartPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no cart associated with this file or button.", "Cart Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PlayVideo(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string videoDirectory = Path.Combine(baseDirectory, "videos", systemName);
            string[] videoExtensions = [".mp4", ".avi", ".mkv"];
            
            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            foreach (var extension in videoExtensions)
            {
                string videoPath = Path.Combine(videoDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(videoPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = videoPath,
                        UseShellExecute = true
                    });
                    return;
                }
            }

            MessageBox.Show("There is no video associated with this file or button.", "Video Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenManual(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string manualDirectory = Path.Combine(baseDirectory, "manuals", systemName);
            string[] manualExtensions = [".pdf"];
            
            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

            foreach (var extension in manualExtensions)
            {
                string manualPath = Path.Combine(manualDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(manualPath))
                {
                    try
                    {
                        // Use the default PDF viewer to open the file
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = manualPath,
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open the manual: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            MessageBox.Show("There is no manual associated with this file or button.", "Manual Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenWalkthrough(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string walkthroughDirectory = Path.Combine(baseDirectory, "walkthrough", systemName);
            string[] walkthroughExtensions = [".pdf"];

            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            
            foreach (var extension in walkthroughExtensions)
            {
                string walkthroughPath = Path.Combine(walkthroughDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(walkthroughPath))
                {
                    try
                    {
                        // Use the default PDF viewer to open the file
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = walkthroughPath,
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open the walkthrough: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            MessageBox.Show("There is no walkthrough associated with this file or button.", "Walkthrough Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenCabinet(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string cabinetDirectory = Path.Combine(baseDirectory, "cabinets", systemName);
            string[] cabinetExtensions = [".png", ".jpg", ".jpeg"];

            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            
            foreach (var extension in cabinetExtensions)
            {
                string cabinetPath = Path.Combine(cabinetDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(cabinetPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(cabinetPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no cabinet associated with this file or button.", "Cabinet Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenFlyer(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string flyerDirectory = Path.Combine(baseDirectory, "flyers", systemName);
            string[] flyerExtensions = [".png", ".jpg", ".jpeg"];

            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            
            foreach (var extension in flyerExtensions)
            {
                string flyerPath = Path.Combine(flyerDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(flyerPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(flyerPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no flyer associated with this file or button.", "Flyer Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenPcb(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string pcbDirectory = Path.Combine(baseDirectory, "pcbs", systemName);
            string[] pcbExtensions = [".png", ".jpg", ".jpeg"];

            // Remove the original file extension
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            
            foreach (var extension in pcbExtensions)
            {
                string pcbPath = Path.Combine(pcbDirectory, fileNameWithoutExtension + extension);
                if (File.Exists(pcbPath))
                {
                    var imageViewerWindow = new OpenImageFiles();
                    imageViewerWindow.LoadImage(pcbPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no PCB associated with this file or button.", "PCB Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchButton_Click(sender, e);
            }
        }

        public class SearchResult
        {
            public string FileName { get; init; }
            public string MachineName { get; init; }
            public string FolderName { get; init; }
            public string FilePath { get; init; }
            public double Size { get; set; }
            public string SystemName { get; init; }
            public SystemConfig.Emulator EmulatorConfig { get; init; }
            public int Score { get; set; }
        }

        private void GlobalSearch_Closed(object sender, EventArgs e)
        {
            _searchResults = null;
        }
    }
}