using System.Globalization;

namespace NeoDenSoftware.Gerber;

public sealed class CoordinateFormat
{
    public int XIntegerDigits { get; set; } = 3;
    public int XDecimalDigits { get; set; } = 4;
    public int YIntegerDigits { get; set; } = 3;
    public int YDecimalDigits { get; set; } = 4;
    public bool TrailingZeroSuppression { get; set; }

    public double ParseCoordinate(string raw, int integerDigits, int decimalDigits)
    {
        var negative = false;
        if (raw.Length > 0 && (raw[0] == '+' || raw[0] == '-'))
        {
            negative = raw[0] == '-';
            raw = raw[1..];
        }

        var totalDigits = integerDigits + decimalDigits;
        var padded = TrailingZeroSuppression
            ? raw.PadRight(totalDigits, '0')
            : raw.PadLeft(totalDigits, '0');

        if (padded.Length > totalDigits)
            padded = padded[^totalDigits..];

        var integerPart = padded[..integerDigits];
        var decimalPart = padded[integerDigits..];
        var value = double.Parse(
            (integerPart.Length == 0 ? "0" : integerPart) + "." + (decimalPart.Length == 0 ? "0" : decimalPart),
            CultureInfo.InvariantCulture);

        return negative ? -value : value;
    }
}
