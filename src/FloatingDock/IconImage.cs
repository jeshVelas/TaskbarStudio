using System.Buffers.Binary;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Personalization;

namespace FloatingDock;

internal static class IconImage
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static InvalidDataException Invalid() => new("Imagen PNG o ICO dañada o incompatible.");
    private static int Big(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32BigEndian(b);
    private static int Little(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt32LittleEndian(b);
    private static int Short(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt16LittleEndian(b);

    internal static byte[] Import(string path)
    {
        var ext = Path.GetExtension(path);
        bool ico = string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase);
        if (!ico && !string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Seleccione un archivo PNG o ICO.");
        return Decode(IconResources.ReadImport(path), ico);
    }

    internal static byte[] Decode(byte[] bytes, bool ico)
    {
        if (bytes.Length > IconResources.MaxImportBytes) throw Invalid();
        try
        {
            using var image = ico ? DecodeIco(bytes) : DecodePng(bytes);
            using var square = new Bitmap(IconResources.Side, IconResources.Side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(square))
            {
                double scale = (double)square.Width / Math.Max(image.Width, image.Height);
                int w = Math.Max(1, (int)Math.Round(image.Width * scale));
                int h = Math.Max(1, (int)Math.Round(image.Height * scale));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using var edges = new ImageAttributes();
                edges.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(image, new Rectangle((square.Width - w) / 2, (square.Height - h) / 2, w, h),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel, edges);
            }
            var pixels = new byte[IconResources.PixelBytes];
            var data = square.LockBits(new Rectangle(Point.Empty, square.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { square.UnlockBits(data); }
            return pixels;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException or NotSupportedException)
        {
            throw new InvalidDataException("Windows no pudo decodificar el icono.", ex);
        }
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        uint value = (uint)i;
        for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
        return value;
    }).ToArray();

    private static Bitmap DecodePng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || !bytes.StartsWith(PngSignature) || Big(bytes[8..]) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) || Big(bytes[16..]) is < 1 or > 1024 ||
            Big(bytes[20..]) is < 1 or > 1024) throw Invalid();
        using var clean = new MemoryStream();
        clean.Write(PngSignature);
        bool hasData = false, ended = false;
        for (int offset = 8; offset < bytes.Length;)
        {
            var chunk = bytes[offset..];
            if (chunk.Length < 12) throw Invalid();
            int length = Big(chunk);
            if (length < 0 || length > chunk.Length - 12) throw Invalid();
            var type = chunk.Slice(4, 4);
            uint crc = uint.MaxValue;
            foreach (byte b in chunk.Slice(4, length + 4)) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(chunk[(8 + length)..])) throw Invalid();
            offset += length + 12;
            if (type.SequenceEqual("IHDR"u8) && clean.Length != 8) throw Invalid();
            if (type.SequenceEqual("acTL"u8)) throw Invalid();
            if (type.SequenceEqual("PLTE"u8) && (length is < 3 or > 768 || length % 3 != 0)) throw Invalid();
            if (type.SequenceEqual("tRNS"u8) && length > 256) throw Invalid();
            if (type.SequenceEqual("IDAT"u8)) hasData = true;
            if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || offset != bytes.Length || !hasData) throw Invalid();
                ended = true;
            }
            // Only pixel data, palette and transparency reach the native decoder.
            if (type.SequenceEqual("IHDR"u8) || type.SequenceEqual("PLTE"u8) || type.SequenceEqual("IDAT"u8) ||
                type.SequenceEqual("tRNS"u8) || type.SequenceEqual("IEND"u8)) clean.Write(chunk[..(length + 12)]);
            else if ((type[0] & 32) == 0) throw Invalid();
        }
        if (!ended) throw Invalid();
        clean.Position = 0;
        using var decoded = Image.FromStream(clean, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(decoded);
    }

    private static Bitmap DecodeIco(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 22 || Little(bytes) != 0x00010000) throw Invalid();
        int count = Short(bytes[4..]);
        if (count is < 1 or > 64 || 6 + count * 16 > bytes.Length) throw Invalid();
        int best = 0, bestArea = 0;
        for (int i = 0; i < count; i++)
        {
            int index = 6 + i * 16;
            var entry = bytes[index..];
            int length = Little(entry[8..]), start = Little(entry[12..]);
            if (length < 1 || start < 6 + count * 16 || start > bytes.Length - length) throw Invalid();
            int area = Dimension(entry[0]) * Dimension(entry[1]);
            if (area > bestArea) { best = index; bestArea = area; }
        }
        var selected = bytes.Slice(best, 16);
        var frame = bytes.Slice(Little(selected[12..]), Little(selected[8..]));
        int w = Dimension(selected[0]), h = Dimension(selected[1]);
        if (frame.StartsWith(PngSignature))
        {
            if (frame.Length < 33 || Big(frame[16..]) != w || Big(frame[20..]) != h) throw Invalid();
            return DecodePng(frame);
        }
        // Bound the actual DIB dimensions, palette and buffers before handing a single frame to Windows.
        if (frame.Length < 40 || Little(frame) != 40 || Little(frame[4..]) != w || Little(frame[8..]) != h * 2 ||
            Short(frame[12..]) != 1 || Little(frame[16..]) != 0) throw Invalid();
        int imageBytes = Little(frame[20..]);
        if (imageBytes < 0 || imageBytes > frame.Length - 40) throw Invalid();
        int depth = Short(frame[14..]), colors = Little(frame[32..]);
        if (depth is not (1 or 4 or 8 or 16 or 24 or 32)) throw Invalid();
        int paletteMax = depth <= 8 ? 1 << depth : 0;
        if (colors < 0 || colors > paletteMax) throw Invalid();
        int minBytes = 40 + 4 * (colors == 0 ? paletteMax : colors) + ((w * depth + 31) / 32 * 4) * h;
        if (depth != 32) minBytes += (w + 31) / 32 * 4 * h;
        if (frame.Length < minBytes) throw Invalid();
        var single = new byte[22 + frame.Length];
        single[2] = single[4] = 1;
        selected.CopyTo(single.AsSpan(6));
        BinaryPrimitives.WriteInt32LittleEndian(single.AsSpan(18), 22);
        frame.CopyTo(single.AsSpan(22));
        using var input = new MemoryStream(single, writable: false);
        using var icon = new Icon(input, w, h);
        return icon.ToBitmap();
    }

    private static int Dimension(byte b) => b == 0 ? 256 : b;

    internal static Bitmap Render(byte[] pixels, int size)
    {
        using var original = new Bitmap(IconResources.Side, IconResources.Side, PixelFormat.Format32bppArgb);
        var data = original.LockBits(new Rectangle(Point.Empty, original.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, IconResources.PixelBytes); }
        finally { original.UnlockBits(data); }
        return new Bitmap(original, size, size);
    }
}
