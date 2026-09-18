using System.Windows.Media.Media3D;

namespace NeoDenSoftware.Models;

/// <summary>One triangle of a parsed STL mesh. <see cref="Normal"/> is always recomputed from the
/// vertices via cross product rather than trusted from the file - many STL exporters write
/// zero/garbage normals, which would otherwise flatten all shading when rendered.</summary>
public sealed record StlTriangle(Point3D V1, Point3D V2, Point3D V3, Vector3D Normal);

/// <summary>A parsed STL model: its triangles plus a computed bounding box (world units as found
/// in the file - no unit conversion is attempted, since STL carries no unit metadata).</summary>
public sealed class StlMesh
{
    public IReadOnlyList<StlTriangle> Triangles { get; }
    public Point3D BoundsMin { get; }
    public Point3D BoundsMax { get; }

    public Point3D Center => new(
        (BoundsMin.X + BoundsMax.X) / 2,
        (BoundsMin.Y + BoundsMax.Y) / 2,
        (BoundsMin.Z + BoundsMax.Z) / 2);

    public double SizeX => BoundsMax.X - BoundsMin.X;
    public double SizeY => BoundsMax.Y - BoundsMin.Y;
    public double SizeZ => BoundsMax.Z - BoundsMin.Z;

    public StlMesh(IReadOnlyList<StlTriangle> triangles)
    {
        if (triangles.Count == 0)
            throw new ArgumentException("A mesh needs at least one triangle.", nameof(triangles));

        Triangles = triangles;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        void Include(Point3D v)
        {
            if (v.X < minX) minX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Z < minZ) minZ = v.Z;
            if (v.X > maxX) maxX = v.X;
            if (v.Y > maxY) maxY = v.Y;
            if (v.Z > maxZ) maxZ = v.Z;
        }

        foreach (var t in triangles)
        {
            Include(t.V1);
            Include(t.V2);
            Include(t.V3);
        }

        BoundsMin = new Point3D(minX, minY, minZ);
        BoundsMax = new Point3D(maxX, maxY, maxZ);
    }
}
