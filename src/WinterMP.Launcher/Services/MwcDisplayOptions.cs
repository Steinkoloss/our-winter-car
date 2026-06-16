using System.Runtime.InteropServices;

namespace WinterMP.Launcher.Services
{
    public sealed class ResolutionOption
    {
        public int Width { get; }
        public int Height { get; }
        public string Label => $"{Width} x {Height}";

        public ResolutionOption(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public override string ToString() => Label;

        public override bool Equals(object? obj) =>
            obj is ResolutionOption other && other.Width == Width && other.Height == Height;

        public override int GetHashCode() => Width ^ (Height << 16);
    }

    public static class MwcDisplayOptions
    {
        public static readonly string[] QualityNames =
        {
            "Shitty (no VSync)",
            "Better",
            "Good",
            "Good (no VSync)",
            "Golden Eye",
            "Golden Eye (no VSync)",
        };

        private static readonly (int W, int H)[] FallbackResolutions =
        {
            (3840, 2160), (2560, 1440), (1920, 1080), (1680, 1050), (1600, 900),
            (1366, 768), (1280, 720), (1024, 768), (960, 540), (800, 600), (640, 480),
        };

        public static IReadOnlyList<string> GetMonitorLabels()
        {
            var devices = EnumerateMonitorDevices();
            if (devices.Count == 0)
                return new[] { "Display 1" };

            var labels = new string[devices.Count];
            for (int i = 0; i < devices.Count; i++)
                labels[i] = $"Display {i + 1}";
            return labels;
        }

        public static IReadOnlyList<ResolutionOption> GetResolutionsForMonitor(int monitorIndex)
        {
            IReadOnlyList<string> devices = EnumerateMonitorDevices();
            if (devices.Count == 0)
                return FallbackResolutions.Select(r => new ResolutionOption(r.W, r.H)).ToList();

            if (monitorIndex < 0 || monitorIndex >= devices.Count)
                monitorIndex = 0;

            string deviceName = devices[monitorIndex];
            var seen = new HashSet<(int, int)>();
            var list = new List<ResolutionOption>();

            var devMode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
            for (int mode = 0; EnumDisplaySettings(deviceName, mode, ref devMode); mode++)
            {
                if (devMode.dmPelsWidth <= 0 || devMode.dmPelsHeight <= 0) continue;
                if (!seen.Add((devMode.dmPelsWidth, devMode.dmPelsHeight))) continue;
                list.Add(new ResolutionOption(devMode.dmPelsWidth, devMode.dmPelsHeight));
            }

            if (list.Count == 0)
            {
                return FallbackResolutions
                    .Select(r => new ResolutionOption(r.W, r.H))
                    .ToList();
            }

            list.Sort((a, b) =>
            {
                int cmp = b.Width.CompareTo(a.Width);
                return cmp != 0 ? cmp : b.Height.CompareTo(a.Height);
            });
            return list;
        }

        public static ResolutionOption? FindResolution(
            IReadOnlyList<ResolutionOption> options,
            int width,
            int height)
        {
            foreach (ResolutionOption option in options)
            {
                if (option.Width == width && option.Height == height)
                    return option;
            }

            return null;
        }

        private static List<string> EnumerateMonitorDevices()
        {
            // user32.dll only exists on Windows; on Linux fall through to the fallback
            // resolution list (callers treat an empty device list as "unknown display").
            if (!OperatingSystem.IsWindows()) return new List<string>();

            var devices = new List<string>();
            var enumerator = new MonitorEnumerator(devices);
            var handle = GCHandle.Alloc(enumerator);
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, enumerator.Callback, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }

            if (devices.Count == 0)
            {
                var devMode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                if (EnumDisplaySettings(null, unchecked((int)EnumDisplaySettingsMode.CurrentSettings), ref devMode))
                    devices.Add(devMode.dmDeviceName);
            }

            return devices;
        }

        private sealed class MonitorEnumerator
        {
            private readonly List<string> _devices;

            public MonitorEnumerator(List<string> devices) => _devices = devices;

            public bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
            {
                var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(hMonitor, ref info)) return true;

                if (!_devices.Contains(info.szDevice))
                    _devices.Add(info.szDevice);
                return true;
            }
        }

        private enum EnumDisplaySettingsMode
        {
            CurrentSettings = unchecked((int)0xFFFFFFFF),
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumDelegate lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DevMode
        {
            private const int CchDeviceName = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTCOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
