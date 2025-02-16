﻿using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using ControlzEx.Theming;

namespace SimpleLauncher;

public partial class App
{
    private static SettingsConfig _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
     
        // Load settings
        _settings = new SettingsConfig();
        
        // Apply theme
        ApplyTheme(_settings.BaseTheme, _settings.AccentColor);
        ApplyLanguage(_settings.Language);
    }

    private void ApplyLanguage(string cultureCode = null)
    {
        try
        {
            // Determine the culture code (default to CurrentUICulture if not provided)
            var culture = string.IsNullOrEmpty(cultureCode)
                ? CultureInfo.CurrentUICulture
                : new CultureInfo(cultureCode);

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // Load the resource dictionary
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"/resources/strings.{culture.Name}.xaml", UriKind.Relative)
            };

            // Replace the current localization dictionary
            var existingDictionary = Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("strings.") ?? false);

            if (existingDictionary != null)
            {
                Resources.MergedDictionaries.Remove(existingDictionary);
            }

            Resources.MergedDictionaries.Add(dictionary);

            // Apply the culture to the application
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Failed to load language resources for {cultureCode}\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, errorMessage).Wait(TimeSpan.FromSeconds(2));
            
            // Notify user
            MessageBoxLibrary.FailedToLoadLanguageResourceMessageBox();

            // Fallback to English
            var fallbackDictionary = new ResourceDictionary
            {
                Source = new Uri("/resources/strings.en.xaml", UriKind.Relative)
            };
            Resources.MergedDictionaries.Add(fallbackDictionary);
        }
    }

    private static void ApplyTheme(string baseTheme, string accentColor)
    {
        try
        {
            ThemeManager.Current.ChangeTheme(Current, $"{baseTheme}.{accentColor}");
        }
        catch (Exception ex)
        {
            // Notify developer
            var errorMessage = $"Failed to Apply Theme\n\n" +
                               $"Exception type: {ex.GetType().Name}\n" +
                               $"Exception details: {ex.Message}";
            LogErrors.LogErrorAsync(ex, errorMessage).Wait(TimeSpan.FromSeconds(2));
        }
    }

    public static void ApplyThemeToWindow(Window window)
    {
        var baseTheme = _settings.BaseTheme;
        var accentColor = _settings.AccentColor;
        ThemeManager.Current.ChangeTheme(window, $"{baseTheme}.{accentColor}");
    }
    
    public static void ChangeTheme(string baseTheme, string accentColor)
    {
        ApplyTheme(baseTheme, accentColor);
        _settings.BaseTheme = baseTheme;
        _settings.AccentColor = accentColor;
        _settings.Save();
    }
}