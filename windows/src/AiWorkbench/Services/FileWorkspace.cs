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

    // 二进制/不可读扩展名（不会作为文本预览）
    private static readonly HashSet<string> BinaryExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",".jpg",".jpeg",".bmp",".gif",".webp",".ico",".tif",".tiff",
        ".mp4",".mp3",".wav",".avi",".mov",".mkv",".flv",".webm",
        ".exe",".dll",".so",".dylib",".a",".lib",".obj",".pdb",
        ".zip",".rar",".7z",".gz",".tar",".bz2",".xz",
        ".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",
        ".class",".jar",".war",".pyc",".wasm",".o",
    };

    public IReadOnlyList<FileEntry> List(string? dir = null)
    {
        var path = string.IsNullOrEmpty(dir) ? Root : dir;
        if (!IsWithinRoot(path)) return Array.Empty<FileEntry>();
        if (!Directory.Exists(path)) return Array.Empty<FileEntry>();
        var entries = new List<FileEntry>();
        var di = new DirectoryInfo(path);
        // 异常隔离：单目录访问失败不影响其他
        DirectoryInfo[] dirs;
        FileInfo[] files;
        try { dirs = di.GetDirectories(); }
        catch (UnauthorizedAccessException) { return entries; }
        catch { return entries; }
        try { files = di.GetFiles(); }
        catch (UnauthorizedAccessException) { return entries; }
        catch { files = Array.Empty<FileInfo>(); }

        foreach (var d in dirs)
        {
            try { entries.Add(new FileEntry { Name = d.Name, FullPath = d.FullName, IsDirectory = true, LastWrite = d.LastWriteTimeUtc }); }
            catch { }
        }
        foreach (var f in files)
        {
            try { entries.Add(new FileEntry { Name = f.Name, FullPath = f.FullName, IsDirectory = false, Size = f.Length, LastWrite = f.LastWriteTimeUtc }); }
            catch { }
        }
        return entries;
    }

    public string ReadText(string fullPath, int maxChars = 200_000)
    {
        if (!IsWithinRoot(fullPath)) return "[路径越界，拒绝读取]";
        if (!File.Exists(fullPath)) return string.Empty;
        var info = new FileInfo(fullPath);
        if (info.Length > 5 * 1024 * 1024) return "[文件过大，超过 5MB，不支持预览]";
        var ext = Path.GetExtension(fullPath);
        if (BinaryExts.Contains(ext)) return $"[二进制文件 {ext}，不支持文本预览]";
        var text = File.ReadAllText(fullPath);
        return text.Length > maxChars ? text.Substring(0, maxChars) + "\n…[已截断]" : text;
    }

    /// <summary>路径必须在 Root 子树内（防 ../ 逃逸）。</summary>
    private bool IsWithinRoot(string fullPath)
    {
        try
        {
            var root = Path.GetFullPath(Root).TrimEnd('\\', '/').ToLowerInvariant();
            var target = Path.GetFullPath(fullPath).TrimEnd('\\', '/').ToLowerInvariant();
            return target == root || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
