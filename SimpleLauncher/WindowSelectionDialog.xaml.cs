using System;
using System.Collections.Generic;

namespace SimpleLauncher;

public partial class WindowSelectionDialog
{
    public IntPtr SelectedWindowHandle { get; private set; } = IntPtr.Zero;

    public WindowSelectionDialog(List<(IntPtr Handle, string Title)> windows)
    {
        InitializeComponent();

        // Populate the ListBox with the window data
        foreach (var window in windows)
        {
            if (!string.IsNullOrWhiteSpace(window.Title))
            {
                WindowsListBox.Items.Add(new WindowItem { Title = window.Title, Handle = window.Handle });
            }
        }

        // Set default DialogResult to false
        Closed += (_, _) => { DialogResult ??= false; };
    }

    private void WindowsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (WindowsListBox.SelectedItem is WindowItem selectedItem)
        {
            SelectedWindowHandle = selectedItem.Handle;
            DialogResult = true; // Only works after ShowDialog() is called
            Close();
        }
    }
    
    public class WindowItem
    {
        public string Title { get; set; }
        public IntPtr Handle { get; init; }
    }
}