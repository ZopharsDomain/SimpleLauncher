﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;

namespace SimpleLauncher
{
    public class MameConfig
    {
        public string MachineName { get; set; }
        public string Description { get; set; }

        public static List<MameConfig> LoadFromXml(string xmlPath)
        {
            try
            {
                if (File.Exists(xmlPath))
                {
                    XDocument xmlDoc = XDocument.Load(xmlPath);
                    return xmlDoc.Descendants("Machine")
                                 .Select(m => new MameConfig
                                 {
                                     MachineName = m.Element("MachineName")?.Value,
                                     Description = m.Element("Description")?.Value
                                 }).ToList();
                }
                else
                {
                    MessageBox.Show("mame.xml not found.", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    string errorMessage = "mame.xml not found. Unable to load MAME configurations.";
                    Task logTask = LogErrors.LogErrorAsync(new FileNotFoundException(errorMessage), "MameConfig LoadFromXml: mame.xml not found");

                    return [];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                Task logTask = LogErrors.LogErrorAsync(ex, "Error loading MAME configurations from XML.");

                return [];
            }
        }
    }
}
