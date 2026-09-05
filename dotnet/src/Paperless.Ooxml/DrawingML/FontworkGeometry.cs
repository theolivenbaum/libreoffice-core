using System.Globalization;

namespace Paperless.Ooxml.DrawingML;

/// <summary>A point in a Fontwork's own coordinate space, in hundredths of a millimetre.</summary>
/// <remarks>
/// Doubles rather than <c>Length</c>, and hundredths of a millimetre rather than EMUs, because this
/// is arithmetic reproduced from LibreOffice and it has to round where LibreOffice rounds. The draw
/// layer works in 1/100 mm and truncates a computed coordinate to an integer of that unit
/// (<c>EnhancedCustomShape2d::GetPoint</c>), and the number of points an arc is approximated by
/// depends on its radius <em>in that unit</em>
/// (<c>tools/source/generic/poly.cxx:260-266</c>) — so an EMU pipeline would subdivide the same arc
/// into twice as many segments and parameterise it slightly differently.
/// </remarks>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position, growing downward.</param>
public readonly record struct FontworkPoint(double X, double Y);

/// <summary>
/// Turns a <see cref="FontworkPreset"/> into the polylines a warp is fitted to.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>EnhancedCustomShape2d</c>'s path construction reduced to what a Fontwork shape needs:
/// the formula evaluator of <c>GetEquation</c>
/// (<c>svx/source/customshapes/EnhancedCustomShape2d.cxx:88-330</c>), the four path opcodes the
/// WordArt tables use, and <c>CreateArc</c> (<c>:1996</c>) with the polygon arc of
/// <c>tools/source/generic/poly.cxx:245-330</c> behind it.
/// </para>
/// <para>
/// The result is always polylines, never curves, because that is what the fitting consumes:
/// <c>GetOutlinesFromShape2d</c> runs <c>adaptiveSubdivideByAngle</c> over anything with control
/// points before <c>CalcDistances</c> measures it, and the whole fit is arc-length along those
/// segments.
/// </para>
/// </remarks>
public static class FontworkGeometry
{
    /// <summary>The square viewbox every WordArt table is written in.</summary>
    private const double CoordinateSize = 21600.0;

    /// <summary>
    /// The polylines a preset's geometry makes at the given size, in shape coordinates.
    /// </summary>
    /// <param name="preset">The preset, from <see cref="FontworkPresets.Find"/>.</param>
    /// <param name="adjustments">
    /// Its adjustment values, already in WordArt units. Shorter than the preset's own default list
    /// is fine — the defaults fill the rest, exactly as <c>SdrObjCustomShape::MergeDefaultAttributes</c>
    /// does (<c>svx/source/svdraw/svdoashp.cxx:855-877</c>).
    /// </param>
    /// <param name="width">The shape's width, in hundredths of a millimetre.</param>
    /// <param name="height">The shape's height, in hundredths of a millimetre.</param>
    /// <returns>One polyline per subpath, in the order the segment programme names them.</returns>
    public static IReadOnlyList<IReadOnlyList<FontworkPoint>> Outlines(
        FontworkPreset preset, IReadOnlyList<double>? adjustments, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(preset);

        double[] values = Adjustments(preset, adjustments);
        Evaluator formulae = new(preset.Calculations, values);

        double xScale = width / CoordinateSize;
        double yScale = height / CoordinateSize;

        List<IReadOnlyList<FontworkPoint>> outlines = [];
        List<FontworkPoint> current = [];
        int coordinates = preset.Vertices.Count / 2;
        int vertex = 0;

        void Flush()
        {
            if (current.Count > 1) outlines.Add(current);
            current = [];
        }

        FontworkPoint At(int index)
        {
            int offset = index * 2;
            if (offset + 1 >= preset.Vertices.Count) return default;

            // Truncated rather than rounded: the draw layer casts the scaled double to a long.
            double x = Math.Truncate(formulae.Resolve(preset.Vertices[offset]) * xScale);
            double y = Math.Truncate(formulae.Resolve(preset.Vertices[offset + 1]) * yScale);
            return new FontworkPoint(x, y);
        }

        for (int i = 0; i < preset.Segments.Count; i++)
        {
            int word = preset.Segments[i];
            int op = (word >> 8) & 0xFF;
            int count = word & 0xFF;

            switch (op)
            {
                case 0x00:                                          // lineTo
                    for (int n = 0; n < Math.Max(count, 1) && vertex < coordinates; n++)
                    {
                        current.Add(At(vertex++));
                    }

                    break;

                case 0x20:                                          // curveTo
                    for (int n = 0; n < Math.Max(count, 1) && vertex + 2 < coordinates; n++)
                    {
                        FontworkPoint from = current.Count > 0 ? current[^1] : At(vertex);
                        FontworkPoint c1 = At(vertex++);
                        FontworkPoint c2 = At(vertex++);
                        FontworkPoint to = At(vertex++);
                        FontworkFlattening.Append(from, c1, c2, to, current);
                    }

                    break;

                case 0x40:                                          // moveTo
                    // One point however many the opcode's count claims, which is what the
                    // reference consumes (`EnhancedCustomShape2d.cxx:2122-2143`).
                    Flush();
                    if (vertex < coordinates) current.Add(At(vertex++));
                    break;

                case 0x60:                                          // closeSubPath
                    if (current.Count > 1) current.Add(current[0]);
                    Flush();
                    break;

                case 0x80:                                          // endSubPath
                    Flush();
                    break;

                case 0xA3:                                          // arcTo
                case 0xA4:                                          // arc
                case 0xA5:                                          // clockwiseArcTo
                case 0xA6:                                          // clockwiseArc
                {
                    bool clockwise = op is 0xA5 or 0xA6;
                    bool implicitMove = op is 0xA4 or 0xA6;
                    int arcs = count >> 2;

                    for (int n = 0; n < arcs && vertex + 3 < coordinates; n++)
                    {
                        if (implicitMove) Flush();

                        int swap = clockwise ? 3 : 2;
                        FontworkPoint corner1 = At(vertex);
                        FontworkPoint corner2 = At(vertex + 1);
                        FontworkPoint start = At(vertex + swap);
                        FontworkPoint end = At(vertex + (swap ^ 1));
                        vertex += 4;

                        Arc(corner1, corner2, start, end, clockwise, current);
                    }

                    break;
                }

                default:
                    break;
            }
        }

        Flush();
        return outlines;
    }

    /// <summary>The adjustment values to evaluate with: the document's, over the preset's.</summary>
    private static double[] Adjustments(FontworkPreset preset, IReadOnlyList<double>? stated)
    {
        double[] values = new double[preset.Defaults.Count];
        for (int i = 0; i < values.Length; i++) values[i] = preset.Defaults[i];

        if (stated is null) return values;

        for (int i = 0; i < stated.Count && i < values.Length; i++) values[i] = stated[i];
        return values;
    }

    /// <summary>
    /// Appends an elliptical arc, as <c>CreateArc</c> builds one.
    /// </summary>
    /// <remarks>
    /// The two corners bound the ellipse; start and end name where on it the arc runs between. A
    /// clockwise arc is generated the other way round and then reversed, which is what
    /// <c>EnhancedCustomShape2d.cxx:1996-2037</c> does and what makes its first point the one the
    /// vertex list names first.
    /// </remarks>
    private static void Arc(
        FontworkPoint corner1,
        FontworkPoint corner2,
        FontworkPoint start,
        FontworkPoint end,
        bool clockwise,
        List<FontworkPoint> into)
    {
        // The bounding rectangle arrives already normalised — the reference builds it with
        // `tools::Rectangle::Normalize` — which is why `CreateArc`'s own start/end swap, which
        // triggers only on an un-normalised one, is not reproduced here.
        double left = Math.Min(corner1.X, corner2.X);
        double right = Math.Max(corner1.X, corner2.X);
        double top = Math.Min(corner1.Y, corner2.Y);
        double bottom = Math.Max(corner1.Y, corner2.Y);

        // VCL's rectangle is inclusive of both edges, so its extent is one unit more than the span.
        if (right - left + 1 <= 0 || bottom - top + 1 <= 0) return;

        double centreX = Math.Floor((left + right) / 2);
        double centreY = Math.Floor((top + bottom) / 2);
        double radiusX = centreX - left;
        double radiusY = centreY - top;
        if (radiusX <= 0 || radiusY <= 0) return;

        int points = (int)Math.Clamp(
            Math.PI * ((1.5 * (radiusX + radiusY)) - Math.Sqrt(Math.Abs(radiusX * radiusY))),
            32.0,
            256.0);

        if (radiusX > 32 && radiusY > 32 && radiusX + radiusY < 8192) points >>= 1;

        double from = Parameter(centreX, centreY, start, radiusX, radiusY);
        double to = Parameter(centreX, centreY, end, radiusX, radiusY);
        double span = to - from;
        if (span <= 0) span += 2 * Math.PI;

        points = Math.Max((int)(span / (2 * Math.PI) * points), 16);
        double step = span / (points - 1);

        FontworkPoint[] arc = new FontworkPoint[points];
        double angle = from;
        for (int i = 0; i < points; i++, angle += step)
        {
            arc[i] = new FontworkPoint(
                Math.Round(centreX + (radiusX * Math.Cos(angle)), MidpointRounding.AwayFromZero),
                Math.Round(centreY - (radiusY * Math.Sin(angle)), MidpointRounding.AwayFromZero));
        }

        if (clockwise) Array.Reverse(arc);

        into.AddRange(arc);
    }

    /// <summary>The ellipse parameter of a point, as <c>ImplGetParameter</c> computes it.</summary>
    private static double Parameter(
        double centreX, double centreY, FontworkPoint point, double radiusX, double radiusY)
    {
        double angle = Math.Atan2(centreY - point.Y, point.X - centreX);
        return Math.Atan2(radiusX * Math.Sin(angle), radiusY * Math.Cos(angle));
    }

    /// <summary>Evaluates a preset's formula table, once per formula.</summary>
    private sealed class Evaluator(IReadOnlyList<FontworkFormula> formulae, double[] adjustments)
    {
        private readonly double[] _values = new double[formulae.Count];
        private readonly bool[] _known = new bool[formulae.Count];

        /// <summary>A vertex coordinate: a formula's value, or the number it already is.</summary>
        public double Resolve(int coordinate)
            => (coordinate & FontworkPresets.FormulaFlag) != 0
                ? Value(coordinate & ~FontworkPresets.FormulaFlag)
                : coordinate;

        /// <summary>Formula n's value.</summary>
        private double Value(int index)
        {
            if (index < 0 || index >= _values.Length) return 0;
            if (_known[index]) return _values[index];

            // Marked known before evaluating so that a table referring to itself answers zero
            // rather than recursing forever. No shipped table does; a malformed one could.
            _known[index] = true;

            FontworkFormula formula = formulae[index];
            double p1 = Parameter(formula.P1, (formula.Flags & 0x2000) != 0);
            double p2 = Parameter(formula.P2, (formula.Flags & 0x4000) != 0);
            double p3 = Parameter(formula.P3, (formula.Flags & 0x8000) != 0);

            _values[index] = Apply(formula.Flags & 0xFF, p1, p2, p3);
            return _values[index];
        }

        /// <summary>One parameter: a number, or a reference to a formula or an adjustment.</summary>
        private double Parameter(int parameter, bool isReference)
        {
            if (!isReference) return parameter;
            if ((parameter & 0x400) != 0) return Value(parameter & 0xFF);

            int adjustment = parameter - FontworkPresets.FirstAdjustmentProperty;
            return adjustment >= 0 && adjustment < adjustments.Length ? adjustments[adjustment] : 0;
        }

        /// <summary>
        /// The operations <c>GetEquation</c> writes, by the low byte that selects them.
        /// </summary>
        /// <remarks>
        /// The WordArt tables use seven of these; the rest are here because the switch is the
        /// specification and a half-transcribed one is a trap for whoever adds the next preset.
        /// </remarks>
        private static double Apply(int operation, double p1, double p2, double p3) => operation switch
        {
            0 or 14 => p1 + p2 - p3,
            1 => p3 == 0 ? p1 * p2 : p1 * p2 / p3,
            2 => (p1 + p2) / 2,
            3 => Math.Abs(p1),
            4 => Math.Min(p1, p2),
            5 => Math.Max(p1, p2),
            6 => p1 > 0 ? p2 : p3,
            7 => Math.Sqrt((p1 * p1) + (p2 * p2) + (p3 * p3)),
            8 => Math.Atan2(p2, p1) * 180.0 / Math.PI,
            9 => p1 * Math.Sin(p2 * Math.PI / 180.0),
            10 => p1 * Math.Cos(p2 * Math.PI / 180.0),
            11 => p1 * Math.Cos(Math.Atan2(p3, p2)),
            12 => p1 * Math.Sin(Math.Atan2(p3, p2)),
            13 => Math.Sqrt(p1),
            15 => p2 == 0 ? 0 : p3 * Math.Sqrt(1 - (p1 / p2 * (p1 / p2))),
            16 => p1 * Math.Tan(p2),
            0x80 => Math.Sqrt((p3 * p3) - (p1 * p1)),
            _ => 0,
        };

        /// <summary>A description of the table, for diagnostics.</summary>
        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"{formulae.Count} formulae");
    }
}
