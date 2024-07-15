﻿using System.Windows;

namespace SimpleLauncher
{
    public partial class EditSystemEasyMode
    {
        public EditSystemEasyMode()
        {
            InitializeComponent();
            
            // Apply the theme to this window
            App.ApplyThemeToWindow(this);
        }

        private void AddSystemButton_Click(object sender, RoutedEventArgs routedEventArgs)
        {
            EditSystemEasyModeAddSystem editSystemEasyModeAdd = new();
            Close();
            editSystemEasyModeAdd.ShowDialog();
        }

        private void EditSystemButton_Click(object sender, RoutedEventArgs routedEventArgs)
        {
            EditSystem editSystem = new();
            Close();
            editSystem.ShowDialog();
        }

        private void DeleteSystemButton_Click(object sender, RoutedEventArgs routedEventArgs)
        {
            EditSystem editSystem = new();
            Close();
            editSystem.ShowDialog();
        }
    }
}