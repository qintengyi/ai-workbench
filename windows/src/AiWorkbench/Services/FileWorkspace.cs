using System;
using System.Collections.Generic;
using System.IO;

namespace AiWorkbench.Services;

public sealed class FileEntry
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTimeOffset LastWrite { get; set; }
    public string DirGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
}

public sealed class FileWorkspace
{
    public const string DefaultRoot = @"E:\code";
    public string Root { get; set; } = DefaultRoot;

    public IReadOnlyList<FileEntry> List(string? dir = null)
    {
        var path = string.IsNullOrEmpty(dir) ? Root : dir;
        if (!Directory.Exists(path)) return Array.Empty<FileEntry>();
        var entries = new List<FileEntry>();
        var di = new DirectoryInfo(path);
        foreach (var d in di.EnumerateDirectories())
        {
            try { entries.Add(new FileEntry { Name = d.Name, FullPath = d.FullName, IsDirectory = true, LastWrite = d.LastWriteTimeUtc }); }
            catch { }
        }
        foreach (var f in di.EnumerateFiles())
        {
            try { entries.Add(new FileEntry { Name = f.Name, FullPath = f.FullName, IsDirectory = false, Size = f.Length, LastWrite = f.LastWriteTimeUtc }); }
            catch { }
        }
        return entries;
    }

    public string ReadText(string fullPath, int maxChars = 200_000)
    {
        if (!File.Exists(fullPath)) return string.Empty;
        var info = new FileInfo(fullPath);
        if (info.Length > 5 * 1024 * 1024) return "[文件过大，超过 5MB，不支持预览]";
        var text = File.ReadAllText(fullPath);
        return text.Length > maxChars ? text.Substring(0, maxChars) + "\n…[已截断]" : text;
    }
}
