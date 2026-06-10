using System;
using System.Runtime.InteropServices;

namespace WinterMP.Core.Util
{
    /// <summary>
    /// My Winter Car ships with Unity's "Force Single Instance" guard: at native
    /// startup it creates a named mutex (verified: <c>...SingleInstanceMutex...</c>)
    /// and any second copy exits with "Player is already running" before a single
    /// line of managed code runs.
    ///
    /// For the local two-instance test we need a second copy on the same machine.
    /// This releases the guard by closing the host process's own handle to that
    /// mutex — destroying the named object so the next launch passes the check.
    /// The host keeps running normally; it has simply given up its single-instance
    /// claim. Only invoked in the HostLocal test mode, never for normal players.
    ///
    /// Works on the current process only, so it needs no elevation. net35-safe.
    /// </summary>
    internal static class SingleInstanceUnlocker
    {
        private const int SystemHandleInformation = 16;
        private const int ObjectNameInformation = 1;
        private const int ObjectTypeInformation = 2;
        private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        // Empirically validated layout on this OS (x64): see tools/MutexFinder.
        private const int EntrySize = 24;
        private const int OffProcessId = 0;   // int
        private const int OffHandle = 6;      // ushort

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, byte[] buffer, int length, out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, int length, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentProcessId();

        /// <returns>true if a single-instance mutex was found and released.</returns>
        public static bool Release()
        {
            try
            {
                return ReleaseCore();
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"SingleInstanceUnlocker failed: {e.Message}");
                return false;
            }
        }

        private static bool ReleaseCore()
        {
            int myPid = GetCurrentProcessId();

            byte[] buffer = new byte[1 << 20];
            int status, len;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                status = NtQuerySystemInformation(SystemHandleInformation, buffer, buffer.Length, out len);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    buffer = new byte[buffer.Length * 2];
                    continue;
                }
                if (status != 0)
                {
                    WinterMPPlugin.Log.LogWarning($"NtQuerySystemInformation status=0x{status:X8}");
                    return false;
                }
                break;
            }

            int handleCount = BitConverter.ToInt32(buffer, 0);
            int baseOffset = IntPtr.Size;

            for (int i = 0; i < handleCount; i++)
            {
                int rec = baseOffset + i * EntrySize;
                if (rec + EntrySize > buffer.Length) break;

                int pid = BitConverter.ToInt32(buffer, rec + OffProcessId);
                if (pid != myPid) continue;

                ushort handleValue = BitConverter.ToUInt16(buffer, rec + OffHandle);
                var handle = (IntPtr)handleValue;

                if (QueryString(handle, ObjectTypeInformation) != "Mutant") continue;

                string name = QueryString(handle, ObjectNameInformation);
                if (name.IndexOf("SingleInstanceMutex", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                WinterMPPlugin.Log.LogInfo($"Releasing single-instance mutex so a second instance can start: {name}");
                CloseHandle(handle);
                return true;
            }

            WinterMPPlugin.Log.LogWarning("No single-instance mutex found to release (already released, or name changed).");
            return false;
        }

        private static string QueryString(IntPtr handle, int infoClass)
        {
            IntPtr buffer = Marshal.AllocHGlobal(2048);
            try
            {
                int status = NtQueryObject(handle, infoClass, buffer, 2048, out _);
                if (status != 0) return string.Empty;

                // UNICODE_STRING { ushort Length; ushort MaxLength; <pad on x64> IntPtr Buffer; }
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
}
