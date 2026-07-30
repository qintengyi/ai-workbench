using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AiWorkbench.Services;

/// <summary>
/// 文件工作区：浏览 E:\code 树、读文件。
/// 对应 ARCHITECTURE.md Windows 端职责 2。
/// </summary>
public sealed class FileWorkspace
{
    public const string DefaultRoot = @"E:\code";

    public string Root { get; set; } = DefaultRoot;

    public sealed class Entry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTimeOffset LastWrite { get; set; }
        /// <summary>Fluent 字体图标 glyph（文件夹/文件）。</summary>
        public string DirGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
    }

    public IReadOnlyList<Entry> List(string? dir = null)
    {
        var path = string.IsNullOrEmpty(dir) ? Root : dir;
        if (!Directory.Exists(path)) return Array.Empty<Entry>();

        var entries = new List<Entry>();
        var di = new DirectoryInfo(path);
        foreach (var d in di.EnumerateDirectories())
        {
            try
            {
                entries.Add(new Entry
                {
                    Name = d.Name,
                    FullPath = d.FullName,
                    IsDirectory = true,
                    LastWrite = d.LastWriteTimeUtc,
                });
            }
            catch { /* skip */ }
        }
        foreach (var f in di.EnumerateFiles())
        {
            try
            {
                entries.Add(new Entry
                {
                    Name = f.Name,
                    FullPath = f.FullName,
                    IsDirectory = false,
                    Size = f.Length,
                    LastWrite = f.LastWriteTimeUtc,
                });
            }
            catch { /* skip */ }
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
