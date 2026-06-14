using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using SD = System.Drawing;

namespace DynamicIsland.Platform.Windows;

internal static class WindowsIcon
{
    public static Bitmap? FromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            using var ico = SD.Icon.FromHandle(hIcon);
            using var bmp = ico.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, SD.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
    }

    public static Bitmap? ForFile(string path)
    {
        var fi = new SHFILEINFO();
        var r = SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (r == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;
        try { return FromHIcon(fi.hIcon); }
        finally { DestroyIcon(fi.hIcon); }
    }

    public static Bitmap? ForApp(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        try
        {
            string leaf = aumid;
            int bang = leaf.IndexOf('!');
            if (bang >= 0) leaf = leaf[(bang + 1)..];
            leaf = Path.GetFileName(leaf.Replace('/', '\\'));
            string name = leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? leaf[..^4] : leaf;
            if (name.Length == 0) return null;

            foreach (var p in Process.GetProcessesByName(name))
            {
                string? exe = null;
                try { exe = p.MainModule?.FileName; } catch { }
                if (exe is null) continue;
                var bmp = ForFile(exe);
                if (bmp is not null) return bmp;
            }
        }
        catch { }
        return null;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
}

internal sealed class WindowsFileIconService : IFileIconService
{
    public Bitmap? ForFile(string path) => WindowsIcon.ForFile(path);
}
