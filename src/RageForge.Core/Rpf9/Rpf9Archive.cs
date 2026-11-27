/* RAGEFORGE - RPF9 Archive */

namespace RageForge.Core.Rpf9;

public sealed class Rpf9Archive
{
    private readonly byte[] _rawToc;

    private Rpf9Archive(Rpf9Header header, byte[] rawToc)
    {
        Header = header;
        _rawToc = rawToc;
    }

    public Rpf9Header Header { get; }

    public ReadOnlySpan<byte> RawToc => _rawToc;


    public static Rpf9Archive Read(ReadOnlySpan<byte> data)
    {
        var header = Rpf9Header.Read(data);
        int tocStart = Rpf9Header.Size;
        long tocEnd = (long)tocStart + header.TocEnd;
        if (data.Length < tocEnd)
            throw new InvalidDataException($"Archive truncated: need {tocEnd} bytes for the TOC, have {data.Length}.");
        var rawToc = data.Slice(tocStart, (int)header.TocEnd).ToArray();
        return new Rpf9Archive(header, rawToc);
    }

    public static Rpf9Archive Open(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> headerBytes = stackalloc byte[Rpf9Header.Size];
        if (fs.Read(headerBytes) < Rpf9Header.Size)
            throw new InvalidDataException("File too small to contain an RPF9 header.");
        var header = Rpf9Header.Read(headerBytes);

        var rawToc = new byte[header.TocEnd];
        fs.Position = Rpf9Header.Size;
        if (fs.Read(rawToc) < rawToc.Length)
            throw new InvalidDataException("File truncated: TOC shorter than the header declares.");
        return new Rpf9Archive(header, rawToc);
    }
}
