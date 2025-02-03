﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SimpleLauncher;

public class MameConfig
{
    public string MachineName { get; private init; }
    public string Description { get; private init; }

    private static readonly string DefaultXmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mame.xml");

    public static List<MameConfig> LoadFromXml(string xmlPath = null)
    {
        xmlPath ??= DefaultXmlPath;

        // Check if the mame.xml file exists
        if (!File.Exists(xmlPath))
        {
            // Notify developer
            string contextMessage = "The file 'mame.xml' could not be found in the application folder.";
            Exception ex = new Exception(contextMessage);
            Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
            logTask.Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.ReinstallSimpleLauncherFileMissingMessageBox();

            return new List<MameConfig>();
        }

        try
        {
            XDocument xmlDoc = XDocument.Load(xmlPath);
            return xmlDoc.Descendants("Machine")
                .Select(m => new MameConfig
                {
                    MachineName = m.Element("MachineName")?.Value,
                    Description = m.Element("Description")?.Value
                }).ToList();
        }
        catch (Exception ex)
        {
            // Notify developer
            string contextMessage = $"The file mame.xml could not be loaded or is corrupted.\n\n" +
                                    $"Exception type: {ex.GetType().Name}\n" +
                                    $"Exception details: {ex.Message}";
            Task logTask = LogErrors.LogErrorAsync(ex, contextMessage);
            logTask.Wait(TimeSpan.FromSeconds(2));

            // Notify user
            MessageBoxLibrary.ReinstallSimpleLauncherFileCorruptedMessageBox();

            return new List<MameConfig>();
        }
    }
}