using System.Buffers.Binary;
using System.Text;

namespace MfgManager.Core.Mod;

/// <summary>
/// The "Product name" and "Product version" written inside a Windows .dll or .exe.
///
/// .NET's own FileVersionInfo only reads these on Windows, but the app must recognise the mod on Linux too,
/// so this reads the file format itself. The files come from game folders and Downloads, so nothing in them
/// is trusted: every offset is checked, and a damaged file just gives null.
/// </summary>
public sealed record PeVersionInfo(string? ProductName, string? ProductVersion)
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxResourceBytes = 64 * 1024 * 1024;
    // RT_VERSION.
    private const uint VersionResourceType = 16;

    /// <summary>Reads the file's version info. Null when it isn't a readable Windows program file.</summary>
    public static PeVersionInfo? Read(string path)
    {
        try
        {
            using FileStream file = File.OpenRead(path);
            return Read(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static PeVersionInfo? Read(Stream file)
    {
        try
        {
            byte[] headers = ReadAt(file, 0, MaxHeaderBytes);
            (uint resourceRva, Section section) = FindResourceSection(headers);
            byte[] resources = ReadAt(file, section.FileOffset, Math.Min(section.Size, MaxResourceBytes));
            byte[] data = FindVersionData(resources, resourceRva - section.Rva, section.Rva);
            return ReadVersionBlock(data);
        }
        catch (InvalidDataException)
        {
            // Not a program file, or a damaged one.
            return null;
        }
    }

    // ---------------- finding the resources ----------------

    private readonly record struct Section(uint Rva, uint FileOffset, int Size);

    private static (uint ResourceRva, Section Section) FindResourceSection(byte[] headers)
    {
        // "MZ".
        if (U16(headers, 0) != 0x5A4D) throw new InvalidDataException();
        int pe = (int)U32(headers, 0x3C);
        // "PE\0\0".
        if (U32(headers, pe) != 0x00004550) throw new InvalidDataException();

        ushort sectionCount = U16(headers, pe + 6);
        ushort optionalSize = U16(headers, pe + 20);
        int optional = pe + 24;
        int resourceEntry = U16(headers, optional) switch
        {
            // 32-bit program.
            0x10B => optional + 112,
            // 64-bit program.
            0x20B => optional + 128,
            _ => throw new InvalidDataException(),
        };
        uint resourceRva = U32(headers, resourceEntry);
        if (resourceRva == 0) throw new InvalidDataException();

        int table = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++)
        {
            int entry = table + i * 40;
            uint rva = U32(headers, entry + 12);
            uint rawSize = U32(headers, entry + 16);
            if (resourceRva >= rva && resourceRva < rva + rawSize)
                return (resourceRva, new Section(rva, U32(headers, entry + 20), (int)Math.Min(rawSize, MaxResourceBytes)));
        }
        throw new InvalidDataException();
    }

    /// <summary>Resources are a three-level tree: type, then id, then language. The version info is type 16.</summary>
    private static byte[] FindVersionData(byte[] resources, uint root, uint sectionRva)
    {
        uint offset = FindEntry(resources, root, root, VersionResourceType);
        offset = FindEntry(resources, root, offset, id: null);
        offset = FindEntry(resources, root, offset, id: null);

        uint dataRva = U32(resources, offset);
        uint size = U32(resources, offset + 4);
        if (dataRva < sectionRva || size > MaxResourceBytes) throw new InvalidDataException();
        uint start = dataRva - sectionRva;
        if (start + size > resources.Length) throw new InvalidDataException();
        return resources.AsSpan((int)start, (int)size).ToArray();
    }

    /// <summary>The entry with this id (or the first entry when id is null). Returns where it points to.</summary>
    private static uint FindEntry(byte[] resources, uint root, uint directory, uint? id)
    {
        int count = U16(resources, directory + 12) + U16(resources, directory + 14);
        for (int i = 0; i < count; i++)
        {
            uint entry = directory + 16 + (uint)i * 8;
            if (id is { } wanted && U32(resources, entry) != wanted) continue;
            // The top bit only says "points to a sub-directory".
            return root + (U32(resources, entry + 4) & 0x7FFFFFFF);
        }
        throw new InvalidDataException();
    }

    // ---------------- reading the version block ----------------

    /// <summary>
    /// The version data is a tree of blocks: VS_VERSION_INFO > StringFileInfo > one table per language > strings.
    /// Every block starts with its length, value length, type and a name, aligned to 4 bytes.
    /// </summary>
    private static PeVersionInfo ReadVersionBlock(byte[] data)
    {
        Block root = ReadBlockHeader(data, 0, data.Length);
        string? productName = null;
        string? productVersion = null;

        foreach ((int fileInfoStart, int fileInfoEnd) in Children(data, root))
        {
            Block fileInfo = ReadBlockHeader(data, fileInfoStart, fileInfoEnd);
            if (fileInfo.Name != "StringFileInfo") continue;

            foreach ((int tableStart, int tableEnd) in Children(data, fileInfo))
            {
                Block table = ReadBlockHeader(data, tableStart, tableEnd);
                foreach ((int textStart, int textEnd) in Children(data, table))
                {
                    Block text = ReadBlockHeader(data, textStart, textEnd);
                    if (text.Name == "ProductName") productName ??= ReadValueText(data, text);
                    if (text.Name == "ProductVersion") productVersion ??= ReadValueText(data, text);
                }
            }
        }
        return new PeVersionInfo(productName, productVersion);
    }

    private readonly record struct Block(string Name, int Start, int End, int ValueStart, int ValueLength, bool IsText);

    private static Block ReadBlockHeader(byte[] data, int start, int limit)
    {
        ushort length = U16(data, start);
        int end = start + length;
        if (length < 6 || end > limit) throw new InvalidDataException();

        ushort valueLength = U16(data, start + 2);
        bool isText = U16(data, start + 4) == 1;

        int nameStart = start + 6;
        int at = nameStart;
        while (U16(data, at) != 0)
        {
            at += 2;
            if (at >= end) throw new InvalidDataException();
        }
        string name = Encoding.Unicode.GetString(data, nameStart, at - nameStart);
        return new Block(name, start, end, Align4(at + 2), valueLength, isText);
    }

    private static IEnumerable<(int Start, int End)> Children(byte[] data, Block parent)
    {
        int valueBytes = parent.IsText ? parent.ValueLength * 2 : parent.ValueLength;
        int at = Align4(parent.ValueStart + valueBytes);
        while (at + 6 <= parent.End)
        {
            ushort length = U16(data, at);
            if (length == 0) yield break;
            yield return (at, Math.Min(at + length, parent.End));
            at = Align4(at + length);
        }
    }

    private static string? ReadValueText(byte[] data, Block block)
    {
        int bytes = block.ValueLength * 2;
        if (bytes == 0) return null;
        if (block.ValueStart + bytes > block.End) bytes = block.End - block.ValueStart;
        if (bytes <= 0) return null;
        return Encoding.Unicode.GetString(data, block.ValueStart, bytes).TrimEnd('\0');
    }

    private static int Align4(int offset) => (offset + 3) & ~3;

    // ---------------- checked reads: anything out of range means a damaged file ----------------

    private static byte[] ReadAt(Stream file, long offset, int maxBytes)
    {
        if (offset < 0 || offset >= file.Length) throw new InvalidDataException();
        byte[] buffer = new byte[(int)Math.Min(maxBytes, file.Length - offset)];
        file.Position = offset;
        file.ReadExactly(buffer);
        return buffer;
    }

    private static ushort U16(byte[] data, long at) =>
        at < 0 || at + 2 > data.Length ? throw new InvalidDataException() : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)at));

    private static uint U32(byte[] data, long at) =>
        at < 0 || at + 4 > data.Length ? throw new InvalidDataException() : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at));
}
