using System.Globalization;
using System.IO;
using System.Windows.Media.Media3D;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Stl;

/// <summary>
/// Hand-rolled parser for both binary and ASCII STL files - no NuGet dependency, matching this
/// project's existing preference for small hand-rolled parsers (Gerber, CSV) over pulling in a
/// library for a well-documented, simple format. STL carries no unit metadata; whatever numbers
/// are in the file are used as-is.
/// </summary>
public static class StlParser
{
    private const int BinaryHeaderSize = 80;
    private const int BinaryTriangleSize = 50; // 12 (normal) + 12*3 (vertices) + 2 (attribute byte count)

    public static StlMesh Parse(string path) => Parse(File.ReadAllBytes(path));

    public static StlMesh Parse(byte[] bytes)
    {
        if (TryParseBinary(bytes, out var mesh)) return mesh;
        return ParseAscii(bytes);
    }

    /// <summary>Binary STL has no magic number - the only reliable check is that the declared
    /// triangle count exactly accounts for the rest of the file's length. (Some binary STLs also
    /// start with the literal text "solid" for legacy-tool compatibility, so checking for that
    /// text is not a safe way to rule out binary.)</summary>
    private static bool TryParseBinary(byte[] bytes, out StlMesh mesh)
    {
        mesh = null!;
        if (bytes.Length < BinaryHeaderSize + 4) return false;

        var triangleCount = BitConverter.ToUInt32(bytes, BinaryHeaderSize);
        var expectedLength = BinaryHeaderSize + 4L + (long)triangleCount * BinaryTriangleSize;
        if (expectedLength != bytes.Length || triangleCount == 0) return false;

        var triangles = new List<StlTriangle>((int)triangleCount);
        var offset = BinaryHeaderSize + 4;
        for (var i = 0; i < triangleCount; i++)
        {
            offset += 12; // file's own normal - ignored, recomputed below
            var v1 = ReadPoint3D(bytes, offset); offset += 12;
            var v2 = ReadPoint3D(bytes, offset); offset += 12;
            var v3 = ReadPoint3D(bytes, offset); offset += 12;
            offset += 2; // attribute byte count

            triangles.Add(new StlTriangle(v1, v2, v3, ComputeNormal(v1, v2, v3)));
        }

        mesh = new StlMesh(triangles);
        return true;
    }

    private static Point3D ReadPoint3D(byte[] bytes, int offset) => new(
        BitConverter.ToSingle(bytes, offset),
        BitConverter.ToSingle(bytes, offset + 4),
        BitConverter.ToSingle(bytes, offset + 8));

    private static StlMesh ParseAscii(byte[] bytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var triangles = new List<StlTriangle>();
        var pending = new List<Point3D>(3);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;

            pending.Add(new Point3D(x, y, z));
            if (pending.Count < 3) continue;

            triangles.Add(new StlTriangle(pending[0], pending[1], pending[2], ComputeNormal(pending[0], pending[1], pending[2])));
            pending.Clear();
        }

        if (triangles.Count == 0)
            throw new FormatException("No triangles found - not a valid STL file.");

        return new StlMesh(triangles);
    }

    private static Vector3D ComputeNormal(Point3D a, Point3D b, Point3D c)
    {
        var normal = Vector3D.CrossProduct(b - a, c - a);
        if (normal.LengthSquared > 0) normal.Normalize();
        return normal;
    }
}
