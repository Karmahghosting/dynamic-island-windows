using System.Runtime.InteropServices;

namespace DynamicIsland.Platform.Windows;

internal sealed class WindowsSystemStatsService : ISystemStatsService
{
    private ulong _prevIdle, _prevKernel, _prevUser;
    private double _lastCpu;

    public SystemStats Get()
    {
        double cpu = _lastCpu;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong i = ToU(idle), k = ToU(kernel), u = ToU(user);
            if (_prevKernel != 0)
            {
                ulong sys = (k - _prevKernel) + (u - _prevUser);
                ulong idl = i - _prevIdle;
                if (sys > 0) cpu = (sys - idl) * 100.0 / sys;
            }
            _prevIdle = i; _prevKernel = k; _prevUser = u;
            _lastCpu = Math.Clamp(cpu, 0, 100);
        }

        int ramPercent = 0;
        string ramText = "";
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            ramPercent = (int)mem.dwMemoryLoad;
            double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;
            double totalGb = mem.ullTotalPhys / 1073741824.0;
            ramText = $"{usedGb:0.0} / {totalGb:0.0} Go";
        }

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new SystemStats(_lastCpu, ramPercent, ramText, uptime);
    }

    private static ulong ToU(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
