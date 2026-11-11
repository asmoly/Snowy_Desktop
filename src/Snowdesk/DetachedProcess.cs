using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Snowdesk;

internal sealed class DetachedProcess : WaitHandle
{
    private const uint CreateNoWindow = 0x08000000;

    private DetachedProcess(nint handle)
    {
        SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: true);
    }

    public static DetachedProcess Start(string executable, string commandLine)
    {
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        // Inherited handles could keep a terminal pipeline open after "start" returns.
        if (!CreateProcessW(executable, new StringBuilder(commandLine), 0, 0, false,
                CreateNoWindow, 0, null, ref startup, out var process))
        {
            throw new Win32Exception();
        }

        using var thread = new SafeWaitHandle(process.Thread, ownsHandle: true);
        return new DetachedProcess(process.Process);
    }

    public uint ExitCode => GetExitCodeProcess(SafeWaitHandle, out uint code)
        ? code
        : throw new Win32Exception();

    public void Terminate()
    {
        if (!TerminateProcess(SafeWaitHandle, 1))
        {
            int error = Marshal.GetLastWin32Error();
            if (!WaitOne(0))
            {
                throw new Win32Exception(error);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int ConsoleColumns;
        public int ConsoleRows;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short ReservedSize;
        public nint ReservedBuffer;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        nint processAttributes, nint threadAttributes, bool inheritHandles, uint flags,
        nint environment, string? directory, ref StartupInfo startup, out ProcessInfo process);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeWaitHandle process, out uint exitCode);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool TerminateProcess(SafeWaitHandle process, uint exitCode);
}
