using System.Runtime.InteropServices;

namespace WindDown.App;
public sealed class TrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint Balloon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMax { public Point Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }
    private delegate nint SubclassProc(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadImage(nint instance, string name, uint type, int x, int y, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    private NotifyData data;
    private readonly SubclassProc callback;
    private readonly Action open, cancel, exit;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    public bool Available { get; private set; }
    public bool Active { get; set; }
    public TrayIcon(nint window, Action open, Action cancel, Action exit)
    {
        this.open = open; this.cancel = cancel; this.exit = exit; callback = WndProc;
        data = new NotifyData { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Flags = 7, Callback = 0x8001, Icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "WindDown.ico"), 1, 32, 32, 0x10), Tip = "Wind Down · Power timer", Info = "", Title = "" };
        if (!SetWindowSubclass(window, callback, 1, 0)) throw new InvalidOperationException("The tray window could not be initialized.");
        Available = Shell_NotifyIcon(0, ref data);
    }
    public void Update(string tip)
    {
        data.Tip = tip.Length > 127 ? tip[..127] : tip;
        if (Available) Available = Shell_NotifyIcon(1, ref data);
    }
    private nint WndProc(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint extra)
    {
        if (message == taskbarCreated) Available = Shell_NotifyIcon(0, ref data);
        if (message == 0x24)
        {
            var size = Marshal.PtrToStructure<MinMax>(lparam); var scale = GetDpiForWindow(window) / 96.0;
            size.MinTrack.X = (int)(440 * scale); size.MinTrack.Y = (int)(590 * scale); Marshal.StructureToPtr(size, lparam, false);
        }
        if (message == 0x8001)
        {
            if ((int)lparam == 0x203 || (int)lparam == 0x202) open();
            else if ((int)lparam == 0x205)
            {
                nint menu = CreatePopupMenu();
                try
                {
                    AppendMenu(menu, 0, 1, "Open Wind Down"); AppendMenu(menu, Active ? 0u : 1u, 2, "Cancel schedule"); AppendMenu(menu, 0x800, 0, ""); AppendMenu(menu, 0, 3, "Exit Wind Down");
                    GetCursorPos(out var point); SetForegroundWindow(window);
                    uint choice = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, 0);
                    if (choice == 1) open(); else if (choice == 2) cancel(); else if (choice == 3) exit();
                }
                finally { DestroyMenu(menu); }
            }
        }
        return DefSubclassProc(window, message, wparam, lparam);
    }
    public void Dispose() { Shell_NotifyIcon(2, ref data); RemoveWindowSubclass(data.Window, callback, 1); if (data.Icon != 0) DestroyIcon(data.Icon); }
}
