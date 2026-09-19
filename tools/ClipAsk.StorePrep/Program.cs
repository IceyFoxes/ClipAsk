using System;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
            {
                GenerateAssets(args[1], args[2]);
                return 0;
            }
            if (args.Length == 7 && args[0].Equals("manifest", StringComparison.OrdinalIgnoreCase))
            {
                GenerateManifest(args[1], args[2], args[3], args[4], args[5], args[6]);
                return 0;
            }

            Console.Error.WriteLine("usage: ClipAsk.StorePrep assets <source.png> <output-dir>");
            Console.Error.WriteLine("   or: ClipAsk.StorePrep manifest <template> <output> <identity-name> <publisher> <publisher-display-name> <version>");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void GenerateAssets(string sourcePath, string outputDirectory)
    {
        var source = LoadBitmap(sourcePath);
        Directory.CreateDirectory(outputDirectory);
        SaveAsset(source, Path.Combine(outputDirectory, "StoreLogo.png"), 50, 50, 50);
        SaveAsset(source, Path.Combine(outputDirectory, "Square44x44Logo.png"), 44, 44, 44);
        SaveAsset(source, Path.Combine(outputDirectory, "Square71x71Logo.png"), 71, 71, 71);
        SaveAsset(source, Path.Combine(outputDirectory, "Square150x150Logo.png"), 150, 150, 150);
        SaveAsset(source, Path.Combine(outputDirectory, "Wide310x150Logo.png"), 310, 150, 132);
        SaveAsset(source, Path.Combine(outputDirectory, "Square310x310Logo.png"), 310, 310, 310);
        Console.WriteLine($"Generated Microsoft Store assets in {Path.GetFullPath(outputDirectory)}");
    }

    private static BitmapSource LoadBitmap(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Brand source was not found.", path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void SaveAsset(BitmapSource source, string path, int width, int height, int iconSize)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var left = (width - iconSize) / 2d;
            var top = (height - iconSize) / 2d;
            drawing.DrawImage(source, new Rect(left, top, iconSize, iconSize));
        }
        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        render.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void GenerateManifest(
        string templatePath,
        string outputPath,
        string identityName,
        string publisher,
        string publisherDisplayName,
        string version)
    {
        if (!IdentityNamePattern().IsMatch(identityName))
            throw new ArgumentException("Identity name must exactly match the Partner Center value and contain only letters, digits, periods, or hyphens.");
        ValidatePackageVersion(version);
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Manifest template was not found.", templatePath);

        var manifest = File.ReadAllText(templatePath)
            .Replace("__PACKAGE_IDENTITY_NAME__", Escape(identityName), StringComparison.Ordinal)
            .Replace("__PUBLISHER__", Escape(publisher), StringComparison.Ordinal)
            .Replace("__PUBLISHER_DISPLAY_NAME__", Escape(publisherDisplayName), StringComparison.Ordinal)
            .Replace("__PACKAGE_VERSION__", version, StringComparison.Ordinal);
        _ = XDocument.Parse(manifest, LoadOptions.PreserveWhitespace);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, manifest, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"Generated {Path.GetFullPath(outputPath)}");
    }

    private static void ValidatePackageVersion(string version)
    {
        if (!VersionPattern().IsMatch(version))
            throw new ArgumentException("Package version must have four numeric components, start at 1, and reserve the fourth component as 0.");

        foreach (var component in version.Split('.'))
        {
            if (!ushort.TryParse(component, out _))
                throw new ArgumentException("Each package version component must be between 0 and 65535.");
        }
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Manifest identity values cannot be empty.");
        return SecurityElement.Escape(value) ?? throw new InvalidOperationException("Manifest identity value could not be escaped.");
    }

    [GeneratedRegex("^[A-Za-z0-9.-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityNamePattern();

    [GeneratedRegex("^[1-9][0-9]{0,4}\\.[0-9]{1,5}\\.[0-9]{1,5}\\.0$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
