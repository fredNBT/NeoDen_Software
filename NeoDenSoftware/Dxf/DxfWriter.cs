using System.Globalization;
using System.IO;
using System.Text;
using NeoDenSoftware.Models;
using NeoDenSoftware.Rendering;

namespace NeoDenSoftware.Dxf;

/// <summary>
/// Hand-rolled writer for a minimal ASCII DXF file (no NuGet dependency - matching this project's
/// established preference for small hand-rolled format readers/writers over pulling in a library,
/// e.g. the Gerber parser, the STL parser, the ICO writer). Targets DXF R12 (<c>$ACADVER</c>
/// <c>AC1009</c>) with plain 2D <c>POLYLINE</c>/<c>VERTEX</c>/<c>SEQEND</c> entities - the oldest,
/// most broadly-compatible entity shape (no <c>LWPOLYLINE</c>, which needs R14+), so the file opens
/// in essentially anything that reads DXF at all.
/// </summary>
public static class DxfWriter
{
    /// <summary>Writes one closed <c>POLYLINE</c> per outline loop found in
    /// <paramref name="outline"/> (via <see cref="GerberRenderer.BuildClosedLoops"/> - the exact
    /// same chaining used to fill the board shape on screen, so the DXF matches what's rendered).
    /// Coordinates are written as-is (millimeters, whatever coordinate frame the caller's layer is
    /// already in) - no further translation happens here.</summary>
    public static void WriteOutline(string path, ParsedLayer outline)
    {
        var loops = GerberRenderer.BuildClosedLoops(outline.Primitives);
        var sb = new StringBuilder();

        void Code(int code, string value) => sb.Append(code).Append('\n').Append(value).Append('\n');
        void CodeD(int code, double value) => Code(code, value.ToString("F6", CultureInfo.InvariantCulture));

        Code(0, "SECTION");
        Code(2, "HEADER");
        Code(9, "$ACADVER");
        Code(1, "AC1009");
        Code(9, "$INSUNITS");
        Code(70, "4"); // 4 = millimeters - respected by most modern DXF readers even on an R12 file
        Code(0, "ENDSEC");

        Code(0, "SECTION");
        Code(2, "ENTITIES");
        foreach (var loop in loops)
        {
            if (loop.Count < 2) continue;

            Code(0, "POLYLINE");
            Code(8, "Outline");
            Code(66, "1"); // "entities follow" marker, required on POLYLINE
            Code(70, "1"); // closed polyline
            foreach (var point in loop)
            {
                Code(0, "VERTEX");
                Code(8, "Outline");
                CodeD(10, point.X);
                CodeD(20, point.Y);
            }
            Code(0, "SEQEND");
        }
        Code(0, "ENDSEC");
        Code(0, "EOF");

        File.WriteAllText(path, sb.ToString());
    }
}
