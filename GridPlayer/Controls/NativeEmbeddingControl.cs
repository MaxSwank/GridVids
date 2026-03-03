using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace GridPlayer.Controls
{
    public class NativeEmbeddingControl : NativeControlHost
    {
        public static readonly DirectProperty<NativeEmbeddingControl, IntPtr> HWndProperty =
            AvaloniaProperty.RegisterDirect<NativeEmbeddingControl, IntPtr>(
                nameof(HWnd),
                o => o.HWnd,
                (o, v) => o.HWnd = v);

        private IntPtr _hWnd;
        public IntPtr HWnd
        {
            get => _hWnd;
            private set => SetAndRaise(HWndProperty, ref _hWnd, value);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = Win32.CreateChildWindow(parent.Handle);
                HWnd = handle.Handle;
                return handle;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux/GTK/X11 implementation (Stub for now or simple X11)
                // For Alpha, likely need to stick to Windows or research X11 embedding in .NET
                // Returning parent handle is dangerous, better to return null or throw.
                return base.CreateNativeControlCore(parent);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return base.CreateNativeControlCore(parent);
            }

            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Win32.DestroyWindow(control.Handle);
            }

            base.DestroyNativeControlCore(control);
        }
    }

    // Simple P/Invoke Wrapper for Windows
    public static class Win32
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
           int dwExStyle,
           string lpClassName,
           string lpWindowName,
           uint dwStyle,
           int x,
           int y,
           int nWidth,
           int nHeight,
           IntPtr hWndParent,
           IntPtr hMenu,
           IntPtr hInstance,
           IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwc);

        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static bool _classRegistered = false;
        private static WndProc? _wndProcDelegate; // Keep reference to prevent GC

        public static IPlatformHandle CreateChildWindow(IntPtr parent)
        {
            EnsureClassRegistered();

            // WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN
            uint style = 0x40000000 | 0x10000000 | 0x02000000;

            IntPtr hWnd = CreateWindowEx(
                0,
                "GridPlayerHost",
                "",
                style,
                0, 0, 800, 600,
                parent,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            return new PlatformHandle(hWnd, "HWND");
        }

        private static void EnsureClassRegistered()
        {
            if (_classRegistered) return;

            _wndProcDelegate = CustomWndProc;

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                style = 0x00000003, // CS_HREDRAW | CS_VREDRAW
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = GetModuleHandle(null),
                hbrBackground = IntPtr.Zero, // No background, let mpv draw
                lpszClassName = "GridPlayerHost"
            };

            RegisterClassEx(ref wndClass);
            _classRegistered = true;
        }

        private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
