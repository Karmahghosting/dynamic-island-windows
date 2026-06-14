using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

internal static class AppIcon
{
    public static BitmapSource? Resolve(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        return FromAppsFolder(aumid) ?? FromProcess(aumid);
    }

    public static BitmapSource? ForFile(string path) => IconFromFile(path);

    private static BitmapSource? FromAppsFolder(string aumid)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName($"shell:AppsFolder\\{aumid}", IntPtr.Zero, ref iid, out var factory);
            if (factory is null) return null;

            factory.GetImage(new SIZE { cx = 64, cy = 64 }, SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out IntPtr hbm);
            Marshal.ReleaseComObject(factory);
            if (hbm == IntPtr.Zero) return null;

            try { return FromHBitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
    }

    private static BitmapSource? FromProcess(string aumid)
    {
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
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                if (path is null) continue;

                var icon = IconFromFile(path);
                if (icon is not null) return icon;
            }
        }
        catch { }
        return null;
    }

    private static BitmapSource? IconFromFile(string path)
    {
        var fi = new SHFILEINFO();
        var r = SHGetFileInfo(path, 0, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (r == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(fi.hIcon); }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        var bmp = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmp) == 0) return null;

        if (bmp.bmBits != IntPtr.Zero && bmp.bmBitsPixel == 32)
        {
            int stride = bmp.bmWidth * 4;
            var src = BitmapSource.Create(bmp.bmWidth, bmp.bmHeight, 96, 96,
                PixelFormats.Bgra32, null, bmp.bmBits, stride * bmp.bmHeight, stride);
            src.Freeze();
            return src;
        }

        var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero,
            Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        fallback.Freeze();
        return fallback;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0,
        BiggerSizeOk = 0x1,
        MemoryOnly = 0x2,
        IconOnly = 0x4,
        ThumbnailOnly = 0x8,
        InCacheOnly = 0x10
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
