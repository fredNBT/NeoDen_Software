using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Gerber;

/// <summary>
/// Parses the subset of RS-274X (Gerber) needed to render copper and outline layers:
/// format/unit directives, circle/rectangle/obround apertures, linear and circular
/// interpolation, and filled regions. Aperture macros and incremental (G91) coordinates
/// are not supported and are skipped gracefully rather than throwing.
/// </summary>
public sealed partial class GerberParser
{
    private const double DefaultApertureSizeMm = 0.15;

    public ParsedLayer Parse(string filePath, GerberLayerRole role)
    {
        var text = File.ReadAllText(filePath);
        var state = new ParserState();
        var primitives = new List<GerberPrimitive>();
        var bounds = BoundingBox.Empty;
        List<PointMm>? regionContour = null;

        void Include(PointMm p) => bounds = bounds.Include(p);

        foreach (var statement in ReadStatements(text))
        {
            if (statement.Length == 0) continue;

            if (statement[0] == '%')
            {
                ApplyExtendedCommand(statement[1..], state);
            }
            else
            {
                ProcessWordCommand(statement, state, primitives, ref regionContour, Include);
            }
        }

        var finalBounds = bounds.MaxX >= bounds.MinX ? bounds : new BoundingBox(0, 0, 0, 0);
        return new ParsedLayer { Role = role, Primitives = primitives, Bounds = finalBounds };
    }

    private static IEnumerable<string> ReadStatements(string text)
    {
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n) yield break;

            if (text[i] == '%')
            {
                var start = i + 1;
                var end = text.IndexOf('%', start);
                if (end < 0) yield break;
                var body = text[start..end].TrimEnd('*', ' ', '\r', '\n', '\t');
                yield return "%" + body;
                i = end + 1;
            }
            else
            {
                var end = text.IndexOf('*', i);
                if (end < 0) yield break;
                var body = text[i..end].Trim();
                if (body.Length > 0) yield return body;
                i = end + 1;
            }
        }
    }

    private static void ApplyExtendedCommand(string body, ParserState state)
    {
        if (body.StartsWith("FS", StringComparison.Ordinal))
            ParseFormatSpec(body, state);
        else if (body.StartsWith("MO", StringComparison.Ordinal))
            state.UnitToMm = body.Contains("IN", StringComparison.OrdinalIgnoreCase) ? 25.4 : 1.0;
        else if (body.StartsWith("AD", StringComparison.Ordinal))
            ParseApertureDefinition(body, state);
        else if (body.StartsWith("LP", StringComparison.Ordinal))
            state.ClearPolarity = body.Length > 2 && char.ToUpperInvariant(body[2]) == 'C';
        // AM (aperture macros) and TF/TO/TD (attributes) are intentionally not interpreted further.
    }

    private static void ParseFormatSpec(string body, ParserState state)
    {
        var idx = 2; // past "FS"
        if (idx < body.Length && (body[idx] == 'L' || body[idx] == 'T'))
        {
            state.Format.TrailingZeroSuppression = body[idx] == 'T';
            idx++;
        }
        if (idx < body.Length && (body[idx] == 'A' || body[idx] == 'I'))
            idx++; // absolute/incremental - only absolute (A) coordinates are supported

        var xIndex = body.IndexOf('X', idx);
        var yIndex = body.IndexOf('Y', idx);
        if (xIndex >= 0 && yIndex > xIndex && yIndex + 2 < body.Length)
        {
            state.Format.XIntegerDigits = body[xIndex + 1] - '0';
            state.Format.XDecimalDigits = body[xIndex + 2] - '0';
            state.Format.YIntegerDigits = body[yIndex + 1] - '0';
            state.Format.YDecimalDigits = body[yIndex + 2] - '0';
        }
    }

    private static void ParseApertureDefinition(string body, ParserState state)
    {
        var match = ApertureDefRegex().Match(body);
        if (!match.Success) return;

        var code = int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture);
        var shapeToken = match.Groups["shape"].Value;
        var paramsToken = match.Groups["params"].Success ? match.Groups["params"].Value : string.Empty;

        var parameters = paramsToken.Length == 0
            ? []
            : paramsToken.Split('X', StringSplitOptions.TrimEntries)
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0)
                .ToArray();

        // Only a real C(ircle)/R(ectangle)/O(bround) aperture's own parameters are actually a
        // size - a macro-referencing aperture (shapeToken is a macro NAME, e.g. "RoundRect" or
        // "FreePoly0") falls back to a circular pad, but its parameters are whatever that specific
        // macro defines them to mean, which is NOT reliably "a diameter". Real-world repro that
        // motivated this: KiCad emits "%ADD41FreePoly0,270.000000*%" for a free-polygon aperture,
        // where 270 is a ROTATION ANGLE IN DEGREES, not a size - trusting parameters[0] here
        // produced a fake 270mm-diameter pad, ballooning that layer's bounds by 100+mm and, since
        // the whole board's Gerber-to-PnP alignment is derived from the union of every layer's
        // bounds, silently shifting every placed component off the board. Fall back to the same
        // small default used when an aperture has no parameters at all - "a Gerber file's
        // vertex/segment shapes are correct even for unsupported macros, only the exact pad
        // silhouette they draw isn't" is an acceptable simplification here; a wildly wrong bound
        // that corrupts the whole board's coordinate alignment is not.
        var isKnownShape = shapeToken.ToUpperInvariant() is "C" or "R" or "O";
        var widthMm = isKnownShape && parameters.Length > 0 ? parameters[0] * state.UnitToMm : DefaultApertureSizeMm;
        var heightMm = isKnownShape && parameters.Length > 1 ? parameters[1] * state.UnitToMm : widthMm;

        var shape = shapeToken.ToUpperInvariant() switch
        {
            "C" => ApertureShape.Circle,
            "R" => ApertureShape.Rectangle,
            "O" => ApertureShape.Obround,
            _ => ApertureShape.Circle, // aperture macros and unrecognized shapes fall back to a circular pad
        };

        state.Apertures[code] = new ApertureDefinition(shape, widthMm, heightMm);
    }

    private static void ProcessWordCommand(
        string word,
        ParserState state,
        List<GerberPrimitive> primitives,
        ref List<PointMm>? regionContour,
        Action<PointMm> include)
    {
        if (word.Length == 0 || word[0] == 'M') return; // M00/M01/M02 - program stop/end, nothing to draw

        var match = WordCommandRegex().Match(word);
        if (!match.Success) return; // unrecognized/unsupported command - skip gracefully

        if (match.Groups["g"].Success)
            ApplyGCode(int.Parse(match.Groups["g"].Value, CultureInfo.InvariantCulture), state, primitives, ref regionContour, include);

        var hasX = match.Groups["x"].Success;
        var hasY = match.Groups["y"].Success;
        var hasI = match.Groups["i"].Success;
        var hasJ = match.Groups["j"].Success;

        var x = hasX ? state.Format.ParseCoordinate(match.Groups["x"].Value, state.Format.XIntegerDigits, state.Format.XDecimalDigits) * state.UnitToMm : state.CurrentPoint.X;
        var y = hasY ? state.Format.ParseCoordinate(match.Groups["y"].Value, state.Format.YIntegerDigits, state.Format.YDecimalDigits) * state.UnitToMm : state.CurrentPoint.Y;
        var iOffset = hasI ? state.Format.ParseCoordinate(match.Groups["i"].Value, state.Format.XIntegerDigits, state.Format.XDecimalDigits) * state.UnitToMm : 0.0;
        var jOffset = hasJ ? state.Format.ParseCoordinate(match.Groups["j"].Value, state.Format.YIntegerDigits, state.Format.YDecimalDigits) * state.UnitToMm : 0.0;

        var target = new PointMm(x, y);

        if (!match.Groups["d"].Success) return;

        var d = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        if (d >= 10)
        {
            state.CurrentAperture = state.Apertures.GetValueOrDefault(d);
            return;
        }

        switch (d)
        {
            case 1:
                ApplyDraw(state, primitives, ref regionContour, include, target, iOffset, jOffset);
                break;
            case 2:
                state.CurrentPoint = target;
                if (state.RegionMode)
                {
                    regionContour ??= [];
                    regionContour.Add(target);
                }
                break;
            case 3:
                ApplyFlash(state, primitives, include, target);
                state.CurrentPoint = target;
                break;
        }
    }

    private static void ApplyGCode(
        int g,
        ParserState state,
        List<GerberPrimitive> primitives,
        ref List<PointMm>? regionContour,
        Action<PointMm> include)
    {
        switch (g)
        {
            case 1: state.Interpolation = InterpolationMode.Linear; break;
            case 2: state.Interpolation = InterpolationMode.ClockwiseArc; break;
            case 3: state.Interpolation = InterpolationMode.CounterclockwiseArc; break;
            case 36:
                state.RegionMode = true;
                regionContour = [];
                break;
            case 37:
                state.RegionMode = false;
                if (regionContour is { Count: >= 3 })
                {
                    primitives.Add(new RegionPrimitive(regionContour, state.ClearPolarity));
                    foreach (var p in regionContour) include(p);
                }
                regionContour = null;
                break;
            case 74: state.MultiQuadrant = false; break;
            case 75: state.MultiQuadrant = true; break;
            // G70/G71 (deprecated unit codes) and G90/G91 (absolute/incremental) are not handled;
            // absolute coordinates in the %MO%-declared unit are assumed throughout.
        }
    }

    private static void ApplyDraw(
        ParserState state,
        List<GerberPrimitive> primitives,
        ref List<PointMm>? regionContour,
        Action<PointMm> include,
        PointMm target,
        double iOffset,
        double jOffset)
    {
        if (state.RegionMode)
        {
            regionContour ??= [state.CurrentPoint];
            if (regionContour.Count == 0) regionContour.Add(state.CurrentPoint);

            if (state.Interpolation == InterpolationMode.Linear)
                regionContour.Add(target);
            else
                AppendArcPoints(regionContour, state.CurrentPoint, target, iOffset, jOffset, state.Interpolation == InterpolationMode.ClockwiseArc);
        }
        else if (state.Interpolation == InterpolationMode.Linear)
        {
            primitives.Add(new SegmentPrimitive(state.CurrentPoint, target, CurrentApertureWidth(state)));
            include(state.CurrentPoint);
            include(target);
        }
        else
        {
            var center = new PointMm(state.CurrentPoint.X + iOffset, state.CurrentPoint.Y + jOffset);
            primitives.Add(new ArcPrimitive(state.CurrentPoint, target, center, state.Interpolation == InterpolationMode.ClockwiseArc, CurrentApertureWidth(state)));
            include(state.CurrentPoint);
            include(target);
        }

        state.CurrentPoint = target;
    }

    private static void ApplyFlash(ParserState state, List<GerberPrimitive> primitives, Action<PointMm> include, PointMm target)
    {
        var aperture = state.CurrentAperture;
        if (aperture is null) return;

        primitives.Add(new FlashPrimitive(target, aperture.Shape, aperture.WidthMm, aperture.HeightMm));
        include(new PointMm(target.X - aperture.WidthMm / 2, target.Y - aperture.HeightMm / 2));
        include(new PointMm(target.X + aperture.WidthMm / 2, target.Y + aperture.HeightMm / 2));
    }

    private static double CurrentApertureWidth(ParserState state) => state.CurrentAperture?.WidthMm ?? DefaultApertureSizeMm;

    private static void AppendArcPoints(List<PointMm> contour, PointMm start, PointMm end, double iOffset, double jOffset, bool clockwise)
    {
        var center = new PointMm(start.X + iOffset, start.Y + jOffset);
        var radius = Math.Sqrt(Math.Pow(start.X - center.X, 2) + Math.Pow(start.Y - center.Y, 2));
        if (radius <= 0)
        {
            contour.Add(end);
            return;
        }

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var sweep = endAngle - startAngle;
        if (clockwise)
        {
            while (sweep > 0) sweep -= 2 * Math.PI;
        }
        else
        {
            while (sweep < 0) sweep += 2 * Math.PI;
        }

        const int stepsPerFullCircle = 90;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * stepsPerFullCircle));
        for (var s = 1; s <= steps; s++)
        {
            var angle = startAngle + sweep * s / steps;
            contour.Add(new PointMm(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }
    }

    private sealed class ParserState
    {
        public CoordinateFormat Format { get; } = new();
        public double UnitToMm { get; set; } = 25.4; // Gerber default unit is inches until %MO% says otherwise
        public Dictionary<int, ApertureDefinition> Apertures { get; } = new();
        public ApertureDefinition? CurrentAperture { get; set; }
        public InterpolationMode Interpolation { get; set; } = InterpolationMode.Linear;
        public bool RegionMode { get; set; }
        public bool MultiQuadrant { get; set; } = true;
        public bool ClearPolarity { get; set; }
        public PointMm CurrentPoint { get; set; }
    }

    [GeneratedRegex(@"^(?:G(?<g>\d{1,2}))?(?:X(?<x>[+-]?\d+))?(?:Y(?<y>[+-]?\d+))?(?:I(?<i>[+-]?\d+))?(?:J(?<j>[+-]?\d+))?(?:D(?<d>\d+))?$")]
    private static partial Regex WordCommandRegex();

    [GeneratedRegex(@"^ADD(?<code>\d+)(?<shape>[^,]+)(,(?<params>.*))?$")]
    private static partial Regex ApertureDefRegex();
}
