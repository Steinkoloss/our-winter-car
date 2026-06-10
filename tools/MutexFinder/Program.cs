// Lists named Mutant (mutex) kernel objects held by a target process by
// enumerating system handles, to discover the name of Unity's
// "Force Single Instance" mutex in My Winter Car.
//
// Usage: dotnet run -- <pid>

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const int SystemHandleInformation = 16;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x2;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, byte[] buffer, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr srcProc, IntPtr srcHandle, IntPtr dstProc, out IntPtr dstHandle, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE
    {
        public int ProcessId;
        public byte ObjectTypeNumber;
        public byte Flags;
        public ushort Handle;
        public IntPtr Object;
        public uint GrantedAccess;
    }

    private static int Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int targetPid))
        {
            Console.Error.WriteLine("usage: MutexFinder <pid>");
            return 2;
        }

        IntPtr targetProc = OpenProcess(PROCESS_DUP_HANDLE, false, targetPid);
        if (targetProc == IntPtr.Zero)
        {
            Console.Error.WriteLine($"OpenProcess failed (run as admin?) err={Marshal.GetLastWin32Error()}");
            return 1;
        }

        byte[] buffer = new byte[1 << 20];
        int status, len;
        while ((status = NtQuerySystemInformation(SystemHandleInformation, buffer, buffer.Length, out len)) == STATUS_INFO_LENGTH_MISMATCH)
            buffer = new byte[buffer.Length * 2];

        if (status != 0)
        {
            Console.Error.WriteLine($"NtQuerySystemInformation failed status=0x{status:X8}");
            return 1;
        }

        int handleCount = BitConverter.ToInt32(buffer, 0);
        int entrySize = Marshal.SizeOf<SYSTEM_HANDLE>();
        int offset = IntPtr.Size; // count field is pointer-sized-aligned

        IntPtr self = GetCurrentProcess();
        Console.WriteLine($"scanning {handleCount} system handles for pid {targetPid}...");

        for (int i = 0; i < handleCount; i++)
        {
            int recordOffset = offset + i * entrySize;
            if (recordOffset + entrySize > buffer.Length) break;

            var handle = BytesToStruct(buffer, recordOffset);
            if (handle.ProcessId != targetPid) continue;

            if (!DuplicateHandle(targetProc, (IntPtr)handle.Handle, self, out IntPtr dup, 0, false, DUPLICATE_SAME_ACCESS))
                continue;

            try
            {
                string type = QueryString(dup, ObjectTypeInformation);
                if (type != "Mutant") continue;

                string name = QueryString(dup, ObjectNameInformation);
                if (!string.IsNullOrEmpty(name))
                    Console.WriteLine($"MUTANT: {name}");
            }
            finally
            {
                CloseHandle(dup);
            }
        }

        CloseHandle(targetProc);
        return 0;
    }

    private static SYSTEM_HANDLE BytesToStruct(byte[] data, int offset)
    {
        GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<SYSTEM_HANDLE>(h.AddrOfPinnedObject() + offset);
        }
        finally
        {
            h.Free();
        }
    }

    private static string QueryString(IntPtr handle, int infoClass)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4096);
        try
        {
            int status = NtQueryObject(handle, infoClass, buffer, 4096, out _);
            if (status != 0) return string.Empty;

            // Layout: UNICODE_STRING { ushort Length; ushort MaxLength; IntPtr Buffer; }
            ushort length = (ushort)Marshal.ReadInt16(buffer);
            IntPtr strPtr = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            if (length == 0 || strPtr == IntPtr.Zero) return string.Empty;

            return Marshal.PtrToStringUni(strPtr, length / 2) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
