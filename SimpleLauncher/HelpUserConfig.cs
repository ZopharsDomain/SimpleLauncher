using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SimpleLauncher;

public class HelpUserConfig
{
    private const string FilePath = "helpuser.xml";

    public List<SystemHelper> Systems { get; private set; } = [];

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                // Notify developer
                const string contextMessage = "The file 'helpuser.xml' is missing.";
                var ex = new Exception(contextMessage);
                LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));

                // Notify user
                if (MessageBoxLibrary.FileHelpUserXmlIsMissingMessageBox()) return;

                return;
            }

            XDocument doc;

            try
            {
                doc = XDocument.Load(FilePath);
            }
            catch (Exception ex)
            {
                // Notify developer
                const string contextMessage = "Unable to load 'helpuser.xml'. The file may be corrupted.";
                LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                if (MessageBoxLibrary.FailedToLoadHelpUserXmlMessageBox()) return;

                return;
            }

            Systems = doc.Descendants("System")
                .Select(system =>
                {
                    try
                    {
                        return new SystemHelper
                        {
                            SystemName = (string)system.Element("SystemName"),
                            SystemHelperText = NormalizeText((string)system.Element("SystemHelper"))
                        };
                    }
                    catch (Exception ex)
                    {
                        // Notify developer
                        const string contextMessage = "Failed to parse the file 'helpuser.xml'.";
                        LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));

                        // Notify user
                        if (MessageBoxLibrary.CouldNotLoadHelpUserXmlMessageBox()) return null;

                        return null; // Ignore invalid system entries
                    }
                })
                .Where(helper => helper != null) // Filter out invalid entries
                .ToList();

            if (Systems.Count != 0) return;
            {
                // Notify developer
                const string contextMessage = "No valid systems found in the file 'helpuser.xml'.";
                var ex = new Exception(contextMessage);
                LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));
                
                // Notify user
                MessageBoxLibrary.NoSystemInHelpUserXmlMessageBox();
            }
        }
        catch (Exception ex)
        {
            // Notify developer
            const string contextMessage = "Unexpected error while loading 'helpuser.xml'.";
            LogErrors.LogErrorAsync(ex, contextMessage).Wait(TimeSpan.FromSeconds(2));
            
            // Notify user
            MessageBoxLibrary.ErrorWhileLoadingHelpUserXmlMessageBox();
        }
    }
    
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Process each line to remove leading spaces while keeping line breaks
        var lines = text.Split(['\r', '\n'], StringSplitOptions.None); // Preserve empty lines
        return string.Join(Environment.NewLine, lines.Select(line => line.TrimStart()));
    }
    
    public class SystemHelper
    {
        public string SystemName { get; init; }
        public string SystemHelperText { get; init; }
    }
}