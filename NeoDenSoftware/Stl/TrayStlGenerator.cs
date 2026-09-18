using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Stl;

/// <summary>
/// Builds a solid box tray with <see cref="TrayStlSettings.PocketCount"/> rectangular pockets
/// recessed into the top (sized per footprint), a groove recessed along both long edges, and an
/// optional depressed component-name label - modeled directly from a real reference tray the user
/// provided (Fusion 360 design + technical drawing for a SOIC-16 part). No CSG/boolean subtraction
/// is needed or used anywhere in this app (no mesh-geometry library) - every cavity is instead
/// produced by tiling ordinary solid boxes around it, via <see cref="AddSolidWithCutouts"/>'s grid
/// decomposition.
/// </summary>
public static class TrayStlGenerator
{
    public static StlMesh BuildTray(FootprintDefinition footprint, TrayStlSettings settings, string? labelText = null)
    {
        var boxLength = Math.Max(settings.BoxLengthMm, 1);
        var boxWidth = Math.Max(settings.BoxWidthMm, 1);
        var boxHeight = Math.Max(settings.BoxHeightMm, 0.1);
        var pocketCount = Math.Max(settings.PocketCount, 1);
        var pocketLengthMm = Math.Max(footprint.LengthMm + settings.ExtraLengthMm, 0.1);
        var pocketWidthMm = Math.Max(footprint.WidthMm + settings.ExtraWidthMm, 0.1);
        var pocketDepthMm = Math.Min(Math.Max(footprint.HeightMm, 0.1), boxHeight - 0.1);
        var grooveWidth = Math.Max(settings.GrooveWidthMm, 0.1);
        var grooveDepthMm = Math.Min(Math.Max(settings.GrooveDepthMm, 0.1), boxHeight - 0.1);

        // Pockets are evenly spaced along the box's length, centered across its width - N pockets
        // and N+1 equal gaps ("wall,pocket,wall,pocket,...,wall").
        var gap = Math.Max((boxLength - pocketCount * pocketLengthMm) / (pocketCount + 1), 0);
        var pocketY0 = (boxWidth - pocketWidthMm) / 2;
        var pocketY1 = pocketY0 + pocketWidthMm;

        var pockets = new List<(double X0, double X1, double Y0, double Y1)>();
        for (var k = 0; k < pocketCount; k++)
        {
            var x0 = gap + k * (pocketLengthMm + gap);
            pockets.Add((x0, x0 + pocketLengthMm, pocketY0, pocketY1));
        }

        var grooves = new List<(double X0, double X1, double Y0, double Y1)>
        {
            (0, boxLength, 0, grooveWidth),
            (0, boxLength, boxWidth - grooveWidth, boxWidth),
        };

        // The label sits "at the top center" of the tray: horizontally centered across the WHOLE
        // box length, in the Y margin strip between the pockets' own top edge and the top groove -
        // since pockets are always Y-centered in the box, that strip runs the full box length with
        // no pocket ever reaching into it, so centering in X doesn't need to dodge pocket
        // positions the way the first version's end-gap placement did. TextHeightMm is a ceiling,
        // not a fixed size - BuildTextCutouts shrinks it further if needed so the label always
        // actually appears (see that method's own doc comment). Never placed if labelText is
        // null/blank.
        var textCutouts = string.IsNullOrWhiteSpace(labelText)
            ? []
            : BuildTextCutouts(labelText, settings.TextHeightMm, centerX: boxLength / 2, centerY: (pocketY1 + (boxWidth - grooveWidth)) / 2,
                maxWidthMm: boxLength - 4, maxHeightMm: boxWidth - grooveWidth - pocketY1);

        var triangles = new List<StlTriangle>();

        // General N-feature depth banding: each feature (pockets/groove/label) cuts from its own
        // depth down to the top; the box is sliced at every distinct depth boundary, and each
        // resulting Z band gets exactly the cutouts of every feature whose cut reaches that deep.
        // Generalizes what used to be a hand-special-cased "shallower vs. deeper of two features"
        // split into any number of independently-depth-banded features.
        var features = new List<(double DepthMm, List<(double X0, double X1, double Y0, double Y1)> Cutouts)>
        {
            (pocketDepthMm, pockets),
            (grooveDepthMm, grooves),
        };
        if (textCutouts.Count > 0) features.Add((Math.Min(Math.Max(settings.TextDepthMm, 0.1), boxHeight - 0.1), textCutouts));

        var thresholds = new SortedSet<double> { 0, boxHeight };
        foreach (var f in features) thresholds.Add(Math.Clamp(boxHeight - f.DepthMm, 0, boxHeight));
        var zList = thresholds.ToList();

        for (var i = 0; i < zList.Count - 1; i++)
        {
            var zBandBottom = zList[i];
            var zBandTop = zList[i + 1];
            var activeCutouts = features
                .Where(f => boxHeight - f.DepthMm <= zBandBottom + 1e-9)
                .SelectMany(f => f.Cutouts)
                .ToList();
            AddSolidWithCutouts(triangles, 0, boxLength, 0, boxWidth, zBandBottom, zBandTop, activeCutouts);
        }

        return new StlMesh(triangles);
    }

    /// <summary>Rasterizes <paramref name="text"/>'s real glyph outlines (via WPF's own
    /// <see cref="FormattedText.BuildGeometry"/> - no new font/vector dependency needed) into a
    /// dense grid of small cutout rectangles, ~0.3mm each - correctly handles letter holes
    /// ('A'/'O'/'e'/...) via <see cref="Geometry.FillContains(Point)"/>'s own fill-rule logic,
    /// rather than needing a general polygon triangulator this app has no reason to otherwise
    /// need. <paramref name="desiredHeightMm"/> is the target physical glyph height in mm (a
    /// ceiling, not a fixed size): if the text doesn't fit <paramref name="maxWidthMm"/>/
    /// <paramref name="maxHeightMm"/> at that height, it's shrunk down further until it does (the
    /// label must actually appear on the tray - "if there is space" turned out to mean "make it
    /// fit", confirmed after the very first real export at this generator's own default box size
    /// came out with no label at all, since a 4mm-tall "CH340G" is still wider than the default
    /// layout's own first pocket gap). Only returns empty (no label at all) when there's
    /// essentially no usable space left even after shrinking, or the text is empty/whitespace.</summary>
    private static List<(double X0, double X1, double Y0, double Y1)> BuildTextCutouts(
        string text, double desiredHeightMm, double centerX, double centerY, double maxWidthMm, double maxHeightMm)
    {
        // WPF's device-independent unit is 1/96 inch, the same unit FormattedText's emSize is
        // expressed in - this app treats mm as its native unit everywhere else, so measurements
        // convert via the standard mm-per-inch factor.
        const double mmPerWpfUnit = 25.4 / 96.0;
        if (maxWidthMm <= 0 || maxHeightMm <= 0 || desiredHeightMm <= 0) return [];

        var typeface = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        // "Font size" here means the actual printed glyph height in mm, not an abstract WPF/point
        // unit - measure once at an arbitrary reference emSize to find this font's own
        // height-per-emSize ratio (varies by font/weight/the specific text's own ascender/descender
        // mix), then scale directly to the requested physical height.
        const double referenceEmSize = 100;
        var referenceBounds = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, referenceEmSize, Brushes.Black, 1.0)
            .BuildGeometry(new Point(0, 0)).Bounds;
        if (referenceBounds.IsEmpty || referenceBounds.Height <= 0) return [];
        var trySize = referenceEmSize * (desiredHeightMm / (referenceBounds.Height * mmPerWpfUnit));

        Geometry geometry;
        Rect boundsWpf;

        // Measure once at the requested size, then shrink-and-remeasure as needed to fit - outline
        // font metrics scale very close to linearly with size but not perfectly (hinting/rounding
        // at small sizes), so a couple of iterations converges reliably rather than trusting one
        // single analytical guess.
        while (true)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, trySize, Brushes.Black, 1.0);
            geometry = formatted.BuildGeometry(new Point(0, 0));
            boundsWpf = geometry.Bounds;
            if (boundsWpf.IsEmpty || boundsWpf.Width <= 0 || boundsWpf.Height <= 0) return [];

            var textWidthMm = boundsWpf.Width * mmPerWpfUnit;
            var textHeightMm = boundsWpf.Height * mmPerWpfUnit;
            if (textWidthMm <= maxWidthMm && textHeightMm <= maxHeightMm) break;

            var scale = Math.Min(maxWidthMm / textWidthMm, maxHeightMm / textHeightMm) * 0.98;
            trySize *= scale;
            if (trySize < 1) return []; // no usable space at all, even fully shrunk - a real, last-resort skip
        }

        var finalWidthMm = boundsWpf.Width * mmPerWpfUnit;
        var finalHeightMm = boundsWpf.Height * mmPerWpfUnit;

        const double stepMm = 0.3;
        var stepWpf = stepMm / mmPerWpfUnit;
        var cutouts = new List<(double X0, double X1, double Y0, double Y1)>();
        for (var gx = boundsWpf.Left; gx < boundsWpf.Right; gx += stepWpf)
        {
            for (var gy = boundsWpf.Top; gy < boundsWpf.Bottom; gy += stepWpf)
            {
                if (!geometry.FillContains(new Point(gx + stepWpf / 2, gy + stepWpf / 2))) continue;

                // WPF text space is X-right/Y-down; tray space is X-right/Y-up - flip Y and
                // center both axes on (centerX, centerY).
                var trayX0 = centerX - finalWidthMm / 2 + (gx - boundsWpf.Left) * mmPerWpfUnit;
                var trayY1 = centerY + finalHeightMm / 2 - (gy - boundsWpf.Top) * mmPerWpfUnit;
                cutouts.Add((trayX0, trayX0 + stepMm, trayY1 - stepMm, trayY1));
            }
        }

        return cutouts;
    }

    /// <summary>Fills the given box with solid material except wherever it overlaps one of
    /// <paramref name="cutoutsXy"/> (XY rectangles, this band's full Z extent) - via a simple grid
    /// decomposition (every distinct X/Y boundary from the bounding box and every cutout forms a
    /// grid; each grid cell becomes its own solid box unless its center falls inside a cutout).
    /// Deliberately not merging adjacent solid cells into larger boxes - more triangles than
    /// strictly necessary, but far simpler and just as correct, and tray-sized meshes are cheap
    /// either way (even with a few hundred small cutouts from a rasterized text label).</summary>
    private static void AddSolidWithCutouts(List<StlTriangle> triangles, double x0, double x1, double y0, double y1, double z0, double z1,
        IReadOnlyList<(double X0, double X1, double Y0, double Y1)> cutoutsXy)
    {
        if (z1 <= z0) return;
        if (cutoutsXy.Count == 0)
        {
            AddBox(triangles, x0, x1, y0, y1, z0, z1);
            return;
        }

        var xs = new SortedSet<double> { x0, x1 };
        var ys = new SortedSet<double> { y0, y1 };
        foreach (var c in cutoutsXy)
        {
            xs.Add(Math.Clamp(c.X0, x0, x1));
            xs.Add(Math.Clamp(c.X1, x0, x1));
            ys.Add(Math.Clamp(c.Y0, y0, y1));
            ys.Add(Math.Clamp(c.Y1, y0, y1));
        }

        var xList = xs.ToList();
        var yList = ys.ToList();
        for (var i = 0; i < xList.Count - 1; i++)
        {
            var cx0 = xList[i];
            var cx1 = xList[i + 1];
            if (cx1 - cx0 < 1e-9) continue;

            for (var j = 0; j < yList.Count - 1; j++)
            {
                var cy0 = yList[j];
                var cy1 = yList[j + 1];
                if (cy1 - cy0 < 1e-9) continue;

                var midX = (cx0 + cx1) / 2;
                var midY = (cy0 + cy1) / 2;
                var isCutout = cutoutsXy.Any(c => midX > c.X0 && midX < c.X1 && midY > c.Y0 && midY < c.Y1);
                if (!isCutout) AddBox(triangles, cx0, cx1, cy0, cy1, z0, z1);
            }
        }
    }

    /// <summary>Appends 12 triangles (2 per face) for an axis-aligned box, with outward-facing
    /// normals and consistent CCW winding per face (verified via the right-hand rule against every
    /// triangle's own vertices by the scratchpad smoke test, not just spot-checked by hand).</summary>
    private static void AddBox(List<StlTriangle> triangles, double x0, double x1, double y0, double y1, double z0, double z1)
    {
        var p000 = new Point3D(x0, y0, z0);
        var p100 = new Point3D(x1, y0, z0);
        var p010 = new Point3D(x0, y1, z0);
        var p110 = new Point3D(x1, y1, z0);
        var p001 = new Point3D(x0, y0, z1);
        var p101 = new Point3D(x1, y0, z1);
        var p011 = new Point3D(x0, y1, z1);
        var p111 = new Point3D(x1, y1, z1);

        void Quad(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D normal)
        {
            triangles.Add(new StlTriangle(a, b, c, normal));
            triangles.Add(new StlTriangle(a, c, d, normal));
        }

        Quad(p000, p010, p110, p100, new Vector3D(0, 0, -1)); // bottom
        Quad(p001, p101, p111, p011, new Vector3D(0, 0, 1));  // top
        Quad(p000, p100, p101, p001, new Vector3D(0, -1, 0)); // front
        Quad(p010, p011, p111, p110, new Vector3D(0, 1, 0));  // back
        Quad(p000, p001, p011, p010, new Vector3D(-1, 0, 0)); // left
        Quad(p100, p110, p111, p101, new Vector3D(1, 0, 0));  // right
    }
}
