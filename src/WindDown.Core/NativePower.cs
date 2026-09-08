using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindDown.Core;

public static class NativePower
{
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Privileges { public uint Count; public Luid Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(nint token, bool disable, ref Privileges privileges, uint length, nint previous, nint returned);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint InitiateShutdownW(string? machine, string? message, uint grace, uint flags, uint reason);
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);
    [DllImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.U1)] public static extern bool IsPwrSuspendAllowed();
    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, nint info, int length, out int returned);
    public static Guid BootId()
    {
        nint memory = Marshal.AllocHGlobal(32);
        try
        {
            int status = NtQuerySystemInformation(90, memory, 32, out _);
            if (status != 0) throw new InvalidOperationException($"Windows boot identity could not be read (0x{status:X8}).");
            return Marshal.PtrToStructure<Guid>(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public static void CheckPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid)) throw new Win32Exception();
            var privileges = new Privileges { Count = 1, Luid = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0)) throw new Win32Exception();
            int error = Marshal.GetLastWin32Error();
            if (error != 0) throw new Win32Exception(error, "Your Windows account is not allowed to control power on this PC.");
        }
        finally { CloseHandle(token); }
    }
    public static void Shutdown()
    {
        CheckPrivilege();
        var error = InitiateShutdownW(null, "Scheduled by Wind Down", 0, 0x8, 0x80040000);
        if (error != 0) throw new Win32Exception((int)error);
    }
    public static void Sleep()
    {
        CheckPrivilege();
        if (!IsPwrSuspendAllowed()) throw new InvalidOperationException("Sleep is not available with this PC's current power configuration.");
        if (!SetSuspendState(false, false, false)) throw new Win32Exception();
    }
}
