using System.IO;
using System.Text.RegularExpressions;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Import;

public sealed partial class LayerClassifier
{
    public void ClassifyAll(IEnumerable<ExtractedFile> files)
    {
        foreach (var file in files)
        {
            var (role, reason) = Classify(file);
            file.DetectedRole = role;
            file.DetectionReason = reason;
        }
    }

    private (GerberLayerRole Role, string? Reason) Classify(ExtractedFile file)
    {
        var attributeResult = TryClassifyByFileFunctionAttribute(file.FullPath);
        if (attributeResult is not null)
            return attributeResult.Value;

        return ClassifyByFileName(file.FileName);
    }

    private (GerberLayerRole, string?)? TryClassifyByFileFunctionAttribute(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            for (var i = 0; i < 80 && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;

                var pasteMatch = PasteFileFunctionRegex().Match(line);
                if (pasteMatch.Success)
                {
                    var side = pasteMatch.Groups["side"].Value;
                    if (side.StartsWith("Top", StringComparison.OrdinalIgnoreCase))
                        return (GerberLayerRole.TopPaste, "Gerber X2 FileFunction attribute (Paste/Top)");
                    if (side.StartsWith("Bot", StringComparison.OrdinalIgnoreCase))
                        return (GerberLayerRole.BottomPaste, "Gerber X2 FileFunction attribute (Paste/Bot)");
                }

                var maskMatch = SoldermaskFileFunctionRegex().Match(line);
                if (maskMatch.Success)
                {
                    var side = maskMatch.Groups["side"].Value;
                    if (side.StartsWith("Top", StringComparison.OrdinalIgnoreCase))
                        return (GerberLayerRole.TopSoldermask, "Gerber X2 FileFunction attribute (Soldermask/Top)");
                    if (side.StartsWith("Bot", StringComparison.OrdinalIgnoreCase))
                        return (GerberLayerRole.BottomSoldermask, "Gerber X2 FileFunction attribute (Soldermask/Bot)");
                }

                if (ProfileFileFunctionRegex().IsMatch(line))
                    return (GerberLayerRole.Outline, "Gerber X2 FileFunction attribute (Profile)");
            }
        }
        catch (IOException)
        {
            // Not readable as text (e.g. a binary/drill file) - fall through to filename heuristics.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static (GerberLayerRole, string?) ClassifyByFileName(string fileName)
    {
        if (Contains(fileName, "F_Paste") || HasExtension(fileName, ".gtp") || HasExtension(fileName, ".crc"))
            return (GerberLayerRole.TopPaste, "Filename convention (top paste)");

        if (Contains(fileName, "B_Paste") || HasExtension(fileName, ".gbp") || HasExtension(fileName, ".crs"))
            return (GerberLayerRole.BottomPaste, "Filename convention (bottom paste)");

        if (Contains(fileName, "F_Mask") || HasExtension(fileName, ".gts") || HasExtension(fileName, ".stc"))
            return (GerberLayerRole.TopSoldermask, "Filename convention (top soldermask)");

        if (Contains(fileName, "B_Mask") || HasExtension(fileName, ".gbs") || HasExtension(fileName, ".sts"))
            return (GerberLayerRole.BottomSoldermask, "Filename convention (bottom soldermask)");

        if (Contains(fileName, "Edge_Cuts") || HasExtension(fileName, ".gko") || HasExtension(fileName, ".gm1") || HasExtension(fileName, ".mil"))
            return (GerberLayerRole.Outline, "Filename convention (outline)");

        return (GerberLayerRole.Unknown, null);
    }

    private static bool Contains(string fileName, string token) =>
        fileName.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool HasExtension(string fileName, string extension) =>
        fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"%TF\.FileFunction,Paste,(?<side>Top|Bot)", RegexOptions.IgnoreCase)]
    private static partial Regex PasteFileFunctionRegex();

    [GeneratedRegex(@"%TF\.FileFunction,Soldermask,(?<side>Top|Bot)", RegexOptions.IgnoreCase)]
    private static partial Regex SoldermaskFileFunctionRegex();

    [GeneratedRegex(@"%TF\.FileFunction,Profile", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileFileFunctionRegex();
}
