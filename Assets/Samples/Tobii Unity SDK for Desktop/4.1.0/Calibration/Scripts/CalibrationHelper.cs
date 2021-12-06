using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using Tobii.EyeX.Client;
using Tobii.EyeX.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tobii.Gaming.Examples.Calibration
{
    public static class Win32Helpers
    {
        public const int GW_OWNER = 4;
        
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
        
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);
        
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        
        // When you don't want the ProcessId, use this overload and pass IntPtr.Zero for the second parameter
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        /// <summary>The GetForegroundWindow function returns a handle to the foreground window.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, uint nCmdShow);
    }

    public static class WindowHelpers
    {
        private static IntPtr Window;
        private static int ProcessId;
        
        private static bool IsMainWindow(IntPtr hwnd)
        {
            return (Win32Helpers.GetWindow(hwnd, Win32Helpers.GW_OWNER) == IntPtr.Zero) && Win32Helpers.IsWindowVisible(hwnd);
        }
        
        [MonoPInvokeCallback(typeof(Win32Helpers.EnumWindowsProc))]
        private static bool FindWindowWithThreadProcessIdCallback(IntPtr wnd, IntPtr param)
        {
            var windowProcessId = 0;
            Win32Helpers.GetWindowThreadProcessId(wnd, out windowProcessId);
            if (windowProcessId != ProcessId || !IsMainWindow(wnd))
            {
                return true;
            }

            Window = wnd;
            return false;
        }
        
        public static IntPtr FindWindowWithThreadProcessId(int processId)
        {
            ProcessId = processId;
            Window = new IntPtr();

            Win32Helpers.EnumWindows(FindWindowWithThreadProcessIdCallback, IntPtr.Zero);

            if (Window.Equals(IntPtr.Zero))
            {
                Debug.LogError("Could not find any window with process id " + processId);
            }

            return Window;
        }
        
        public static void CaptureFocus()
        {
            // force window to have focus
            uint foreThread = Win32Helpers.GetWindowThreadProcessId(Win32Helpers.GetForegroundWindow(), IntPtr.Zero);
            uint appThread = Win32Helpers.GetCurrentThreadId();
            const uint SW_SHOW = 5;
            IntPtr hwnd = WindowHelpers.FindWindowWithThreadProcessId(Process.GetCurrentProcess().Id);
            if (foreThread != appThread)
            {
                Win32Helpers.AttachThreadInput(foreThread, appThread, true);
                Win32Helpers.BringWindowToTop(hwnd);
                Win32Helpers.ShowWindow(Process.GetCurrentProcess().MainWindowHandle, SW_SHOW);
                Win32Helpers.AttachThreadInput(foreThread, appThread, false);
            }
            else
            {
                Win32Helpers.BringWindowToTop(hwnd);
                Win32Helpers.ShowWindow(hwnd, SW_SHOW);
            }
        }
    }

    public class CalibrationHelper : MonoBehaviour
    {
        private static Context _context;
        private static bool _calibrationIsOngoing;
        private static bool _connected;

        private static Context Context
        {
            get
            {
                if (_context == null)
                {
                    Tobii.EyeX.Client.Environment.Initialize(LogTarget.Trace);
                    _context = new Tobii.EyeX.Client.Context(true);
                }

                return _context;
            }
        }

        private void StartCalibration()
        {
            if (!_connected)
            {
                Debug.Log("Tobii is not connected. Can't run calibration.");
                return;
            }

            if (_calibrationIsOngoing)
            {
                Debug.Log("Calibration is ongoing.");
                return;
            }

            Debug.Log("Starting calibration.");
            Task.Run(() =>
            {
                _calibrationIsOngoing = true;
                Context.LaunchConfigurationTool(ConfigurationTool.GuestCalibration, CompletionHandler);
                Context.ConnectionStateChanged -= ContextOnConnectionStateChanged;
            });
        }

        private void CompletionHandler(AsyncData asyncdata)
        {
        }

        private static void StateChangedHandler(AsyncData asyncdata)
        {
            var state = asyncdata.GetDataAs<StateBag>()
                .GetStateValueOrDefault<EyeTrackingDeviceStatus>("eyeTracking.state");
            Debug.Log("StateChangedHandler: " + state);
            if (_calibrationIsOngoing)
            {
                if (state == EyeTrackingDeviceStatus.Tracking)
                {
                    Debug.Log("Calibration is finished.");
                    _calibrationIsOngoing = false;
                    Debug.Log("Return focus to Unity.");
                    WindowHelpers.CaptureFocus();
                }
            }
        }

        private static void ContextOnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            _connected = e.State == ConnectionState.Connected;
            Debug.Log("Tobii connection state: " + e.State);
            if (_connected)
            {
                Task.Run(() =>
                {
                    Debug.Log("RegisterStateChangedHandler.");
                    Context.RegisterStateChangedHandler("eyeTracking.state", StateChangedHandler);
                });
            }
        }

        void Start()
        {
            Debug.Log("Initializing Tobii context");
            Context.ConnectionStateChanged += ContextOnConnectionStateChanged;
            Context.EnableConnection();
            Debug.Log("Tobii context initialized");
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            Debug.Log("Disposing Tobii context");
            Context.DisableConnection();
            Context.Shutdown();
            Context.Dispose();
            Debug.Log("Tobii context is disposed");
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                StartCalibration();
            }
        }
    }
}