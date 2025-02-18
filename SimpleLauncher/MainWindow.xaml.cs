﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using ControlzEx.Theming;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SimpleLauncher;

public partial class MainWindow : INotifyPropertyChanged
{
    // Declare Controller Detection
    private DispatcherTimer _controllerCheckTimer;     

    // Declare CacheManager and CacheFiles
    private readonly CacheManager _cacheManager = new();
    private List<string> _cachedFiles;

    // Declare GameListItems
    // Used in ListView Mode
    public ObservableCollection<GameListFactory.GameListViewItem> GameListItems { get; set; } = [];     
    
    // Declare System Name and PlayTime in the Statusbar
    // _selectedSystem is the selected system from ComboBox
    public event PropertyChangedEventHandler PropertyChanged;
    private string _selectedSystem;
    private string _playTime;
    public string SelectedSystem
    {
        get => _selectedSystem;
        set
        {
            _selectedSystem = value;
            OnPropertyChanged(nameof(SelectedSystem));
        }
    }
    public string PlayTime
    {
        get => _playTime;
        set
        {
            _playTime = value;
            OnPropertyChanged(nameof(PlayTime));
        }
    }
    private void OnPropertyChanged(string propertyName) // Update UI on OnPropertyChanged
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
       
    // Define Tray Icon
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _trayMenu;
        
    // Define Pagination Related Variables
    private int _currentPage = 1;
    private int _filesPerPage;
    private int _totalFiles;
    private int _paginationThreshold;
    private Button _nextPageButton;
    private Button _prevPageButton;
    private string _currentFilter;

    // Define _currentSearchResults
    private List<string> _currentSearchResults = [];

    // Define and Instantiate variables
    private readonly List<SystemConfig> _systemConfigs;
    private readonly LetterNumberMenu _letterNumberMenu = new();
    private readonly GameListFactory _gameListFactory;
    private readonly WrapPanel _gameFileGrid;
    private GameButtonFactory _gameButtonFactory;
    private readonly SettingsConfig _settings = new();
    private FavoritesManager _favoritesManager;
    private readonly List<MameConfig> _machines;
    private readonly Dictionary<string, string> _mameLookup; // Used for faster lookup of MAME machine names
    private string _selectedImageFolder;
    private string _selectedRomFolder;
    
    // Define the LogPath
    private readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_user.log");
        
    public MainWindow()
    {
        InitializeComponent();
        
        // Check for Command-line Args
        // Show UpdateHistory after the MainWindow is fully loaded
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("whatsnew"))
        {
            Loaded += (_, _) => OpenUpdateHistory();
        }

        // DataContext set to the MainWindow instance
        DataContext = this;

        // Load and Apply _settings
        ToggleGamepad.IsChecked = _settings.EnableGamePadNavigation;
        UpdateMenuCheckMarks(_settings.ThumbnailSize);
        UpdateMenuCheckMarks2(_settings.GamesPerPage);
        UpdateMenuCheckMarks3(_settings.ShowGames);
        _filesPerPage = _settings.GamesPerPage;
        _paginationThreshold = _settings.GamesPerPage;
        
        // Load _machines and _mameLookup
        _machines = MameConfig.LoadFromXml();
        _mameLookup = _machines
            .GroupBy(m => m.MachineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);

        // Load and Sort _systemConfigs
        _systemConfigs = SystemConfig.LoadSystemConfigs();
        var sortedSystemNames = _systemConfigs.Select(config => config.SystemName).OrderBy(name => name).ToList();
        SystemComboBox.ItemsSource = sortedSystemNames;

        // Initialize the GamePadController
        GamePadController.Instance2.ErrorLogger = (ex, msg) =>
            LogErrors.LogErrorAsync(ex, msg).Wait(TimeSpan.FromSeconds(2));
        if (_settings.EnableGamePadNavigation)
        {
            GamePadController.Instance2.Start();
        }
        else
        {
            GamePadController.Instance2.Stop();
        }

        // Add _letterNumberMenu to the UI
        LetterNumberMenu.Children.Clear();
        LetterNumberMenu.Children.Add(_letterNumberMenu.LetterPanel);
        // Create and integrate LetterNumberMenu
        _letterNumberMenu.OnLetterSelected += async selectedLetter =>
        {
            ResetPaginationButtons(); // Ensure pagination is reset at the beginning
            SearchTextBox.Text = "";  // Clear SearchTextBox
            _currentFilter = selectedLetter; // Update current filter
            await LoadGameFilesAsync(selectedLetter); // Load games
        };
        _letterNumberMenu.OnFavoritesSelected += async () =>
        {
            ResetPaginationButtons();
            SearchTextBox.Text = ""; // Clear search field
            _currentFilter = null; // Clear any active filter

            // Filter favorites for the selected system and store them in _currentSearchResults
            var favoriteGames = GetFavoriteGamesForSelectedSystem();
            if (favoriteGames.Count != 0)
            {
                _currentSearchResults = favoriteGames.ToList(); // Store only favorite games in _currentSearchResults
                await LoadGameFilesAsync(null, "FAVORITES"); // Call LoadGameFilesAsync
            }
            else
            {
                AddNoFilesMessage();
                MessageBoxLibrary.NoFavoriteFoundMessageBox();
            }
        };
            
        // Initialize _favoritesManager
        _favoritesManager = FavoritesManager.LoadFavorites();

        // Initialize _gameFileGrid
        _gameFileGrid = FindName("GameFileGrid") as WrapPanel;
        
        // Initialize _gameButtonFactory
        _gameButtonFactory = new GameButtonFactory(EmulatorComboBox, SystemComboBox, _systemConfigs, _machines, _settings, _favoritesManager, _gameFileGrid, this);
            
        // Initialize _gameListFactory
        _gameListFactory = new GameListFactory(EmulatorComboBox, SystemComboBox, _systemConfigs, _machines, _settings, _favoritesManager, this);

        // Check for Updates
        Loaded += async (_, _) => await UpdateChecker.CheckForUpdatesAsync(this);
            
        // Call Stats API
        Loaded += async (_, _) => await Stats.CallApiAsync();

        // Attach the Load and Close events
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply language
        SetLanguageAndCheckMenu(_settings.Language);
        
        // Apply Theme
        App.ChangeTheme(_settings.BaseTheme, _settings.AccentColor);
        SetCheckedTheme(_settings.BaseTheme, _settings.AccentColor);
        
        // Load previous windows state
        Width = _settings.MainWindowWidth;
        Height = _settings.MainWindowHeight;
        Top = _settings.MainWindowTop;
        Left = _settings.MainWindowLeft;
        WindowState = Enum.Parse<WindowState>(_settings.MainWindowState);

        // Set the initial SelectedSystem and PlayTime
        var nosystemselected = (string)Application.Current.TryFindResource("Nosystemselected") ?? "No system selected";
        SelectedSystem = nosystemselected;
        PlayTime = "00:00:00";

        // Set the initial ViewMode based on the _settings 
        SetViewMode(_settings.ViewMode);
        
        // Check if a system is already selected, otherwise show the message
        if (SystemComboBox.SelectedItem == null)
        {
            AddNoSystemMessage();
        }
        
        // Check if application has write access
        if (!CheckIfDirectoryIsWritable.IsWritableDirectory(AppDomain.CurrentDomain.BaseDirectory))
        {
            MessageBoxLibrary.MoveToWritableFolderMessageBox();
        }
        
        // Set initial pagination state
        PrevPageButton.IsEnabled = false;
        NextPageButton.IsEnabled = false;
        _prevPageButton = PrevPageButton;
        _nextPageButton = NextPageButton;
        
        // Update the GamePadController dead zone settings from SettingsConfig
        GamePadController.Instance2.DeadZoneX = _settings.DeadZoneX;
        GamePadController.Instance2.DeadZoneY = _settings.DeadZoneY;
        
        InitializeControllerDetection();
        InitializeTrayIcon();
    }
    
    private void InitializeControllerDetection()
    {
        _controllerCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5) // Check every 5 seconds
        };
        _controllerCheckTimer.Tick += GamePadControllerCheckTimer_Tick;
        _controllerCheckTimer.Start();
    }
    
    private static void GamePadControllerCheckTimer_Tick(object sender, EventArgs e)
    {
        GamePadController.Instance2.CheckAndReconnectControllers();
    }

    private static void OpenUpdateHistory()
    {
        var updateHistoryWindow = new UpdateHistory();
        updateHistoryWindow.Show();
    }

    private void SaveApplicationSettings()
    {
        // Save application's window state
        _settings.MainWindowWidth = (int)Width;
        _settings.MainWindowHeight = (int)Height;
        _settings.MainWindowTop = (int)Top;
        _settings.MainWindowLeft = (int)Left;
        _settings.MainWindowState = WindowState.ToString();

        // Save application's current state
        _settings.ThumbnailSize = _gameButtonFactory.ImageHeight;
        _settings.GamesPerPage = _filesPerPage;
        _settings.ShowGames = _settings.ShowGames;
        _settings.EnableGamePadNavigation = ToggleGamepad.IsChecked;

        // Save theme settings
        var detectedTheme = ThemeManager.Current.DetectTheme(this);
        if (detectedTheme != null)
        {
            _settings.BaseTheme = detectedTheme.BaseColorScheme;
            _settings.AccentColor = detectedTheme.ColorScheme;
        }

        _settings.Save();
    }

    private void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        SaveApplicationSettings();
        
        // Delete temp folders and files before close
        CleanSimpleLauncherFolder.CleanupTrash();
        
        // Dispose gamepad resources
        GamePadController.Instance2.Stop();
        GamePadController.Instance2.Dispose();
    }

    // Used in cases that need to reload system.xml or update the pagination settings
    // Used when user set the Language
    // Used when user set the GamesPerPage
    private void MainWindow_Restart()
    {
        SaveApplicationSettings();

        var processModule = Process.GetCurrentProcess().MainModule;
        if (processModule == null) return;
        var startInfo = new ProcessStartInfo
        {
            FileName = processModule.FileName,
            UseShellExecute = true
        };

        Process.Start(startInfo);

        Application.Current.Shutdown();
        Environment.Exit(0);
    }
        
    private List<string> GetFavoriteGamesForSelectedSystem()
    {
        // Reload favorites to ensure we have the latest data
        _favoritesManager = FavoritesManager.LoadFavorites();
            
        var selectedSystem = SystemComboBox.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(selectedSystem))
        {
            return []; // Return an empty list if there is no favorite for that system
        }

        // Retrieve the system configuration for the selected system
        var selectedConfig = _systemConfigs.FirstOrDefault(c => c.SystemName.Equals(selectedSystem, StringComparison.OrdinalIgnoreCase));
        if (selectedConfig == null)
        {
            return []; // Return an empty list if there is no favorite for that system
        }
        var systemFolderPath = selectedConfig.SystemFolder;

        // Filter the favorites and build the full file path for each favorite game
        var favoriteGamePaths = _favoritesManager.FavoriteList
            .Where(fav => fav.SystemName.Equals(selectedSystem, StringComparison.OrdinalIgnoreCase))
            .Select(fav => Path.Combine(systemFolderPath, fav.FileName))
            .ToList();

        return favoriteGamePaths;
    }
    
    private static Task ShowPleaseWaitWindowAsync(Window window)
    {
        return Task.Run(() =>
        {
            window.Dispatcher.Invoke(window.Show);
        });
    }

    private static Task ClosePleaseWaitWindowAsync(Window window)
    {
        return Task.Run(() =>
        {
            window.Dispatcher.Invoke(window.Close);
        });
    }
    
    private void GameListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameDataGrid.SelectedItem is not GameListFactory.GameListViewItem selectedItem) return;
        var gameListViewFactory = new GameListFactory(EmulatorComboBox, SystemComboBox, _systemConfigs, _machines, _settings, _favoritesManager, this);
        gameListViewFactory.HandleSelectionChanged(selectedItem);
    }

    // Used on the Game List Mode
    private async void GameListDoubleClickOnSelectedItem(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (GameDataGrid.SelectedItem is GameListFactory.GameListViewItem selectedItem)
            {
                // Delegate the double-click handling to GameListFactory
                await _gameListFactory.HandleDoubleClick(selectedItem);
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error while using the method GameListDoubleClickOnSelectedItem.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.MethodErrorMessageBox();
        }
    }
    
    private async void SystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            SearchTextBox.Text = ""; // Empty search field
            EmulatorComboBox.ItemsSource = null; // Null selected emulator
            EmulatorComboBox.SelectedIndex = -1; // No emulator selected
            PreviewImage.Source = null; // Empty PreviewImage
            
            // Clear search results
            _currentSearchResults.Clear();
            
            // Hide ListView
            GameFileGrid.Visibility = Visibility.Visible;
            ListViewPreviewArea.Visibility = Visibility.Collapsed;

            if (SystemComboBox.SelectedItem != null)
            {
                var selectedSystem = SystemComboBox.SelectedItem.ToString();
                var selectedConfig = _systemConfigs.FirstOrDefault(c => c.SystemName == selectedSystem);

                if (selectedConfig != null)
                {
                    // Populate EmulatorComboBox with the emulators for the selected system
                    EmulatorComboBox.ItemsSource = selectedConfig.Emulators.Select(emulator => emulator.EmulatorName).ToList();

                    // Select the first emulator
                    if (EmulatorComboBox.Items.Count > 0)
                    {
                        EmulatorComboBox.SelectedIndex = 0;
                    }
                    
                    // Update the selected system property
                    SelectedSystem = selectedSystem;
                
                    // Retrieve the playtime for the selected system
                    var systemPlayTime = _settings.SystemPlayTimes.FirstOrDefault(s => s.SystemName == selectedSystem);
                    PlayTime = systemPlayTime != null ? systemPlayTime.PlayTime : "00:00:00";

                    // Display the system info
                    var systemFolderPath = selectedConfig.SystemFolder;
                    var fileExtensions = selectedConfig.FileFormatsToSearch.Select(ext => $"{ext}").ToList();
                    var gameCount = FileManager.CountFiles(systemFolderPath, fileExtensions);
                
                    // Display SystemInfo for that system
                    SystemManager.DisplaySystemInfo(systemFolderPath, gameCount, selectedConfig, _gameFileGrid);
                    
                    // Update Image Folder and Rom Folder Variables
                    _selectedRomFolder = selectedConfig.SystemFolder;
                    _selectedImageFolder = string.IsNullOrWhiteSpace(selectedConfig.SystemImageFolder) 
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", selectedConfig.SystemName) 
                        : selectedConfig.SystemImageFolder;
                    
                    // Call DeselectLetter to clear any selected letter
                    _letterNumberMenu.DeselectLetter();
                    
                    ResetPaginationButtons();
                    
                    // Load files from cache or rescan if needed
                    _cachedFiles = await _cacheManager.LoadSystemFilesAsync(selectedSystem, systemFolderPath, fileExtensions, gameCount);
                }
                else
                {
                    AddNoSystemMessage();
                }
            }
            else
            {
                AddNoSystemMessage();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error in the method SystemComboBox_SelectionChanged.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);
        }
    }

    private void AddNoSystemMessage()
    {
        var noSystemMessage = (string)Application.Current.TryFindResource("NoSystemMessage") ?? "Please select a System";
        // Check the current view mode
        if (_settings.ViewMode == "GridView")
        {
            GameFileGrid.Children.Clear();
            GameFileGrid.Children.Add(new TextBlock
            {
                Text = $"\n{noSystemMessage}",
                Padding = new Thickness(10)
            });
        }
        else
        {
            // For List view, clear GameListItems
            GameListItems.Clear();
            GameListItems.Add(new GameListFactory.GameListViewItem
            {
                FileName = noSystemMessage,
                MachineDescription = string.Empty
            });
        }

        // Deselect any selected letter when no system is selected
        _letterNumberMenu.DeselectLetter();
    }
        
    private void AddNoFilesMessage()
    {
        var noGamesMatched = (string)Application.Current.TryFindResource("nogamesmatched") ?? "Unfortunately, no games matched your search query or the selected button.";
        // Check the current view mode
        if (_settings.ViewMode == "GridView")
        {
            // Clear existing content in Grid view and add the message
            GameFileGrid.Children.Clear();
            GameFileGrid.Children.Add(new TextBlock
            {
                Text = $"\n{noGamesMatched}",
                Padding = new Thickness(10)
            });
        }
        else
        {
            // For List view, clear GameListItems
            GameListItems.Clear();
            GameListItems.Add(new GameListFactory.GameListViewItem
            {
                FileName = noGamesMatched,
                MachineDescription = string.Empty
            });
        }

        // Deselect any selected letter when no system is selected
        _letterNumberMenu.DeselectLetter();
    }

    public async Task LoadGameFilesAsync(string startLetter = null, string searchQuery = null)
    {
        // Move scroller to top
        Scroller.Dispatcher.Invoke(() => Scroller.ScrollToTop());
            
        // Clear PreviewImage
        PreviewImage.Source = null;

        // Clear Game Grid
        GameFileGrid.Dispatcher.Invoke(() => GameFileGrid.Children.Clear());
            
        // Clear the Game List
        await Dispatcher.InvokeAsync(() => GameListItems.Clear());
            
        // Set ViewMode based on user preference
        if (_settings.ViewMode == "GridView")
        {
            // Allow GridView
            GameFileGrid.Visibility = Visibility.Visible;
            ListViewPreviewArea.Visibility = Visibility.Collapsed;                
        }
        else
        {
            // Allow ListView
            GameFileGrid.Visibility = Visibility.Collapsed;
            ListViewPreviewArea.Visibility = Visibility.Visible;
        }

        try
        {
            if (CheckIfSystemComboBoxIsNotNull()) return;
 
            var selectedSystem = SystemComboBox.SelectedItem.ToString();
            var selectedConfig = _systemConfigs.FirstOrDefault(c => c.SystemName == selectedSystem);
            
            if (await CheckIfSelectConfigIsNull(selectedConfig)) return;
            Debug.Assert(selectedConfig != null, nameof(selectedConfig) + " != null");
            
            // Create allFiles list
            List<string> allFiles;
                
            // If we are in "FAVORITES" mode, use '_currentSearchResults'
            if (searchQuery == "FAVORITES" && _currentSearchResults != null && _currentSearchResults.Count != 0)
            {
                allFiles = _currentSearchResults;
            }

            // Regular behavior: load files based on startLetter or searchQuery
            else
            {
                var systemFolderPath = selectedConfig.SystemFolder;
                var fileExtensions = selectedConfig.FileFormatsToSearch.Select(ext => $"*.{ext}").ToList();
                
                // Attempt to use the cached file list first
                _cachedFiles = _cacheManager.GetCachedFiles(selectedSystem);

                if (_cachedFiles is { Count: > 0 })
                {
                    allFiles = _cachedFiles;
                }
                else
                {
                    // Fall back to scanning the folder if no cache is available
                    allFiles = await FileManager.GetFilesAsync(systemFolderPath, fileExtensions);
                }
                
                // Filter by TopMenu Letter if specified
                if (!string.IsNullOrWhiteSpace(startLetter))
                {
                    allFiles = await FileManager.FilterFilesAsync(allFiles, startLetter);
                }

                // Process search query (from SearchBox)
                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    // If _currentSearchResults already exists, use it
                    if (_currentSearchResults != null && _currentSearchResults.Count != 0)
                    {
                        allFiles = _currentSearchResults;
                    }
                    else
                    {
                        // Check if the system is MAME-based
                        var systemIsMame = selectedConfig.SystemIsMame;

                        // If a system is MAME-based, use the pre-built _mameLookup dictionary for faster lookups.
                        if (systemIsMame && _mameLookup != null)
                        {
                            // Use a case-insensitive comparison.
                            var lowerQuery = searchQuery.ToLowerInvariant();
                            allFiles = await Task.Run(() =>
                                allFiles.FindAll(file =>
                                {
                                    var fileName = Path.GetFileNameWithoutExtension(file);
                                    // Check if the filename contains the search query.
                                    var filenameMatch = fileName.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase);
                                    if (filenameMatch)
                                        return true;

                                    // Lookup in the dictionary.
                                    if (_mameLookup.TryGetValue(fileName, out var description))
                                    {
                                        return description.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase);
                                    }

                                    return false;
                                }).ToList());
                        }
                        else
                        {
                            // For non-MAME systems, use the original filtering by filename.
                            allFiles = await Task.Run(() =>
                                allFiles.FindAll(file =>
                                {
                                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                                    return fileNameWithoutExtension.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                                }).ToList());
                        }

                        // Create the search results
                        _currentSearchResults = allFiles;
                    }
                }
            }

            // Sort the collection of files
            allFiles.Sort();

            // Count the collection of files
            _totalFiles = allFiles.Count;

            // Calculate the indices of files displayed on the current page
            var startIndex = (_currentPage - 1) * _filesPerPage + 1; // +1 because we are dealing with a 1-based index for displaying
            var endIndex = startIndex + _filesPerPage; // Actual number of files loaded on this page
            if (endIndex > _totalFiles)
            {
                endIndex = _totalFiles;
            }

            // Pagination related
            if (_totalFiles > _paginationThreshold)
            {
                // Enable pagination and adjust file list based on the current page
                allFiles = allFiles.Skip((_currentPage - 1) * _filesPerPage).Take(_filesPerPage).ToList();

                // Update or create pagination controls
                InitializePaginationButtons();
            }

            // Display message if the number of files == 0
            if (allFiles.Count == 0)
            {
                AddNoFilesMessage();
            }

            // Update the UI to reflect the current pagination status and the indices of files being displayed
            var displayingfiles0To = (string)Application.Current.TryFindResource("Displayingfiles0to") ?? "Displaying files 0 to";
            var outOf = (string)Application.Current.TryFindResource("outof") ?? "out of";
            var total = (string)Application.Current.TryFindResource("total") ?? "total";
            var displayingfiles = (string)Application.Current.TryFindResource("Displayingfiles") ?? "Displaying files";
            var to = (string)Application.Current.TryFindResource("to") ?? "to";
            
            TotalFilesLabel.Dispatcher.Invoke(() => 
                TotalFilesLabel.Content = allFiles.Count == 0 ? $"{displayingfiles0To} {endIndex} {outOf} {_totalFiles} {total}" : $"{displayingfiles} {startIndex} {to} {endIndex} {outOf} {_totalFiles} {total}"
            );

            // Reload the FavoritesConfig
            _favoritesManager = FavoritesManager.LoadFavorites();
                
            // Initialize GameButtonFactory with updated FavoritesConfig
            _gameButtonFactory = new GameButtonFactory(EmulatorComboBox, SystemComboBox, _systemConfigs, _machines, _settings, _favoritesManager, _gameFileGrid, this);
                
            // Initialize GameListFactory with updated FavoritesConfig
            var gameListFactory = new GameListFactory(EmulatorComboBox, SystemComboBox, _systemConfigs, _machines, _settings, _favoritesManager, this);

            // Display files based on ViewMode
            foreach (var filePath in allFiles)
            {
                if (_settings.ViewMode == "GridView")
                {
                    var gameButton = await _gameButtonFactory.CreateGameButtonAsync(filePath, selectedSystem, selectedConfig);
                    GameFileGrid.Dispatcher.Invoke(() => GameFileGrid.Children.Add(gameButton));
                }
                else // ListView
                {
                    var gameListViewItem = await gameListFactory.CreateGameListViewItemAsync(filePath, selectedSystem, selectedConfig);
                    await Dispatcher.InvokeAsync(() => GameListItems.Add(gameListViewItem));
                }
            }
                
            // Apply visibility settings to each button based on _settings.ShowGames
            ApplyShowGamesSetting();

            // Update the UI to reflect the current pagination status
            UpdatePaginationButtons();
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error in the method LoadGameFilesAsync.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);
                
            // Notify user
            MessageBoxLibrary.ErrorMethodLoadGameFilesAsyncMessageBox();
        }
    }

    private static async Task<bool> CheckIfSelectConfigIsNull(SystemConfig selectedConfig)
    {
        if (selectedConfig != null) return false;
        
        // Notify developer
        const string errorMessage = "Invalid system configuration.\n\n" +
                                    "Method: LoadGameFilesAsync";
        var ex = new Exception(errorMessage);
        await LogErrors.LogErrorAsync(ex, errorMessage);

        // Notify user
        MessageBoxLibrary.InvalidSystemConfigMessageBox();

        return true;

    }

    private bool CheckIfSystemComboBoxIsNotNull()
    {
        if (SystemComboBox.SelectedItem != null) return false;
        
        AddNoSystemMessage();
        return true;
    }

    #region Menu Items
    
    private void SetViewMode(string viewMode)
    {
        if (viewMode == "ListView")
        {
            ListView.IsChecked = true;
            GridView.IsChecked = false;
        }
        else
        {
            GridView.IsChecked = true;
            ListView.IsChecked = false;
        }
    }
    
    private void SetLanguageAndCheckMenu(string languageCode)
    {
        LanguageArabic.IsChecked = languageCode == "ar";
        LanguageBengali.IsChecked = languageCode == "bn";
        LanguageGerman.IsChecked = languageCode == "de";
        LanguageEnglish.IsChecked = languageCode == "en";
        LanguageSpanish.IsChecked = languageCode == "es";
        LanguageFrench.IsChecked = languageCode == "fr";
        LanguageHindi.IsChecked = languageCode == "hi";
        LanguageIndonesianMalay.IsChecked = languageCode == "id";
        LanguageItalian.IsChecked = languageCode == "it";
        LanguageJapanese.IsChecked = languageCode == "ja";
        LanguageKorean.IsChecked = languageCode == "ko";
        LanguageDutch.IsChecked = languageCode == "nl";
        LanguagePortugueseBr.IsChecked = languageCode == "pt-br";
        LanguageRussian.IsChecked = languageCode == "ru";
        LanguageTurkish.IsChecked = languageCode == "tr";
        LanguageUrdu.IsChecked = languageCode == "ur";
        LanguageVietnamese.IsChecked = languageCode == "vi";
        LanguageChineseSimplified.IsChecked = languageCode == "zh-hans";
        LanguageChineseTraditional.IsChecked = languageCode == "zh-hant";
    }
        
    private void EasyMode_Click(object sender, RoutedEventArgs e)
    {
        SaveApplicationSettings();
                
        EditSystemEasyMode editSystemEasyModeWindow = new(_settings);
        editSystemEasyModeWindow.ShowDialog();
    }

    private void ExpertMode_Click(object sender, RoutedEventArgs e)
    {
        SaveApplicationSettings();
                
        EditSystem editSystemWindow = new(_settings);
        editSystemWindow.ShowDialog();
    }
    
    private void DownloadImagePack_Click(object sender, RoutedEventArgs e)
    {
        DownloadImagePack downloadImagePack = new();
        downloadImagePack.ShowDialog();
    }
        
    private void EditLinks_Click(object sender, RoutedEventArgs e)
    {
        SaveApplicationSettings();
                
        EditLinks editLinksWindow = new(_settings);
        editLinksWindow.ShowDialog();
    }
    private void BugReport_Click(object sender, RoutedEventArgs e)
    {
        BugReport bugReportWindow = new();
        bugReportWindow.ShowDialog();
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "https://www.purelogiccode.com/Donate",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            // Notify developer
            var contextMessage = $"Unable to open the Donation Link from the menu.\n\n" +
                                 $"Exception type: {ex.GetType().Name}\n" +
                                 $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));
            
            // Notify user
            MessageBoxLibrary.ErrorOpeningDonationLinkMessageBox();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        About aboutWindow = new();
        aboutWindow.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
        
    private void ShowAllGames_Click(object sender, RoutedEventArgs e)
    {
        UpdateGameVisibility(visibilityCondition: _ => true); // Show all games
        UpdateShowGamesSetting("ShowAll");
        UpdateMenuCheckMarks("ShowAll");
    }
        
    private void ShowGamesWithCover_Click(object sender, RoutedEventArgs e)
    {
        UpdateGameVisibility(visibilityCondition: btn => btn.Tag?.ToString() != "DefaultImage"); // Show games with covers only
        UpdateShowGamesSetting("ShowWithCover");
        UpdateMenuCheckMarks("ShowWithCover");
    }

    private void ShowGamesWithoutCover_Click(object sender, RoutedEventArgs e)
    {
        UpdateGameVisibility(visibilityCondition: btn => btn.Tag?.ToString() == "DefaultImage"); // Show games without covers only
        UpdateShowGamesSetting("ShowWithoutCover");
        UpdateMenuCheckMarks("ShowWithoutCover");
    }

    private void UpdateGameVisibility(Func<Button, bool> visibilityCondition)
    {
        foreach (var child in _gameFileGrid.Children)
        {
            if (child is Button btn)
            {
                btn.Visibility = visibilityCondition(btn) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void UpdateShowGamesSetting(string showGames)
    {
        _settings.ShowGames = showGames;
        _settings.Save();
    }
        
    private void UpdateMenuCheckMarks(string selectedMenu)
    {
        ShowAll.IsChecked = selectedMenu == "ShowAll";
        ShowWithCover.IsChecked = selectedMenu == "ShowWithCover";
        ShowWithoutCover.IsChecked = selectedMenu == "ShowWithoutCover";
    }
        
    private void ToggleGamepad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        try
        {
            // Update the settings
            _settings.EnableGamePadNavigation = menuItem.IsChecked;

            _settings.Save();

            // Start or stop the GamePadController
            if (menuItem.IsChecked)
            {
                GamePadController.Instance2.Start();
            }
            else
            {
                GamePadController.Instance2.Stop();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"Failed to toggle gamepad.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                    
            // Notify user
            MessageBoxLibrary.ToggleGamepadFailureMessageBox();
        }
    }

    private async void ThumbnailSize_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem clickedItem) return;
            
            var sizeText = clickedItem.Name.Replace("Size", "");
            
            if (!int.TryParse(new string(sizeText.Where(char.IsDigit).ToArray()), out var newSize)) return;
            _gameButtonFactory.ImageHeight = newSize; // Update the image height
            _settings.ThumbnailSize = newSize;
            _settings.Save();
                    
            UpdateMenuCheckMarks(newSize);
                    
            // Reload List of Games
            await LoadGameFilesAsync();
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error in method ThumbnailSize_Click.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.MethodErrorMessageBox();
        }
    }
        
    private void GamesPerPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem clickedItem) return;
        var pageText = clickedItem.Name.Replace("Page", "");
        if (!int.TryParse(new string(pageText.Where(char.IsDigit).ToArray()), out var newPage)) return;
        _filesPerPage = newPage; 
        _paginationThreshold = newPage; 
        _settings.GamesPerPage = newPage; 
                    
        _settings.Save(); 
        UpdateMenuCheckMarks2(newPage);
                    
        SaveApplicationSettings();
        MainWindow_Restart();
    }
        
    private void GlobalSearch_Click(object sender, RoutedEventArgs e)
    {
        var globalSearchWindow = new GlobalSearch(_systemConfigs, _machines, _mameLookup, _settings, _favoritesManager, this);
        globalSearchWindow.Show();
    }
        
    private void GlobalStats_Click(object sender, RoutedEventArgs e)
    {
        var globalStatsWindow = new GlobalStats(_systemConfigs);
        globalStatsWindow.Show();
    }
        
    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        SaveApplicationSettings();
            
        var favoritesWindow = new FavoritesWindow(_settings, _systemConfigs, _machines, _favoritesManager, this);
        favoritesWindow.Show();
    }
        
    private void OrganizeSystemImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var findRomCoverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "FindRomCover", "FindRomCover.exe");

            if (File.Exists(findRomCoverPath))
            {
                string absoluteImageFolder = null;
                string absoluteRomFolder = null;

                // Check if _selectedImageFolder and _selectedRomFolder are set
                if (!string.IsNullOrEmpty(_selectedImageFolder))
                {
                    absoluteImageFolder = Path.GetFullPath(Path.IsPathRooted(_selectedImageFolder)
                        ? _selectedImageFolder
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _selectedImageFolder));
                }

                if (!string.IsNullOrEmpty(_selectedRomFolder))
                {
                    absoluteRomFolder = Path.GetFullPath(Path.IsPathRooted(_selectedRomFolder)
                        ? _selectedRomFolder
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _selectedRomFolder));
                }

                // Determine arguments based on available folders
                var arguments = string.Empty;
                if (absoluteImageFolder != null && absoluteRomFolder != null)
                {
                    arguments = $"\"{absoluteImageFolder}\" \"{absoluteRomFolder}\"";
                }

                // Start the process with or without arguments
                Process.Start(new ProcessStartInfo
                {
                    FileName = findRomCoverPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = findRomCoverPath
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "The file 'FindRomCover.exe' is missing.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.FindRomCoverMissingMessageBox();
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Notify developer
            var formattedException = $"The operation was canceled by the user while trying to launch 'FindRomCover.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.FindRomCoverLaunchWasCanceledByUserMessageBox();
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'FindRomCover.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.FindRomCoverLaunchWasBlockedMessageBox(_logPath);
        }
    }

    private void CreateBatchFilesForPS3Games_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchFilesForPs3GamesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "CreateBatchFilesForPS3Games", "CreateBatchFilesForPS3Games.exe");

            if (File.Exists(createBatchFilesForPs3GamesPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchFilesForPs3GamesPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'CreateBatchFilesForPS3Games.exe' was not found.";
                Exception ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'CreateBatchFilesForPS3Games.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
    
    private void BatchConvertIsoToXiso_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchConvertIsoToXisoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "BatchConvertIsoToXiso", "BatchConvertIsoToXiso.exe");

            if (File.Exists(createBatchConvertIsoToXisoPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchConvertIsoToXisoPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'BatchConvertIsoToXiso.exe' was not found.";
                Exception ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'BatchConvertIsoToXiso.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }

    private void BatchConvertToCHD_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchConvertToChdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "BatchConvertToCHD", "BatchConvertToCHD.exe");

            if (File.Exists(createBatchConvertToChdPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchConvertToChdPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'BatchConvertToCHD.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'BatchConvertToCHD.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
    
    private void BatchConvertTo7z_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var batchConvertTo7ZPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "BatchConvertTo7z", "BatchConvertTo7z.exe");

            if (File.Exists(batchConvertTo7ZPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchConvertTo7ZPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'BatchConvertTo7z.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'BatchConvertTo7z.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
    
    private void BatchConvertToZip_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var batchConvertToZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "BatchConvertToZip", "BatchConvertToZip.exe");

            if (File.Exists(batchConvertToZipPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchConvertToZipPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                var formattedException = "'BatchConvertToZip.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'BatchConvertToZip.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }

    private void CreateBatchFilesForScummVMGames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchFilesForScummVmGamesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "CreateBatchFilesForScummVMGames", "CreateBatchFilesForScummVMGames.exe");

            if (File.Exists(createBatchFilesForScummVmGamesPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchFilesForScummVmGamesPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                var formattedException = "'CreateBatchFilesForScummVMGames.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'CreateBatchFilesForScummVMGames.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
        
    private void CreateBatchFilesForSegaModel3Games_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchFilesForSegaModel3Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "CreateBatchFilesForSegaModel3Games", "CreateBatchFilesForSegaModel3Games.exe");

            if (File.Exists(createBatchFilesForSegaModel3Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchFilesForSegaModel3Path,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'CreateBatchFilesForSegaModel3Games.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'CreateBatchFilesForSegaModel3Games.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }

    private void CreateBatchFilesForWindowsGames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchFilesForWindowsGamesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "CreateBatchFilesForWindowsGames", "CreateBatchFilesForWindowsGames.exe");

            if (File.Exists(createBatchFilesForWindowsGamesPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchFilesForWindowsGamesPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'CreateBatchFilesForWindowsGames.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'CreateBatchFilesForWindowsGames.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
    
    private void CreateBatchFilesForXbox360XBLAGames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var createBatchFilesForXbox360XblaGamesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "CreateBatchFilesForXbox360XBLAGames", "CreateBatchFilesForXbox360XBLAGames.exe");

            if (File.Exists(createBatchFilesForXbox360XblaGamesPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = createBatchFilesForXbox360XblaGamesPath,
                    UseShellExecute = true
                });
            }
            else
            {
                // Notify developer
                const string formattedException = "'CreateBatchFilesForXbox360XBLAGames.exe' was not found.";
                var ex = new Exception(formattedException);
                LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.SelectedToolNotFoundMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var formattedException = $"An error occurred while launching 'CreateBatchFilesForXbox360XBLAGames.exe'.\n\n" +
                                     $"Exception type: {ex.GetType().Name}\n" +
                                     $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, formattedException).Wait(TimeSpan.FromSeconds(2));
                
            // Notify user
            MessageBoxLibrary.ErrorLaunchingToolMessageBox(_logPath);
        }
    }
        
    private void UpdateMenuCheckMarks(int selectedSize)
    {
        Size100.IsChecked = (selectedSize == 100);
        Size150.IsChecked = (selectedSize == 150);
        Size200.IsChecked = (selectedSize == 200);
        Size250.IsChecked = (selectedSize == 250);
        Size300.IsChecked = (selectedSize == 300);
        Size350.IsChecked = (selectedSize == 350);
        Size400.IsChecked = (selectedSize == 400);
        Size450.IsChecked = (selectedSize == 450);
        Size500.IsChecked = (selectedSize == 500);
        Size550.IsChecked = (selectedSize == 550);
        Size600.IsChecked = (selectedSize == 600);
    }
        
    private void UpdateMenuCheckMarks2(int selectedSize)
    {
        Page100.IsChecked = (selectedSize == 100);
        Page200.IsChecked = (selectedSize == 200);
        Page300.IsChecked = (selectedSize == 300);
        Page400.IsChecked = (selectedSize == 400);
        Page500.IsChecked = (selectedSize == 500);
        Page1000.IsChecked = (selectedSize == 1000);
    }
        
    private void UpdateMenuCheckMarks3(string selectedValue)
    {
        ShowAll.IsChecked = (selectedValue == "ShowAll");
        ShowWithCover.IsChecked = (selectedValue == "ShowWithCover");
        ShowWithoutCover.IsChecked = (selectedValue == "ShowWithoutCover");
    }
        
    private async void ChangeViewMode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Equals(sender, GridView))
            {
                GridView.IsChecked = true;
                ListView.IsChecked = false;
                _settings.ViewMode = "GridView";
                
                GameFileGrid.Visibility = Visibility.Visible;
                ListViewPreviewArea.Visibility = Visibility.Collapsed;

                // Ensure pagination is reset at the beginning
                ResetPaginationButtons();
                
                // Clear SearchTextBox
                SearchTextBox.Text = "";
                
                // Update current filter
                _currentFilter = null;
                
                // Empty SystemComboBox
                _selectedSystem = null;
                SystemComboBox.SelectedItem = null;
                var nosystemselected = (string)Application.Current.TryFindResource("Nosystemselected") ?? "No system selected";
                SelectedSystem = nosystemselected;
                PlayTime = "00:00:00";

                AddNoSystemMessage();
                
            }
            else if (Equals(sender, ListView))
            {
                GridView.IsChecked = false;
                ListView.IsChecked = true;
                _settings.ViewMode = "ListView";
                
                GameFileGrid.Visibility = Visibility.Collapsed;
                ListViewPreviewArea.Visibility = Visibility.Visible;

                // Ensure pagination is reset at the beginning
                ResetPaginationButtons();

                // Clear SearchTextBox
                SearchTextBox.Text = "";
                
                // Update current filter
                _currentFilter = null;
                
                // Empty SystemComboBox
                _selectedSystem = null;
                PreviewImage.Source = null;
                SystemComboBox.SelectedItem = null;
                
                // Set selected system
                var nosystemselected = (string)Application.Current.TryFindResource("Nosystemselected") ?? "No system selected";
                SelectedSystem = nosystemselected;
                PlayTime = "00:00:00";
                
                AddNoSystemMessage();
                
                await LoadGameFilesAsync();
            }
            _settings.Save(); // Save the updated ViewMode
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error while using the method ChangeViewMode_Click.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.ErrorChangingViewModeMessageBox();
        }
    }
        
    private void ApplyShowGamesSetting()
    {
        switch (_settings.ShowGames)
        {
            case "ShowAll":
                ShowAllGames_Click(ShowAll, null);
                break;
            case "ShowWithCover":
                ShowGamesWithCover_Click(ShowWithCover, null);
                break;
            case "ShowWithoutCover":
                ShowGamesWithoutCover_Click(ShowWithoutCover, null);
                break;
        }
    }
    
    private void ChangeLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var selectedLanguage = menuItem.Name switch
        {
            "LanguageArabic" => "ar",
            "LanguageBengali" => "bn",
            "LanguageGerman" => "de",
            "LanguageEnglish" => "en",
            "LanguageSpanish" => "es",
            "LanguageFrench" => "fr",
            "LanguageHindi" => "hi",
            "LanguageIndonesianMalay" => "id",
            "LanguageItalian" => "it",
            "LanguageJapanese" => "ja",                
            "LanguageKorean" => "ko",
            "LanguageDutch" => "nl",
            "LanguagePortugueseBr" => "pt-br",
            "LanguageRussian" => "ru",
            "LanguageTurkish" => "tr",
            "LanguageUrdu" => "ur",
            "LanguageVietnamese" => "vi",
            "LanguageChineseSimplified" => "zh-hans",
            "LanguageChineseTraditional" => "zh-hant",
            _ => "en"
        };

        _settings.Language = selectedLanguage;
        _settings.Save();

        // Update checked status
        SetLanguageAndCheckMenu(selectedLanguage);
            
        MainWindow_Restart();
    }

    #endregion
        
    #region Theme Options
        
    private void ChangeBaseTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var baseTheme = menuItem.Name;
        var currentAccent = ThemeManager.Current.DetectTheme(this)?.ColorScheme;
        App.ChangeTheme(baseTheme, currentAccent);

        UncheckBaseThemes();
        menuItem.IsChecked = true;
    }

    private void ChangeAccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var accentColor = menuItem.Name;
        var currentBaseTheme = ThemeManager.Current.DetectTheme(this)?.BaseColorScheme;
        App.ChangeTheme(currentBaseTheme, accentColor);
            
        UncheckAccentColors();
        menuItem.IsChecked = true;
    }

    private void UncheckBaseThemes()
    {
        Light.IsChecked = false;
        Dark.IsChecked = false;
    }

    private void UncheckAccentColors()
    {
        Red.IsChecked = false;
        Green.IsChecked = false;
        Blue.IsChecked = false;
        Purple.IsChecked = false;
        Orange.IsChecked = false;
        Lime.IsChecked = false;
        Emerald.IsChecked = false;
        Teal.IsChecked = false;
        Cyan.IsChecked = false;
        Cobalt.IsChecked = false;
        Indigo.IsChecked = false;
        Violet.IsChecked = false;
        Pink.IsChecked = false;
        Magenta.IsChecked = false;
        Crimson.IsChecked = false;
        Amber.IsChecked = false;
        Yellow.IsChecked = false;
        Brown.IsChecked = false;
        Olive.IsChecked = false;
        Steel.IsChecked = false;
        Mauve.IsChecked = false;
        Taupe.IsChecked = false;
        Sienna.IsChecked = false;
    }

    private void SetCheckedTheme(string baseTheme, string accentColor)
    {
        switch (baseTheme)
        {
            case "Light":
                Light.IsChecked = true;
                break;
            case "Dark":
                Dark.IsChecked = true;
                break;
        }

        switch (accentColor)
        {
            case "Red":
                Red.IsChecked = true;
                break;
            case "Green":
                Green.IsChecked = true;
                break;
            case "Blue":
                Blue.IsChecked = true;
                break;
            case "Purple":
                Purple.IsChecked = true;
                break;
            case "Orange":
                Orange.IsChecked = true;
                break;
            case "Lime":
                Lime.IsChecked = true;
                break;
            case "Emerald":
                Emerald.IsChecked = true;
                break;
            case "Teal":
                Teal.IsChecked = true;
                break;
            case "Cyan":
                Cyan.IsChecked = true;
                break;
            case "Cobalt":
                Cobalt.IsChecked = true;
                break;
            case "Indigo":
                Indigo.IsChecked = true;
                break;
            case "Violet":
                Violet.IsChecked = true;
                break;
            case "Pink":
                Pink.IsChecked = true;
                break;
            case "Magenta":
                Magenta.IsChecked = true;
                break;
            case "Crimson":
                Crimson.IsChecked = true;
                break;
            case "Amber":
                Amber.IsChecked = true;
                break;
            case "Yellow":
                Yellow.IsChecked = true;
                break;
            case "Brown":
                Brown.IsChecked = true;
                break;
            case "Olive":
                Olive.IsChecked = true;
                break;
            case "Steel":
                Steel.IsChecked = true;
                break;
            case "Mauve":
                Mauve.IsChecked = true;
                break;
            case "Taupe":
                Taupe.IsChecked = true;
                break;
            case "Sienna":
                Sienna.IsChecked = true;
                break;
        }
    }
    #endregion
    
    #region TrayIcon
        
    private void InitializeTrayIcon()
    {
        // Create a context menu for the tray icon
        _trayMenu = new ContextMenuStrip();
        
        var open = (string)Application.Current.TryFindResource("Open") ?? "Open";
        var exit = (string)Application.Current.TryFindResource("Exit") ?? "Exit";
        _trayMenu.Items.Add(open, null, OnOpen);
        _trayMenu.Items.Add(exit, null, OnExit);

        // Load the embedded icon from resources
        var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/SimpleLauncher;component/icon/icon.ico"))?.Stream;

        // Create the tray icon using the embedded icon
        if (iconStream == null) return;
        _trayIcon = new NotifyIcon
        {
            Icon = new Icon(iconStream),
            ContextMenuStrip = _trayMenu,
            Text = @"Simple Launcher",
            Visible = true
        };

        // Handle tray icon events
        _trayIcon.DoubleClick += OnOpen;
    }
        
    // Handle "Open" context menu item or tray icon double-click
    private void OnOpen(object sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // Handle "Exit" context menu item
    private void OnExit(object sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Current.Shutdown();
    }

    // Override the OnStateChanged method to hide the window when minimized
    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            // Retrieve the dynamic resource string
            var isminimizedtothetray = (string)Application.Current.TryFindResource("isminimizedtothetray") ?? "is minimized to the tray.";
            ShowTrayMessage($"Simple Launcher {isminimizedtothetray}");
        }
        base.OnStateChanged(e);
    }

    // Display a balloon message
    private void ShowTrayMessage(string message)
    {
        _trayIcon.BalloonTipTitle = @"Simple Launcher";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000); // Display for 3 seconds
    }

    // Clean up resources when closing the application
    protected override void OnClosing(CancelEventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosing(e);
    }
        
    #endregion
    
    #region Pagination

    private void ResetPaginationButtons()
    {
        _prevPageButton.IsEnabled = false;
        _nextPageButton.IsEnabled = false;
        _currentPage = 1;
        Scroller.ScrollToTop();
        TotalFilesLabel.Content = null;
    }
    private void InitializePaginationButtons()
    {
        _prevPageButton.IsEnabled = _currentPage > 1;
        _nextPageButton.IsEnabled = _currentPage * _filesPerPage < _totalFiles;
        Scroller.ScrollToTop();
    }
        
    private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentPage <= 1) return;
            _currentPage--;
            if (_currentSearchResults.Count != 0)
            {
                await LoadGameFilesAsync(searchQuery: SearchTextBox.Text);
            }
            else
            {
                await LoadGameFilesAsync(_currentFilter);
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Previous page button error.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.NavigationButtonErrorMessageBox();
        }
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var totalPages = (int)Math.Ceiling(_totalFiles / (double)_filesPerPage);

            if (_currentPage < totalPages)
            {
                _currentPage++;
                if (_currentSearchResults.Count != 0)
                {
                    await LoadGameFilesAsync(searchQuery: SearchTextBox.Text);
                }
                else
                {
                    await LoadGameFilesAsync(_currentFilter);
                }
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Next page button error.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.NavigationButtonErrorMessageBox();
        }
    }
        
    private void UpdatePaginationButtons()
    {
        _prevPageButton.IsEnabled = _currentPage > 1;
        _nextPageButton.IsEnabled = _currentPage * _filesPerPage < _totalFiles;
    }
        
    #endregion
    
    #region MainWindow Search
        
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ExecuteSearch();
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error in the method SearchButton_Click.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);

            // Notify user
            MessageBoxLibrary.MainWindowSearchEngineErrorMessageBox();
        }
    }

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Enter)
            {
                await ExecuteSearch();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Error in the method SearchTextBox_KeyDown.\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            await LogErrors.LogErrorAsync(ex, errorMessage);
 
            // Notify user
            MessageBoxLibrary.MainWindowSearchEngineErrorMessageBox();
        }
    }

    private async Task ExecuteSearch()
    {
        ResetPaginationButtons();
            
        _currentSearchResults.Clear();
    
        // Call DeselectLetter to clear any selected letter
        _letterNumberMenu.DeselectLetter();

        var searchQuery = SearchTextBox.Text.Trim();

        if (SystemComboBox.SelectedItem == null)
        {
            // Notify user
            MessageBoxLibrary.SelectSystemBeforeSearchMessageBox();

            return;
        }
        
        if (string.IsNullOrEmpty(searchQuery))
        {
            // Notify user
            MessageBoxLibrary.EnterSearchQueryMessageBox();

            return;
        }

        var pleaseWaitWindow = new PleaseWaitSearch();
        await ShowPleaseWaitWindowAsync(pleaseWaitWindow);

        try
        {
            await LoadGameFilesAsync(null, searchQuery);
        }
        finally
        {
            await ClosePleaseWaitWindowAsync(pleaseWaitWindow);
        }
    }
        
    #endregion
}