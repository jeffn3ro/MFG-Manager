using System.Buffers.Binary;
using System.Text;

namespace MfgManager.Tests.Core;

/// <summary>
/// Builds a tiny Windows program file holding only a version resource, so the reader can be tested on any
/// system and without the real mod. The file can't run: it contains no code.
/// </summary>
public static class FakePe
{
    public static byte[] Build(string productName, string productVersion, bool is64Bit = true)
    {
        var version = VersionBlock(productName, productVersion);
        const int sectionRva = 0x1000;
        const int headersSize = 0x400;

        // Resource section: three directory levels, one data entry, then the version data.
        var resources = new byte[16 * 3 + 8 * 3 + 16 + version.Length];
        void Directory(int at, uint id, uint target)
        {
            WriteU16(resources, at + 14, 1);   // one entry
            WriteU32(resources, at + 16, id);
            WriteU32(resources, at + 20, target);
        }
        const int level2 = 24, level3 = 48, dataEntry = 72, dataStart = 88;
        Directory(0, 16, 0x80000000 | level2);     // type 16 = version info
        Directory(level2, 1, 0x80000000 | level3); // id 1
        Directory(level3, 0x409, dataEntry);       // language: English
        WriteU32(resources, dataEntry, sectionRva + dataStart);
        WriteU32(resources, dataEntry + 4, (uint)version.Length);
        Array.Resize(ref resources, dataStart + version.Length);
        version.CopyTo(resources, dataStart);

        var file = new byte[headersSize + resources.Length];
        const int pe = 0x80;
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        WriteU32(file, 0x3C, pe);
        file[pe] = (byte)'P'; file[pe + 1] = (byte)'E';
        WriteU16(file, pe + 6, 1);                                // one section

        var optionalSize = is64Bit ? 240 : 224;
        WriteU16(file, pe + 20, (ushort)optionalSize);
        var optional = pe + 24;
        WriteU16(file, optional, (ushort)(is64Bit ? 0x20B : 0x10B));
        WriteU32(file, optional + (is64Bit ? 128 : 112), sectionRva);   // where the resources are
        WriteU32(file, optional + (is64Bit ? 132 : 116), (uint)resources.Length);

        var section = optional + optionalSize;
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(file, section);
        WriteU32(file, section + 8, (uint)resources.Length);
        WriteU32(file, section + 12, sectionRva);
        WriteU32(file, section + 16, (uint)resources.Length);
        WriteU32(file, section + 20, headersSize);

        resources.CopyTo(file, headersSize);
        return file;
    }

    private static byte[] VersionBlock(string productName, string productVersion)
    {
        var strings = Concat(Block("ProductName", productName), Block("ProductVersion", productVersion));
        var table = Block("040904B0", null, strings);
        var stringFileInfo = Block("StringFileInfo", null, table);
        return Block("VS_VERSION_INFO", null, stringFileInfo, fixedInfoBytes: 52);
    }

    private static byte[] Block(string name, string? text, byte[]? children = null, int fixedInfoBytes = 0)
    {
        var value = text is null ? new byte[fixedInfoBytes] : Encoding.Unicode.GetBytes(text + "\0");
        var key = Encoding.Unicode.GetBytes(name + "\0");
        var body = new List<byte>();
        body.AddRange(new byte[6]);
        body.AddRange(key);
        Pad(body);
        body.AddRange(value);
        Pad(body);
        if (children is not null) body.AddRange(children);

        var bytes = body.ToArray();
        WriteU16(bytes, 0, (ushort)bytes.Length);
        WriteU16(bytes, 2, (ushort)(text is null ? fixedInfoBytes : value.Length / 2));
        WriteU16(bytes, 4, (ushort)(text is null ? 0 : 1));
        return bytes;
    }

    private static void Pad(List<byte> bytes)
    {
        while (bytes.Count % 4 != 0) bytes.Add(0);
    }

    private static byte[] Concat(byte[] a, byte[] b) => [.. a, .. b];

    private static void WriteU16(byte[] data, int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), value);
    private static void WriteU32(byte[] data, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
}
