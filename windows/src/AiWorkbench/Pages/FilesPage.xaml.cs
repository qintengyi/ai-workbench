using System;
using System.IO;
using AiWorkbench.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiWorkbench.Pages;

/// <summary>文件工作区：浏览 E:\code 树、读文件。可拖入对话。</summary>
public sealed partial class FilesPage : Page
{
    private string _currentDir = FileWorkspace.DefaultRoot;

    public FilesPage()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        PathLabel.Text = _currentDir;
        FileList.Items.Clear();
        foreach (var e in MainWindow.FileWorkspace.List(_currentDir))
        {
            FileList.Items.Add(e);
        }
    }

    private void File_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry entry) return;
        if (entry.IsDirectory)
        {
            _currentDir = entry.FullPath;
            Refresh();
            return;
        }
        FileTitle.Text = entry.Name;
        FileContent.Text = MainWindow.FileWorkspace.ReadText(entry.FullPath);
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDir)?.FullName;
        if (!string.IsNullOrEmpty(parent)) { _currentDir = parent; Refresh(); }
    }

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        _currentDir = FileWorkspace.DefaultRoot;
        Refresh();
    }
}
