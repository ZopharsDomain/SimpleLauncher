using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleLauncher
{
    public partial class GlobalSearch
    {
        private readonly List<SystemConfig> _systemConfigs;
        private readonly List<MameConfig> _machines;
        private readonly SettingsConfig _settings;
        private ObservableCollection<SearchResult> _searchResults;
        private PleaseWaitSearch _pleaseWaitWindow;
        private DispatcherTimer _closeTimer;
        private readonly FavoritesManager _favoritesManager;
        private readonly MainWindow _mainWindow;

        public GlobalSearch(List<SystemConfig> systemConfigs, List<MameConfig> machines, SettingsConfig settings, MainWindow mainWindow)
        {
            InitializeComponent();
            _systemConfigs = systemConfigs;
            _machines = machines;
            _settings = settings;
            _searchResults = [];
            ResultsDataGrid.ItemsSource = _searchResults;
            Closed += GlobalSearch_Closed;
            _favoritesManager = new FavoritesManager();
            _mainWindow = mainWindow;
            
            // Apply the theme to this window
            App.ApplyThemeToWindow(this);
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
                    
                    string formattedException = $"That was an error using the Global Search window.\n\nException details: {args.Error.Message}";
                    Exception ex = new(formattedException);
                    Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                    logTask.Wait(TimeSpan.FromSeconds(2));

                    MessageBox.Show($"There was an error using the Global Search.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Search in machine descriptions first
            var machinesWithMatchingDescriptions = _machines
                .Where(m => MatchesSearchQuery(m.Description.ToLower(), searchTerms))
                .Select(m => m.MachineName)
                .ToList();

            // Search in filenames within all systems
            foreach (var systemConfig in _systemConfigs)
            {
                string systemFolderPath = GetFullPath(systemConfig.SystemFolder);

                if (Directory.Exists(systemFolderPath))
                {
                    var files = Directory.GetFiles(systemFolderPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => systemConfig.FileFormatsToSearch.Contains(Path.GetExtension(file).TrimStart('.').ToLower()))
                        .Where(file => MatchesSearchQuery(Path.GetFileName(file).ToLower(), searchTerms) ||
                                       (systemConfig.SystemIsMame && machinesWithMatchingDescriptions.Any(machineName => Path.GetFileNameWithoutExtension(file).Equals(machineName, StringComparison.OrdinalIgnoreCase))))
                        .Select(file => new SearchResult
                        {
                            FileName = Path.GetFileName(file),
                            FolderName = Path.GetDirectoryName(file)?.Split(Path.DirectorySeparatorChar).Last(),
                            FilePath = file,
                            Size = Math.Round(new FileInfo(file).Length / 1024.0, 2),
                            MachineName = GetMachineDescription(Path.GetFileNameWithoutExtension(file)),
                            SystemName = systemConfig.SystemName,
                            EmulatorConfig = systemConfig.Emulators.FirstOrDefault(),
                            CoverImage = GetCoverImagePath(systemConfig.SystemName, Path.GetFileName(file)) // Set cover image path
                        })
                        .ToList();

                    results.AddRange(files);
                }
            }

            var scoredResults = ScoreResults(results, searchTerms);
            return scoredResults;
        }
        
        private string GetCoverImagePath(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var systemConfig = _systemConfigs.FirstOrDefault(config => config.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (systemConfig == null)
            {
                return Path.Combine(baseDirectory, "images", "default.png");
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
                return foundImagePath;
            }
            // If not found, try the global directory
            else if (TryFindImage(globalDirectory, out foundImagePath))
            {
                return foundImagePath;
            }
            else
            {
                return Path.Combine(baseDirectory, "images", "default.png");
            }
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
                    string formattedException = $"That was an error trying to launch a game from the search result in the Global Search window.\n\nThere is no System or Emulator associated with the game.";
                    Exception ex = new(formattedException);
                    await LogErrors.LogErrorAsync(ex, formattedException);
                                        
                    MessageBox.Show("There was an error launching the selected game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var systemConfig = _systemConfigs.FirstOrDefault(config =>
                    config.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));

                if (systemConfig == null)
                {
                    string formattedException = $"That was an error trying to launch a game from the search result in the Global Search window.\n\nSystem configuration not found for the selected game.\nThe error was reported to the developer that will fix the issue.";
                    Exception exception = new(formattedException);
                    await LogErrors.LogErrorAsync(exception, formattedException);
                    
                    MessageBox.Show("There was an error launching the selected game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var mockSystemComboBox = new ComboBox();
                var mockEmulatorComboBox = new ComboBox();

                mockSystemComboBox.ItemsSource = _systemConfigs.Select(config => config.SystemName).ToList();
                mockSystemComboBox.SelectedItem = systemConfig.SystemName;

                mockEmulatorComboBox.ItemsSource = systemConfig.Emulators.Select(emulator => emulator.EmulatorName).ToList();
                mockEmulatorComboBox.SelectedItem = emulatorConfig.EmulatorName;

                await GameLauncher.HandleButtonClick(filePath, mockEmulatorComboBox, mockSystemComboBox, _systemConfigs, _settings, _mainWindow);
            }
            catch (Exception ex)
            {
                string formattedException = $"There was an error launching the game from the Global Search window.\n\nFile Path: {filePath}\nSystem Name: {systemName}\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                await LogErrors.LogErrorAsync(ex, formattedException);
                
                MessageBox.Show($"There was an error launching the selected game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    PlayClick.PlayClickSound();
                    LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                }
                else
                {
                    MessageBox.Show("Please select a game to launch.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                string formattedException = $"That was an error launching a game from the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));

                MessageBox.Show($"There was an error launching the selected game.\n\nThe error was reported to the developer that will try to fix the issue. ", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResultsDataGrid_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    var contextMenu = new ContextMenu();

                    // "Launch Selected Game" MenuItem
                    var launchIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/launch.png")),
                        Width = 16,
                        Height = 16
                    };
                    var launchMenuItem = new MenuItem
                    {
                        Header = "Launch Selected Game",
                        Icon = launchIcon
                    };
                    launchMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                    };

                    // "Add To Favorites" MenuItem
                    var addToFavoritesIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/heart.png")),
                        Width = 16,
                        Height = 16
                    };
                    var addToFavoritesMenuItem = new MenuItem
                    {
                        Header = "Add To Favorites",
                        Icon = addToFavoritesIcon
                    };
                    addToFavoritesMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        AddToFavorites(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Open Video Link" MenuItem
                    var videoLinkIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/video.png")),
                        Width = 16,
                        Height = 16
                    };
                    var videoLinkMenuItem = new MenuItem
                    {
                        Header = "Open Video Link",
                        Icon = videoLinkIcon
                    };
                    videoLinkMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenVideoLink(selectedResult.SystemName, selectedResult.FileName, selectedResult.MachineName);
                    };

                    // "Open Info Link" MenuItem
                    var infoLinkIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/info.png")),
                        Width = 16,
                        Height = 16
                    };
                    var infoLinkMenuItem = new MenuItem
                    {
                        Header = "Open Info Link",
                        Icon = infoLinkIcon
                    };
                    infoLinkMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenInfoLink(selectedResult.SystemName, selectedResult.FileName, selectedResult.MachineName);
                    };
                    
                    // "Open ROM History" MenuItem
                    var openHistoryIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/romhistory.png", UriKind.RelativeOrAbsolute)),
                        Width = 16,
                        Height = 16
                    };
                    var openHistoryMenuItem = new MenuItem
                    {
                        Header = "Open ROM History",
                        Icon = openHistoryIcon
                    };
                    openHistoryMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(selectedResult.FileName);
                        var systemConfig = _systemConfigs.FirstOrDefault(config => config.SystemName.Equals(selectedResult.SystemName, StringComparison.OrdinalIgnoreCase));
                        OpenHistoryWindow(selectedResult.SystemName, fileNameWithoutExtension, systemConfig);
                    };

                    // "Cover" MenuItem
                    var coverIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/cover.png")),
                        Width = 16,
                        Height = 16
                    };
                    var coverMenuItem = new MenuItem
                    {
                        Header = "Cover",
                        Icon = coverIcon
                    };
                    coverMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenCover(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Title Snapshot" MenuItem
                    var titleSnapshotIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/snapshot.png")),
                        Width = 16,
                        Height = 16
                    };
                    var titleSnapshotMenuItem = new MenuItem
                    {
                        Header = "Title Snapshot",
                        Icon = titleSnapshotIcon
                    };
                    titleSnapshotMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenTitleSnapshot(selectedResult.SystemName, selectedResult.FileName);
                    };
                    
                    // "Gameplay Snapshot" MenuItem
                    var gameplaySnapshotIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/snapshot.png")),
                        Width = 16,
                        Height = 16
                    };
                    var gameplaySnapshotMenuItem = new MenuItem
                    {
                        Header = "Gameplay Snapshot",
                        Icon = gameplaySnapshotIcon
                    };
                    gameplaySnapshotMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenGameplaySnapshot(selectedResult.SystemName, selectedResult.FileName);
                    };
                    
                    // "Cart" MenuItem
                    var cartIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/cart.png")),
                        Width = 16,
                        Height = 16
                    };
                    var cartMenuItem = new MenuItem
                    {
                        Header = "Cart",
                        Icon = cartIcon
                    };
                    cartMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenCart(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Video" MenuItem
                    var videoIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/video.png")),
                        Width = 16,
                        Height = 16
                    };
                    var videoMenuItem = new MenuItem
                    {
                        Header = "Video",
                        Icon = videoIcon
                    };
                    videoMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        PlayVideo(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Manual" MenuItem
                    var manualIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/manual.png")),
                        Width = 16,
                        Height = 16
                    };
                    var manualMenuItem = new MenuItem
                    {
                        Header = "Manual",
                        Icon = manualIcon
                    };
                    manualMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenManual(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Walkthrough" MenuItem
                    var walkthroughIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/walkthrough.png")),
                        Width = 16,
                        Height = 16
                    };
                    var walkthroughMenuItem = new MenuItem
                    {
                        Header = "Walkthrough",
                        Icon = walkthroughIcon
                    };
                    walkthroughMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenWalkthrough(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Cabinet" MenuItem
                    var cabinetIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/cabinet.png")),
                        Width = 16,
                        Height = 16
                    };
                    var cabinetMenuItem = new MenuItem
                    {
                        Header = "Cabinet",
                        Icon = cabinetIcon
                    };
                    cabinetMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenCabinet(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "Flyer" MenuItem
                    var flyerIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/flyer.png")),
                        Width = 16,
                        Height = 16
                    };
                    var flyerMenuItem = new MenuItem
                    {
                        Header = "Flyer",
                        Icon = flyerIcon
                    };
                    flyerMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenFlyer(selectedResult.SystemName, selectedResult.FileName);
                    };

                    // "PCB" MenuItem
                    var pcbIcon = new Image
                    {
                        Source = new BitmapImage(new Uri("pack://application:,,,/images/pcb.png")),
                        Width = 16,
                        Height = 16
                    };
                    var pcbMenuItem = new MenuItem
                    {
                        Header = "PCB",
                        Icon = pcbIcon
                    };
                    pcbMenuItem.Click += (_, _) =>
                    {
                        PlayClick.PlayClickSound();
                        OpenPcb(selectedResult.SystemName, selectedResult.FileName);
                    };

                    contextMenu.Items.Add(launchMenuItem);
                    contextMenu.Items.Add(addToFavoritesMenuItem);
                    contextMenu.Items.Add(videoLinkMenuItem);
                    contextMenu.Items.Add(infoLinkMenuItem);
                    contextMenu.Items.Add(openHistoryMenuItem);
                    contextMenu.Items.Add(coverMenuItem);
                    contextMenu.Items.Add(titleSnapshotMenuItem);
                    contextMenu.Items.Add(gameplaySnapshotMenuItem);
                    contextMenu.Items.Add(cartMenuItem);
                    contextMenu.Items.Add(videoMenuItem);
                    contextMenu.Items.Add(manualMenuItem);
                    contextMenu.Items.Add(walkthroughMenuItem);
                    contextMenu.Items.Add(cabinetMenuItem);
                    contextMenu.Items.Add(flyerMenuItem);
                    contextMenu.Items.Add(pcbMenuItem);

                    contextMenu.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                string formattedException = $"There was an error in the right-click context menu in the global search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));

                MessageBox.Show("There was an error in the right-click context menu.\n\nThe error was reported to the developer, who will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void AddToFavorites(string systemName, string fileNameWithoutExtension)
        {
            try
            {
                // Load existing favorites
                FavoritesConfig favorites = _favoritesManager.LoadFavorites();

                // Add the new favorite if it doesn't already exist
                if (!favorites.FavoriteList.Any(f => f.FileName.Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase)
                                                     && f.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase)))
                {
                    favorites.FavoriteList.Add(new Favorite
                    {
                        FileName = fileNameWithoutExtension,
                        SystemName = systemName
                    });

                    // Save the updated favorites list
                    _favoritesManager.SaveFavorites(favorites);

                    MessageBox.Show($"{fileNameWithoutExtension} has been added to favorites.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"{fileNameWithoutExtension} is already in favorites.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                string formattedException = $"An error occurred while adding game to favorites in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));
                
                MessageBox.Show($"An error occurred while adding the game to the favorites.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenVideoLink(string systemName, string fileName, string machineDescription = null)
        {
            var searchTerm =
                // Check if machineDescription is provided and not empty
                !string.IsNullOrEmpty(machineDescription) ? $"{machineDescription} {systemName}" : $"{Path.GetFileNameWithoutExtension(fileName)} {systemName}";

            string searchUrl = $"{_settings.VideoUrl}{Uri.EscapeDataString(searchTerm)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = searchUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                string formattedException = $"There was a problem opening the Video Link in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));
                
                MessageBox.Show($"There was a problem opening the Video Link.\n\nThe problem was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenInfoLink(string systemName, string fileName, string machineDescription = null)
        {
            var searchTerm =
                // Check if machineDescription is provided and not empty
                !string.IsNullOrEmpty(machineDescription) ? $"{machineDescription} {systemName}" : $"{Path.GetFileNameWithoutExtension(fileName)} {systemName}";

            string searchUrl = $"{_settings.InfoUrl}{Uri.EscapeDataString(searchTerm)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = searchUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                string formattedException = $"There was a problem opening the Info Link in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));
                
                MessageBox.Show($"There was a problem opening the Info Link.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void OpenHistoryWindow(string systemName, string fileNameWithoutExtension, SystemConfig systemConfig)
        {
            string romName = fileNameWithoutExtension.ToLowerInvariant();
           
            // Attempt to find a matching machine description
            string searchTerm = fileNameWithoutExtension;
            var machine = _machines.FirstOrDefault(m => m.MachineName.Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));
            if (machine != null && !string.IsNullOrWhiteSpace(machine.Description))
            {
                searchTerm = machine.Description;
            }

            try
            {
                var historyWindow = new RomHistoryWindow(romName, systemName, searchTerm, systemConfig);
                historyWindow.Show();

            }
            catch (Exception ex)
            {
                string contextMessage = $"There was a problem opening the History window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
                logTask.Wait(TimeSpan.FromSeconds(2));
                
                MessageBox.Show($"There was a problem opening the History window.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenCover(string systemName, string fileName)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var systemConfig = _systemConfigs.FirstOrDefault(config => config.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));
            if (systemConfig == null)
            {
                string formattedException = $"System configuration not found for the selected game in the Global Search window while using the OpenCover method.";
                Exception ex = new(formattedException);
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));
                
                MessageBox.Show("There was a problem opening the Cover Image for this game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var imageViewerWindow = new ImageViewerWindow();
                imageViewerWindow.LoadImage(foundImagePath);
                imageViewerWindow.Show();
            }
            // If not found, try the global directory
            else if (TryFindImage(globalDirectory, out foundImagePath))
            {
                var imageViewerWindow = new ImageViewerWindow();
                imageViewerWindow.LoadImage(foundImagePath);
                imageViewerWindow.Show();
            }
            else
            {
                MessageBox.Show("There is no cover associated with this game.", "Cover not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(titleSnapshotPath);
                    imageViewerWindow.Show();
                    return;
                }
            }

            MessageBox.Show("There is no title snapshot file associated with this game.", "Title Snapshot not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(gameplaySnapshotPath);
                    imageViewerWindow.Show();
                    return;
                }
            }
            MessageBox.Show("There is no gameplay snapshot file associated with this game.", "Gameplay Snapshot not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(cartPath);
                    imageViewerWindow.Show();
                    return;
                }
            }
            MessageBox.Show("There is no cart file associated with this game.", "Cart not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("There is no video file associated with this game.", "Video not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                        string formattedException = $"Failed to open the manual in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                        Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                        logTask.Wait(TimeSpan.FromSeconds(2));
                        
                        MessageBox.Show($"Failed to open the manual for this game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            MessageBox.Show("There is no manual associated with this game.", "Manual not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                        string formattedException = $"Failed to open the walkthrough file in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                        Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                        logTask.Wait(TimeSpan.FromSeconds(2));
                        
                        MessageBox.Show($"Failed to open the walkthrough file for this game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            MessageBox.Show("There is no walkthrough file associated with this game.", "Walkthrough not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(cabinetPath);
                    imageViewerWindow.Show();
                    return;
                }
            }
            MessageBox.Show("There is no cabinet file associated with this game.", "Cabinet not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(flyerPath);
                    imageViewerWindow.Show();
                    return;
                }
            }
            MessageBox.Show("There is no flyer file associated with this game.", "Flyer not found", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    var imageViewerWindow = new ImageViewerWindow();
                    imageViewerWindow.LoadImage(pcbPath);
                    imageViewerWindow.Show();
                    return;
                }
            }
            MessageBox.Show("There is no PCB file associated with this game.", "PCB not found", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
                {
                    PlayClick.PlayClickSound();

                    LaunchGameFromSearchResult(selectedResult.FilePath, selectedResult.SystemName, selectedResult.EmulatorConfig);
                }
            }
            catch (Exception ex)
            {
                string formattedException = $"There was an error while using the method MouseDoubleClick in the Global Search window.\n\nException type: {ex.GetType().Name}\nException details: {ex.Message}";
                Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
                logTask.Wait(TimeSpan.FromSeconds(2));

                MessageBox.Show($"The application could not launch this game.\n\nThe error was reported to the developer that will try to fix the issue.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            public string CoverImage { get; init; }
        }
        
        private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem is SearchResult selectedResult)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(selectedResult.CoverImage, UriKind.Absolute);
                bitmap.EndInit();
                PreviewImage.Source = bitmap;
            }
            else
            {
                PreviewImage.Source = null;
            }
        }

        private void GlobalSearch_Closed(object sender, EventArgs e)
        {
            _searchResults = null;
        }
    }
}