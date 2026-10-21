using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace AntiWallHack;

internal sealed class VisibilityMap
{
    private readonly int nx, ny, nz, words;
    private readonly Vector3 minimum;
    private readonly int[] cells;
    private readonly Vector3[] floors;
    private readonly ulong[] bits;
    public int Count => floors.Length;

    private VisibilityMap(int nx, int ny, int nz, Vector3 minimum, int[] cells, Vector3[] floors, ulong[] bits)
    {
        this.nx = nx;
        this.ny = ny;
        this.nz = nz;
        this.minimum = minimum;
        this.cells = cells;
        this.floors = floors;
        this.bits = bits;

        words = (Count + 63) / 64;
    }

    public const ulong HashSeed = 14695981039346656037UL;

    public static ulong Hash(ReadOnlySpan<byte> bytes, ulong hash = HashSeed)
    {
        foreach (byte value in bytes)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        return hash;
    }

    private static ulong ReadChunks(Stream stream, Span<byte> destination, ulong hash, CancellationToken cancellation)
    {
        for (int offset = 0; offset < destination.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = Math.Min(65536, destination.Length - offset);
            stream.ReadExactly(destination.Slice(offset, count));
            hash = Hash(destination.Slice(offset, count), hash);
            offset += count;

            if (offset < destination.Length && cancellation.WaitHandle.WaitOne(1))
            {
                cancellation.ThrowIfCancellationRequested();
            }
        }

        return hash;
    }

    private static ulong FileHash(string path, ulong expected, CancellationToken cancellation)
    {
        using var stream = File.OpenRead(path);

        if ((ulong)stream.Length != expected)
        {
            throw new InvalidDataException("VIS source size changed: " + path);
        }

        byte[] buffer = new byte[65536];
        ulong hash = HashSeed;
        ulong length = 0;
        int count;

        while ((count = stream.Read(buffer)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            length += (uint)count;
            hash = Hash(buffer.AsSpan(0, count), hash);

            if (length < expected && cancellation.WaitHandle.WaitOne(1))
            {
                cancellation.ThrowIfCancellationRequested();
            }
        }
        if (length != expected)
        {
            throw new InvalidDataException("VIS source changed during validation: " + path);
        }

        return hash;
    }

    public static VisibilityMap Load(string path, string root, string map, CancellationToken cancellation = default, Action<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke("resolving map archive");

        string archive = MapIdentity.Archive(root, map, cancellation);
        bool workshop = map.StartsWith("workshop/", StringComparison.Ordinal);
        string sourceRoot = workshop ? Path.GetDirectoryName(archive)! : Path.GetFullPath(root);
        string expectedSource = Path.GetRelativePath(sourceRoot, archive).Replace('\\', '/');

        using var file = File.OpenRead(path);

        Span<byte> sizeBytes = stackalloc byte[4];
        file.ReadExactly(sizeBytes);

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBytes);

        if (headerSize is 0 or > 65536)
        {
            throw new InvalidDataException("Invalid VIS manifest size");
        }

        byte[] header = new byte[headerSize];
        file.ReadExactly(header);

        using var document = JsonDocument.Parse(header);

        var data = document.RootElement;

        if (data.GetProperty("version").GetInt32() != 1 || data.GetProperty("map").GetString() != map || data.GetProperty("cellSize").GetInt32() != 64)
        {
            throw new InvalidDataException("VIS map/format mismatch");
        }

        var min = data.GetProperty("min").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        
        if (min.Length != 3 || min.Any(v => !float.IsFinite(v)))
        {
            throw new InvalidDataException("Invalid VIS origin");
        }

        int nx = data.GetProperty("nx").GetInt32(), ny = data.GetProperty("ny").GetInt32(), nz = data.GetProperty("nz").GetInt32();
        int count = data.GetProperty("count").GetInt32();

        if (nx is <= 0 or > 4096 || ny is <= 0 or > 4096 || nz is <= 0 or > 4096 || count is <= 0 or > 32768)
        {
            throw new InvalidDataException("Invalid VIS dimensions");
        }

        long grid = (long)nx * ny * nz;
        long bitCount = (long)count * ((count + 63) / 64);
        long bytes = grid * 4 + count * 12L + bitCount * 8;

        if (bytes > 256L * 1024 * 1024 || file.Length != 4L + headerSize + bytes)
        {
            throw new InvalidDataException("VIS table too large or truncated");
        }

        var sources = data.GetProperty("sources");

        if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() is 0 or > 128)
        {
            throw new InvalidDataException("Invalid VIS sources");
        }

        bool mapSource = false;

        foreach (var source in sources.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            string name = source.GetProperty("path").GetString() ?? "";

            if (name.Length == 0 || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p => p is "." or "..") || (workshop && name.Contains('/')))
            {
                throw new InvalidDataException("Invalid VIS source path");
            }

            mapSource |= name == expectedSource;
            progress?.Invoke("verifying archive " + name);

            if (FileHash(Path.Combine(sourceRoot, name), source.GetProperty("size").GetUInt64(), cancellation) != source.GetProperty("hash").GetUInt64())
            {
                throw new InvalidDataException("VIS source changed; rebake the map: " + name);
            }
        }

        if (!mapSource)
        {
            throw new InvalidDataException("VIS manifest lacks current map VPK: " + expectedSource);
        }

        progress?.Invoke("reading and verifying VIS payload");
        byte[] payload = new byte[(int)bytes];

        if (ReadChunks(file, payload, HashSeed, cancellation) != data.GetProperty("checksum").GetUInt64())
        {
            throw new InvalidDataException("VIS checksum mismatch");
        }

        int offset = 0;
        int[] cells = new int[(int)grid];

        for (int i = 0; i < cells.Length; i++, offset += 4)
        {
            if ((i & 16383) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            cells[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));

            if (cells[i] < -1 || cells[i] >= count)
            {
                throw new InvalidDataException("Invalid VIS cell index");
            }
        }

        Vector3[] floors = new Vector3[count];

        for (int i = 0; i < count; i++, offset += 12)
        {
            floors[i] = new Vector3(BitConverter.ToSingle(payload, offset), BitConverter.ToSingle(payload, offset + 4), BitConverter.ToSingle(payload, offset + 8));

            if (!CornerPrediction.Finite(floors[i]))
            {
                throw new InvalidDataException("Invalid VIS floor");
            }
        }

        ulong[] bits = new ulong[(int)bitCount];

        for (int i = 0; i < bits.Length; i++, offset += 8)
        {
            if ((i & 16383) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            bits[i] = BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(offset, 8));
        }

        return new VisibilityMap(nx, ny, nz, new(min[0], min[1], min[2]), cells, floors, bits);
    }

    public int Cell(Vector3 origin)
    {
        if (!CornerPrediction.Finite(origin))
        {
            return -1;
        }

        double x = Math.Floor(((double)origin.X - minimum.X) / 64);
        double y = Math.Floor(((double)origin.Y - minimum.Y) / 64);
        double z = Math.Floor(((double)origin.Z - minimum.Z) / 64);

        if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz)
        {
            return -1;
        }

        for (int layer = (int)z; layer >= 0 && layer >= (int)z - 1; layer--)
        {
            int cell = cells[(layer * ny + (int)y) * nx + (int)x];

            if (cell >= 0 && Math.Abs(origin.Z - floors[cell].Z) <= 16)
            {
                return cell;
            }
        }

        return -1;
    }

    public bool Visible(int from, int to) => from < 0 || to < 0 || from >= Count || to >= Count || from == to || (bits[from * words + (to >> 6)] & (1UL << (to & 63))) != 0;

    public bool Blocked(int viewer, int peek, int target) => !Visible(viewer, target) && !Visible(peek, target) && !Visible(target, viewer) && !Visible(target, peek);

    public static bool TrustHull(Vector3 mins, Vector3 maxs, float eyeHeight, float padding, bool grounded) => grounded && CornerPrediction.Finite(mins) && CornerPrediction.Finite(maxs) && float.IsFinite(padding) && padding is >= 0 and <= 38 && mins.X >= -16 && mins.Y >= -16 && mins.Z >= 0 && maxs.X <= 16 && maxs.Y <= 16 && maxs.Z <= 72 && eyeHeight is >= 40 and <= 68;
}
