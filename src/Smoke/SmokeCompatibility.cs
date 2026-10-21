using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AntiWallHack;

internal static class SmokeCompatibility
{
    // Rechecked against the supplied Linux binary on 2026-10-21.
    // All accepted Linux binaries use the same private smoke layout.
    public const int WindowsVolumeOffset = 3008;
    public const int LinuxVolumeOffset = 3736;

    public static bool Matches(bool windows, long size, uint crc) => windows ? size == 34628248 && crc == 4192197381u : (size == 41724600 && crc == 775764504u) || (size == 41726328 && (crc == 131044578u || crc == 705576273u));

    public static bool SupportedSize(bool windows, long size) => windows ? size == 34628248 : size is 41724600 or 41726328;

    public static uint Crc32(Stream stream)
    {
        uint crc = uint.MaxValue;
        Span<byte> bytes = stackalloc byte[8192];
        int length;

        while ((length = stream.Read(bytes)) != 0)
            foreach (byte b in bytes[..length])
            {
                crc ^= b;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }

        return ~crc;
    }

    internal static string? LinuxMappedPath(IEnumerable<string> lines, ulong address)
    {
        foreach (string line in lines)
        {
            var parts = line.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 6) 
                continue;

            var bounds = parts[0].Split('-');

            if (bounds.Length != 2 || !ulong.TryParse(bounds[0], System.Globalization.NumberStyles.HexNumber, null, out ulong start) || !ulong.TryParse(bounds[1], System.Globalization.NumberStyles.HexNumber, null, out ulong end) || address < start || address >= end) 
                continue;

            string path = parts[5].TrimStart();

            if (!path.StartsWith('/') || path.EndsWith(" (deleted)", StringComparison.Ordinal)) 
                return null;

            return System.Text.RegularExpressions.Regex.Replace(path, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());
        }

        return null;
    }

    public static bool Verify(nint serverAddress, out string reason)
    {
        reason = "unsupported platform or server binary";

        if (!Environment.Is64BitProcess || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())) 
            return false;

        try
        {
            if (serverAddress == 0) 
            { 
                reason = "SwiftlyS2 server vtable address unavailable"; 
                return false; 
            }

            string? path;
            
            if (OperatingSystem.IsLinux())
                path = LinuxMappedPath(File.ReadLines("/proc/self/maps"), (ulong)serverAddress);
            else
            {
                using var process = Process.GetCurrentProcess();
                path = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => serverAddress >= m.BaseAddress && (ulong)(serverAddress - m.BaseAddress) < (ulong)m.ModuleMemorySize)?.FileName;
            }

            if (path == null) 
            {
                reason = "no readable module path contains the server vtable (mapping missing or deleted)";
                return false; 
            }

            using var file = File.OpenRead(path);
            
            if (!SupportedSize(OperatingSystem.IsWindows(), file.Length))
            { 
                reason = $"unsupported server binary {path}: size {file.Length}";
                return false; 
            }

            uint crc = Crc32(file);

            if (!Matches(OperatingSystem.IsWindows(), file.Length, crc)) 
            { 
                reason = $"unsupported server binary {path}: CRC32 {crc}"; 
                return false; 
            }

            reason = $"Server fingerprint matched: {path}";
            return true;
        }
        catch (Exception e) { reason = e.Message; return false; }
    }
}


internal static unsafe class SmokeMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct IoVector { public void* Address; public nuint Length; }
    [DllImport("libc", EntryPoint = "process_vm_readv", SetLastError = true)]
    private static extern nint ReadLinux(int pid, IoVector* local, nuint localCount, IoVector* remote, nuint remoteCount, nuint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, void* buffer, nuint size, out nuint read);

    public static bool Read(nint address, Span<byte> output)
    {
        if (address <= 0 || output.Length == 0 || address > nint.MaxValue - output.Length) 
            return false;

        fixed (byte* buffer = output)
        {
            if (OperatingSystem.IsWindows())
                return ReadProcessMemory(-1, address, buffer, (nuint)output.Length, out var count) && count == (nuint)output.Length;

            if (OperatingSystem.IsLinux())
            {
                var local = new IoVector { Address = buffer, Length = (nuint)output.Length };
                var remote = new IoVector { Address = (void*)address, Length = (nuint)output.Length };

                return ReadLinux(Environment.ProcessId, &local, 1, &remote, 1, 0) == output.Length;
            }
        }
        
        return false;
    }
}
