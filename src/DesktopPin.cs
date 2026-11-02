using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClickUpWidget
{
    /// <summary>
    /// Прикріплює вікно до робочого столу: завжди під іншими вікнами, без панелі завдань і Alt+Tab.
    /// Власник — вікно робочого столу (Progman), тож Win+D його не ховає.
    /// </summary>
    internal static class DesktopPin
    {
        public static IntPtr Attach(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            Native.MakeToolWindow(hwnd);

            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
                Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, progman);

            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            SendToBottom(hwnd);
            return hwnd;
        }

        private static IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Хоч би що відбувалося (клік, активація) — лишаємося під усіма вікнами.
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((wp.flags & Native.SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        public static void SendToBottom(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>Чи збережене місце досі на якомусь екрані (монітор могли від'єднати).</summary>
        public static bool OnScreen(double left, double top, double width)
        {
            return left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
                   && left + width >= SystemParameters.VirtualScreenLeft + 80
                   && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80
                   && top >= SystemParameters.VirtualScreenTop - 20;
        }
    }
}
