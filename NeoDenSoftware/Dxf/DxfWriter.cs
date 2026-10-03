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

    /// <summary>Rough average glyph width as a fraction of text height, used only to estimate
    /// whether a string will overflow the card - there's no real font here to measure against
    /// (unlike the tray STL's own label, which cuts actual glyph geometry and so measures its own
    /// vector outlines - a DXF TEXT entity just names a string and leaves rendering to whatever
    /// opens the file).</summary>
    private const double AverageCharWidthFactor = 0.6;

    /// <summary>Writes a small standalone DXF "label card" to accompany one tray STL: a
    /// <paramref name="widthMm"/> x <paramref name="heightMm"/> rectangle (corners at the origin,
    /// same closed-<c>POLYLINE</c> style as <see cref="WriteOutline"/>) containing the component's
    /// own name, centered, plus its Component Library description underneath when one exists -
    /// both as native DXF <c>TEXT</c> entities (center/middle justified), so the viewer's own font
    /// renders the glyphs - no font rendering happens here. Each line's height auto-shrinks (down
    /// to a 0.5mm floor) from its own cap when <see cref="AverageCharWidthFactor"/> estimates it
    /// would otherwise overflow the card's width, the same "shrink to fit, never omit" spirit as
    /// the tray STL's own label. Either <paramref name="name"/> or <paramref name="description"/>
    /// can be null/blank (e.g. a BOM row with no Value) - a blank one is simply skipped rather than
    /// leaving an empty TEXT entity, and the other still gets centered in the card on its own.</summary>
    public static void WriteLabelCard(string path, double widthMm, double heightMm, string? name, string? description)
    {
        var sb = new StringBuilder();

        void Code(int code, string value) => sb.Append(code).Append('\n').Append(value).Append('\n');
        void CodeD(int code, double value) => Code(code, value.ToString("F6", CultureInfo.InvariantCulture));

        Code(0, "SECTION");
        Code(2, "HEADER");
        Code(9, "$ACADVER");
        Code(1, "AC1009");
        Code(9, "$INSUNITS");
        Code(70, "4"); // 4 = millimeters
        Code(0, "ENDSEC");

        Code(0, "SECTION");
        Code(2, "ENTITIES");

        Code(0, "POLYLINE");
        Code(8, "Border");
        Code(66, "1");
        Code(70, "1"); // closed
        foreach (var (x, y) in new (double X, double Y)[] { (0, 0), (widthMm, 0), (widthMm, heightMm), (0, heightMm) })
        {
            Code(0, "VERTEX");
            Code(8, "Border");
            CodeD(10, x);
            CodeD(20, y);
        }
        Code(0, "SEQEND");

        void WriteCenteredText(string text, double centerX, double centerY, double textHeightMm)
        {
            Code(0, "TEXT");
            Code(8, "Label");
            CodeD(10, centerX);
            CodeD(20, centerY);
            CodeD(40, textHeightMm);
            Code(1, text);
            Code(72, "1"); // horizontal justification: center
            CodeD(11, centerX);
            CodeD(21, centerY);
            Code(73, "2"); // vertical justification: middle
        }

        double FitTextHeight(string text, double maxHeightMm, double availableWidthMm)
        {
            var length = text.Trim().Length;
            if (length == 0) return maxHeightMm;
            var estimatedWidth = length * AverageCharWidthFactor * maxHeightMm;
            return estimatedWidth <= availableWidthMm ? maxHeightMm : Math.Max(availableWidthMm / (length * AverageCharWidthFactor), 0.5);
        }

        const double margin = 2.0;
        const double lineGap = 2.0;
        const double maxNameHeightMm = 5.0;
        const double maxDescriptionHeightMm = 3.0;
        var availableWidth = Math.Max(widthMm - margin * 2, 1.0);
        var centerX = widthMm / 2;
        var centerY = heightMm / 2;

        var hasName = !string.IsNullOrWhiteSpace(name);
        var hasDescription = !string.IsNullOrWhiteSpace(description);

        if (hasName && hasDescription)
        {
            var nameHeight = FitTextHeight(name!, maxNameHeightMm, availableWidth);
            var descriptionHeight = FitTextHeight(description!, maxDescriptionHeightMm, availableWidth);
            var stackHeight = nameHeight + lineGap + descriptionHeight;
            WriteCenteredText(name!, centerX, centerY + stackHeight / 2 - nameHeight / 2, nameHeight);
            WriteCenteredText(description!, centerX, centerY - stackHeight / 2 + descriptionHeight / 2, descriptionHeight);
        }
        else if (hasName)
        {
            WriteCenteredText(name!, centerX, centerY, FitTextHeight(name!, maxNameHeightMm, availableWidth));
        }
        else if (hasDescription)
        {
            WriteCenteredText(description!, centerX, centerY, FitTextHeight(description!, maxDescriptionHeightMm, availableWidth));
        }

        Code(0, "ENDSEC");
        Code(0, "EOF");

        File.WriteAllText(path, sb.ToString());
    }
}
