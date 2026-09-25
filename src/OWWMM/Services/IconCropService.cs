using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace OWWMM.Services;

public readonly record struct IconCropSelection(double X, double Y, double Width, double Height);

public readonly record struct IconSourceInfo(int PixelWidth, int PixelHeight);

public sealed class IconCropService
{
    public const long MaximumSourceBytes = 50L * 1024 * 1024;
    public const long MaximumSourcePixels = 100_000_000;
    public const int OutputSize = 256;
    private const int PreviewMaximumEdge = 1200;

    public IconSourceInfo InspectSource(string sourcePath)
    {
        var path = Path.GetFullPath(sourcePath);
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("图标图片不存在。", path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("图标图片不能是符号链接或重解析点。");
        if (file.Length is <= 0 or > MaximumSourceBytes)
            throw new IOException("图标图片必须小于 50 MB 且不能为空。");

        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream)
            ?? throw new IOException("无法识别图标图片格式。");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0
            || (long)info.Width * info.Height > MaximumSourcePixels)
            throw new IOException("图标图片尺寸无效或超过 1 亿像素限制。");
        return new IconSourceInfo(info.Width, info.Height);
    }

    public Bitmap LoadPreview(string sourcePath, IconSourceInfo sourceInfo)
    {
        using var stream = File.OpenRead(sourcePath);
        var longest = Math.Max(sourceInfo.PixelWidth, sourceInfo.PixelHeight);
        if (longest <= PreviewMaximumEdge) return new Bitmap(stream);
        var targetWidth = Math.Max(1,
            (int)Math.Round(sourceInfo.PixelWidth * (PreviewMaximumEdge / (double)longest)));
        return Bitmap.DecodeToWidth(stream, targetWidth, BitmapInterpolationMode.HighQuality);
    }

    public void SaveCroppedIcon(string sourcePath, string destinationPath, IconCropSelection selection)
    {
        var sourceInfo = InspectSource(sourcePath);
        ValidateSelection(selection);

        byte[] pngBytes;
        using (var input = File.OpenRead(sourcePath))
        using (var codec = SKCodec.Create(input) ?? throw new IOException("无法读取图标图片。"))
        using (var source = new SKBitmap(codec.Info))
        using (var output = new SKBitmap(OutputSize, OutputSize, SKColorType.Rgba8888, SKAlphaType.Premul))
        {
            var decodeResult = codec.GetPixels(source.Info, source.GetPixels());
            if (decodeResult is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                throw new IOException($"读取图标图片失败：{decodeResult}");

            var left = Math.Clamp((float)(selection.X * sourceInfo.PixelWidth), 0, sourceInfo.PixelWidth - 1);
            var top = Math.Clamp((float)(selection.Y * sourceInfo.PixelHeight), 0, sourceInfo.PixelHeight - 1);
            var right = Math.Clamp((float)((selection.X + selection.Width) * sourceInfo.PixelWidth), left + 1, sourceInfo.PixelWidth);
            var bottom = Math.Clamp((float)((selection.Y + selection.Height) * sourceInfo.PixelHeight), top + 1, sourceInfo.PixelHeight);

            using (var canvas = new SKCanvas(output))
            using (var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, new SKRect(left, top, right, bottom),
                    new SKRect(0, 0, OutputSize, OutputSize), paint);
            }

            using var image = SKImage.FromBitmap(output);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new IOException("无法编码裁剪后的图标。");
            pngBytes = encoded.ToArray();
        }
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new IOException("图标目标目录无效。");
        Directory.CreateDirectory(directory);
        var temporary = destination + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(temporary, pngBytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidateSelection(IconCropSelection selection)
    {
        if (!double.IsFinite(selection.X) || !double.IsFinite(selection.Y)
            || !double.IsFinite(selection.Width) || !double.IsFinite(selection.Height)
            || selection.X < 0 || selection.Y < 0 || selection.Width <= 0 || selection.Height <= 0
            || selection.X + selection.Width > 1.000001 || selection.Y + selection.Height > 1.000001)
            throw new ArgumentOutOfRangeException(nameof(selection), "裁剪区域无效。");
    }
}
