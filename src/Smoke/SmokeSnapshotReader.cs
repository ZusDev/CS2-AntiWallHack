using System.Numerics;
using System.Runtime.InteropServices;

namespace AntiWallHack;

internal static class SmokeSnapshotReader
{
    public const int FrameBytes = 0x80000;

    public static bool Read(nint entity, int volumeOffset, float now, SmokeVolume output, byte[] frameBuffer)
    {
        if (entity <= 0 || entity > nint.MaxValue - volumeOffset - 240 || !float.IsFinite(now)) 
            return false;

        nint volume = entity + volumeOffset;
        Span<byte> header = stackalloc byte[240];
        Span<byte> finalHeader = stackalloc byte[240];

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!SmokeMemory.Read(volume, header)) 
                return false;

            long storageValue = BitConverter.ToInt64(header[112..]);
            int frame = BitConverter.ToInt32(header[236..]);
            float started = BitConverter.ToSingle(header[12..]);

            var center = new Vector3(BitConverter.ToSingle(header[212..]), BitConverter.ToSingle(header[216..]), BitConverter.ToSingle(header[220..]));

            float age = now - started;

            if (frame is not (0 or 1) || storageValue <= 0 || storageValue > long.MaxValue - 0x103008 || !SmokeOcclusion.Finite(center) || !float.IsFinite(age) || age < -0.25f || age > 30) 
                return false;

            nint storage = (nint)storageValue;

            if (!SmokeMemory.Read(storage + 8, output.Opaque) || !SmokeMemory.Read(storage + 0x3008 + frame * FrameBytes, frameBuffer) || !SmokeMemory.Read(volume, finalHeader)) 
                return false;

            if (!header.SequenceEqual(finalHeader)) 
                continue;

            if (!DecodeDensity(frameBuffer, output.Density)) 
                return false;

            output.Center = center; output.Age = age; output.StartTime = started;

            return true;
        }

        return false;
    }

    public static bool DecodeDensity(ReadOnlySpan<byte> bytes, Span<float> density)
    {
        if (bytes.Length != FrameBytes || density.Length != 32768) 
            return false;

        var values = MemoryMarshal.Cast<byte, float>(bytes);

        for (int i = 0; i < density.Length; i++)
        {
            float value = values[i * 4];

            if (!float.IsFinite(value) || value < 0) 
                return false;
                
            density[i] = value;
        }

        return true;
    }
}
