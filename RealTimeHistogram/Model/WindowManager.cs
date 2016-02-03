using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RealTimeHistogram.Model
{
    public class WindowManager
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        public WindowManager(){}

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        private static extern int GetWindowRect(IntPtr hwnd, ref RECT rc);

        /// <summary>
        /// MainWindowHandleを持つプロセス配列を取得する
        /// </summary>
        /// <returns></returns>
        public Process[] GetProcessesWithMainWindowHandle()
        {
            List<Process> processes = new List<Process>();

            foreach (Process p in Process.GetProcesses())
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    processes.Add(p);
                    RECT rc = new RECT();
                    GetWindowRect(p.MainWindowHandle, ref rc);
                    string msg = String.Format("name={0}, top={1}, left={2}, right={3}, bottom={4}.", p.ProcessName, rc.top, rc.left, rc.right, rc.bottom);
                    Console.WriteLine(msg);

                    Rectangle rect = GetWindowRectangle(p);
                }
            }

            return processes.ToArray();
        }

        /// <summary>
        /// 指定したプロセスのWindow位置を取得する
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public Rectangle GetWindowRectangle(Process p)
        {
            if (p == null) return Rectangle.Empty;
            RECT rc = new RECT();
            if (GetWindowRect(p.MainWindowHandle, ref rc) == 0) return Rectangle.Empty;

            Rectangle rect = new Rectangle(rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top);
            return rect;
        }
    }
}
