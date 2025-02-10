using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace SimpleLauncher;

[XmlRoot("EasyMode")]
public class EasyModeConfig
{
    [XmlElement("EasyModeSystemConfig")]
    public List<EasyModeSystemConfig> Systems { get; set; }

    public static EasyModeConfig Load()
    {
        string xmlFile = "easymode.xml";
        string xmlFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, xmlFile);
        
        // Check if the file exists before proceeding.
        if (!File.Exists(xmlFilePath))
        {
            // Notify developer
            LogAndNotify(new FileNotFoundException($"File not found: {xmlFilePath}"),
                "The file 'easymode.xml' was not found.");
            return new EasyModeConfig { Systems = new List<EasyModeSystemConfig>() };
        }

        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(EasyModeConfig));
            
            // Open the file with explicit access and sharing settings.
            using FileStream fileStream = new FileStream(xmlFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var config = (EasyModeConfig)serializer.Deserialize(fileStream);
            
            // Validate configuration if not null.
            if (config != null)
            {
                config.Validate(); // Exclude invalid systems
                return config;
            }
        }
        catch (InvalidOperationException ex)
        {
            // Notify developer
            string errorMessage = "The file 'easymode.xml' is corrupted or invalid.\n\n" +
                                  $"Exception type: {ex.GetType().Name}\n" +
                                  $"Error Details: {ex.Message}";
            LogAndNotify(ex, errorMessage);
        }
        catch (Exception ex)
        {
            // Notify developer
            string errorMessage = "An unexpected error occurred while loading the file 'easymode.xml'.\n\n" +
                                  $"Exception type: {ex.GetType().Name}\n" +
                                  $"Error Details: {ex.Message}";
            LogAndNotify(ex, errorMessage);
        }

        // Return an empty config to avoid further null reference issues
        return new EasyModeConfig { Systems = new List<EasyModeSystemConfig>() };
    }

    public void Validate()
    {
        Systems = Systems?.Where(system => system.IsValid()).ToList() ?? new List<EasyModeSystemConfig>();
    }
    
    private static void LogAndNotify(Exception ex, string errorMessage)
    {
        // Notify developer
        Task logTask = LogErrors.LogErrorAsync(ex, errorMessage);
        logTask.Wait(TimeSpan.FromSeconds(2));
            
        // Notify the user.
        MessageBoxLibrary.ErrorLoadingEasyModeXmlMessageBox();
    }
}

public class EasyModeSystemConfig(List<string> fileFormatsToSearch, string systemImageFolder, string systemName, string systemFolder, List<string> fileFormatsToLaunch)
{
    public string SystemName { get; } = systemName;
    public string SystemFolder { get; set; } = systemFolder;
    public string SystemImageFolder { get; } = systemImageFolder;

    [XmlElement("SystemIsMame")]
    public bool? SystemIsMameNullable { get; set; }

    [XmlIgnore]
    public bool SystemIsMame => SystemIsMameNullable ?? false;

    [XmlArray("FileFormatsToSearch")]
    [XmlArrayItem("FormatToSearch")]
    public List<string> FileFormatsToSearch { get; } = fileFormatsToSearch;

    [XmlElement("ExtractFileBeforeLaunch")]
    public bool? ExtractFileBeforeLaunchNullable { get; set; }

    [XmlIgnore]
    public bool ExtractFileBeforeLaunch => ExtractFileBeforeLaunchNullable ?? false;

    [XmlArray("FileFormatsToLaunch")]
    [XmlArrayItem("FormatToLaunch")]
    public List<string> FileFormatsToLaunch { get; } = fileFormatsToLaunch;

    [XmlElement("Emulators")]
    public EmulatorsConfig Emulators { get; set; }

    public bool IsValid()
    {
        // Validate only SystemName; Emulators and nested Emulator can be null.
        return !string.IsNullOrWhiteSpace(SystemName);
    }
}


public class EmulatorsConfig
{
    [XmlElement("Emulator")]
    public EmulatorConfig Emulator { get; set; }
}

public class EmulatorConfig(
    string emulatorName,
    string emulatorLocation,
    string emulatorParameters,
    string emulatorDownloadLink,
    string emulatorDownloadExtractPath,
    string coreDownloadLink,
    string extrasDownloadExtractPath,
    string extrasDownloadLink,
    string coreDownloadExtractPath)
{
    public string EmulatorName { get; } = emulatorName;
    public string EmulatorLocation { get; } = emulatorLocation;
    public string EmulatorParameters { get; } = emulatorParameters;
    public string EmulatorDownloadPage { get; set; }
    public string EmulatorLatestVersion { get; set; }
    public string EmulatorDownloadLink { get; } = emulatorDownloadLink;

    [XmlElement("EmulatorDownloadRename")]
    public bool? EmulatorDownloadRenameNullable { get; set; }

    [XmlIgnore]
    public bool EmulatorDownloadRename => EmulatorDownloadRenameNullable ?? false;

    public string EmulatorDownloadExtractPath { get; } = emulatorDownloadExtractPath;
    public string CoreLocation { get; set; }
    public string CoreLatestVersion { get; set; }
    public string CoreDownloadLink { get; } = coreDownloadLink;
    public string CoreDownloadExtractPath { get; } = coreDownloadExtractPath;
    public string ExtrasLocation { get; set; }
    public string ExtrasLatestVersion { get; set; }
    public string ExtrasDownloadLink { get; } = extrasDownloadLink;
    public string ExtrasDownloadExtractPath { get; } = extrasDownloadExtractPath;
}