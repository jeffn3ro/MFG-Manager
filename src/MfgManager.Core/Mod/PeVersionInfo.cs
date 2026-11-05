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
    private const uint VersionResourceType = 16;   // RT_VERSION

    /// <summary>Reads the file's version info. Null when it isn't a readable Windows program file.</summary>
    public static PeVersionInfo? Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
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
            var headers = ReadAt(file, 0, MaxHeaderBytes);
            var (resourceRva, section) = FindResourceSection(headers);
            var resources = ReadAt(file, section.FileOffset, Math.Min(section.Size, MaxResourceBytes));
            var data = FindVersionData(resources, resourceRva - section.Rva, section.Rva);
            return ReadVersionBlock(data);
        }
        catch (InvalidDataException)
        {
            return null;   // not a program file, or a damaged one
        }
    }

    // ---------------- finding the resources ----------------

    private readonly record struct Section(uint Rva, uint FileOffset, int Size);

    private static (uint ResourceRva, Section Section) FindResourceSection(byte[] headers)
    {
        if (U16(headers, 0) != 0x5A4D) throw new InvalidDataException();           // "MZ"
        var pe = (int)U32(headers, 0x3C);
        if (U32(headers, pe) != 0x00004550) throw new InvalidDataException();      // "PE\0\0"

        var sectionCount = U16(headers, pe + 6);
        var optionalSize = U16(headers, pe + 20);
        var optional = pe + 24;
        var resourceEntry = U16(headers, optional) switch
        {
            0x10B => optional + 112,   // 32-bit program
            0x20B => optional + 128,   // 64-bit program
            _ => throw new InvalidDataException(),
        };
        var resourceRva = U32(headers, resourceEntry);
        if (resourceRva == 0) throw new InvalidDataException();

        var table = optional + optionalSize;
        for (var i = 0; i < sectionCount; i++)
        {
            var entry = table + i * 40;
            var rva = U32(headers, entry + 12);
            var rawSize = U32(headers, entry + 16);
            if (resourceRva >= rva && resourceRva < rva + rawSize)
                return (resourceRva, new Section(rva, U32(headers, entry + 20), (int)Math.Min(rawSize, MaxResourceBytes)));
        }
        throw new InvalidDataException();
    }

    /// <summary>Resources are a three-level tree: type, then id, then language. The version info is type 16.</summary>
    private static byte[] FindVersionData(byte[] resources, uint root, uint sectionRva)
    {
        var offset = FindEntry(resources, root, root, VersionResourceType);
        offset = FindEntry(resources, root, offset, id: null);
        offset = FindEntry(resources, root, offset, id: null);

        var dataRva = U32(resources, offset);
        var size = U32(resources, offset + 4);
        if (dataRva < sectionRva || size > MaxResourceBytes) throw new InvalidDataException();
        var start = dataRva - sectionRva;
        if (start + size > resources.Length) throw new InvalidDataException();
        return resources.AsSpan((int)start, (int)size).ToArray();
    }

    /// <summary>The entry with this id (or the first entry when id is null). Returns where it points to.</summary>
    private static uint FindEntry(byte[] resources, uint root, uint directory, uint? id)
    {
        var count = U16(resources, directory + 12) + U16(resources, directory + 14);
        for (var i = 0; i < count; i++)
        {
            var entry = directory + 16 + (uint)i * 8;
            if (id is { } wanted && U32(resources, entry) != wanted) continue;
            return root + (U32(resources, entry + 4) & 0x7FFFFFFF);   // the top bit only says "points to a sub-directory"
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
        var root = ReadBlockHeader(data, 0, data.Length);
        string? productName = null, productVersion = null;

        foreach (var (fileInfoStart, fileInfoEnd) in Children(data, root))
        {
            var fileInfo = ReadBlockHeader(data, fileInfoStart, fileInfoEnd);
            if (fileInfo.Name != "StringFileInfo") continue;

            foreach (var (tableStart, tableEnd) in Children(data, fileInfo))
            {
                var table = ReadBlockHeader(data, tableStart, tableEnd);
                foreach (var (textStart, textEnd) in Children(data, table))
                {
                    var text = ReadBlockHeader(data, textStart, textEnd);
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
        var length = U16(data, start);
        var end = start + length;
        if (length < 6 || end > limit) throw new InvalidDataException();

        var valueLength = U16(data, start + 2);
        var isText = U16(data, start + 4) == 1;

        var nameStart = start + 6;
        var at = nameStart;
        while (U16(data, at) != 0)
        {
            at += 2;
            if (at >= end) throw new InvalidDataException();
        }
        var name = Encoding.Unicode.GetString(data, nameStart, at - nameStart);
        return new Block(name, start, end, Align4(at + 2), valueLength, isText);
    }

    private static IEnumerable<(int Start, int End)> Children(byte[] data, Block parent)
    {
        var valueBytes = parent.IsText ? parent.ValueLength * 2 : parent.ValueLength;
        var at = Align4(parent.ValueStart + valueBytes);
        while (at + 6 <= parent.End)
        {
            var length = U16(data, at);
            if (length == 0) yield break;
            yield return (at, Math.Min(at + length, parent.End));
            at = Align4(at + length);
        }
    }

    private static string? ReadValueText(byte[] data, Block block)
    {
        var bytes = block.ValueLength * 2;
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
        var buffer = new byte[(int)Math.Min(maxBytes, file.Length - offset)];
        file.Position = offset;
        file.ReadExactly(buffer);
        return buffer;
    }

    private static ushort U16(byte[] data, long at) =>
        at < 0 || at + 2 > data.Length ? throw new InvalidDataException() : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)at));

    private static uint U32(byte[] data, long at) =>
        at < 0 || at + 4 > data.Length ? throw new InvalidDataException() : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at));
}
