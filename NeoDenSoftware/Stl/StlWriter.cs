using System.Globalization;
using System.IO;
using System.Text;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Stl;

/// <summary>
/// Hand-rolled writer for an ASCII STL file (no NuGet dependency - matching this project's
/// established preference for small hand-rolled format readers/writers, e.g. <see cref="StlParser"/>
/// and <see cref="Dxf.DxfWriter"/>). Round-trips with <see cref="StlParser"/>'s own ASCII path.
/// </summary>
public static class StlWriter
{
    public static void WriteAscii(string path, StlMesh mesh)
    {
        var sb = new StringBuilder();

        string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);

        sb.Append("solid tray\n");
        foreach (var t in mesh.Triangles)
        {
            sb.Append("facet normal ").Append(F(t.Normal.X)).Append(' ').Append(F(t.Normal.Y)).Append(' ').Append(F(t.Normal.Z)).Append('\n');
            sb.Append("outer loop\n");
            sb.Append("vertex ").Append(F(t.V1.X)).Append(' ').Append(F(t.V1.Y)).Append(' ').Append(F(t.V1.Z)).Append('\n');
            sb.Append("vertex ").Append(F(t.V2.X)).Append(' ').Append(F(t.V2.Y)).Append(' ').Append(F(t.V2.Z)).Append('\n');
            sb.Append("vertex ").Append(F(t.V3.X)).Append(' ').Append(F(t.V3.Y)).Append(' ').Append(F(t.V3.Z)).Append('\n');
            sb.Append("endloop\n");
            sb.Append("endfacet\n");
        }
        sb.Append("endsolid tray\n");

        File.WriteAllText(path, sb.ToString());
    }
}
