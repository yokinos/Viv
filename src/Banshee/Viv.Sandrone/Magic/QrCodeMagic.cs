using SkiaSharp;
using SkiaSharp.QrCode;
using Viv.Aoi;
using Viv.Echo.Http;

namespace Viv.Sandrone.Magic
{
    /// <summary>
    /// 二维码生成 — 支持 PNG 字节数组、Base64 字符串、本地文件输出，可嵌入中心 Logo（本地文件或网络图片）
    /// </summary>
    public static class QrCodeMagic
    {
        /// <summary>
        /// 生成二维码 PNG 字节数组
        /// </summary>
        /// <param name="content">二维码内容（不能为空）</param>
        /// <param name="size">图片宽高（正方形，默认 512px）</param>
        /// <param name="centerLogoPath">中心 Logo 路径或 URL（可选，支持本地文件和 http/https 网络图片）</param>
        /// <param name="logoSize">中心 Logo 尺寸（默认 96px）</param>
        /// <param name="darkColor">深色模块颜色（默认黑色）</param>
        /// <param name="lightColor">浅色模块颜色（默认白色）</param>
        /// <returns>PNG 格式字节数组</returns>
        /// <exception cref="ArgumentException">content 为空时抛出</exception>
        public static async Task<byte[]> GeneratePngBytesAsync(
            string content,
            int size = 512,
            string? centerLogoPath = null,
            int logoSize = 96,
            SKColor? darkColor = null,
            SKColor? lightColor = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException("content 不能为空", nameof(content));

            // 防无界 bitmap：size 直接决定像素内存（size²×4B），且 QR 尺寸越大越难扫描。512px 已足够，超 2048 一律收口。
            size = Math.Clamp(size, 8, 2048);

            darkColor ??= SKColors.Black;
            lightColor ??= SKColors.White;

            using var bitmap = GenerateQrBitmap(content, size, darkColor.Value, lightColor.Value);

            if (!string.IsNullOrWhiteSpace(centerLogoPath))
            {
                using var logoBitmap = await LoadLogoAsync(centerLogoPath);
                if (logoBitmap != null)
                {
                    using var merged = DrawLogo(bitmap, logoBitmap, logoSize);
                    return EncodePng(merged);
                }
            }

            return EncodePng(bitmap);
        }

        /// <summary>
        /// 生成二维码 Base64 字符串（可直接嵌入 img 标签）
        /// </summary>
        /// <param name="content">二维码内容</param>
        /// <param name="size">图片宽高（默认 512px）</param>
        /// <param name="centerLogoPath">中心 Logo 路径或 URL（可选，支持本地文件和 http/https 网络图片）</param>
        /// <param name="logoSize">中心 Logo 尺寸（默认 96px）</param>
        /// <param name="darkColor">深色模块颜色</param>
        /// <param name="lightColor">浅色模块颜色</param>
        /// <returns>Base64 字符串（不含 data:前缀，需自行拼接）</returns>
        public static async Task<string> GenerateBase64Async(
            string content,
            int size = 512,
            string? centerLogoPath = null,
            int logoSize = 96,
            SKColor? darkColor = null,
            SKColor? lightColor = null)
        {
            var bytes = await GeneratePngBytesAsync(content, size, centerLogoPath, logoSize, darkColor, lightColor);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 生成二维码并保存到本地文件（自动创建目录）
        /// </summary>
        /// <param name="content">二维码内容</param>
        /// <param name="filePath">保存路径（含文件名）</param>
        /// <param name="size">图片宽高（默认 512px）</param>
        /// <param name="centerLogoPath">中心 Logo 路径或 URL（可选，支持本地文件和 http/https 网络图片）</param>
        /// <param name="logoSize">中心 Logo 尺寸（默认 96px）</param>
        /// <param name="darkColor">深色模块颜色</param>
        /// <param name="lightColor">浅色模块颜色</param>
        public static async Task SaveToFileAsync(
            string content,
            string filePath,
            int size = 512,
            string? centerLogoPath = null,
            int logoSize = 96,
            SKColor? darkColor = null,
            SKColor? lightColor = null)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var bytes = await GeneratePngBytesAsync(content, size, centerLogoPath, logoSize, darkColor, lightColor);
            await File.WriteAllBytesAsync(filePath, bytes);
        }

        /// <summary>
        /// 加载 Logo：网络图片下载解码，本地文件直接解码
        /// </summary>
        private static async Task<SKBitmap?> LoadLogoAsync(string pathOrUrl)
        {
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var httpService = VivLocator.GetAutofaService<IVivHttpService>();
                using var stream = await httpService.HttpClient.GetStreamAsync(pathOrUrl);
                return SKBitmap.Decode(stream);
            }

            if (File.Exists(pathOrUrl))
                return SKBitmap.Decode(pathOrUrl);

            return null;
        }

        /// <summary>
        /// 生成二维码 SKBitmap（包含模块矩阵绘制）
        /// </summary>
        private static SKBitmap GenerateQrBitmap(
            string content,
            int size,
            SKColor darkColor,
            SKColor lightColor)
        {
            var qrCode = QRCodeGenerator.CreateQrCode(content, ECCLevel.Q)
                ?? throw new InvalidOperationException("二维码生成失败");

            var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(lightColor);

            using var paint = new SKPaint
            {
                Color = darkColor,
                IsAntialias = false,
                Style = SKPaintStyle.Fill
            };

            var moduleCount = qrCode.Size;
            var moduleSize = (float)size / moduleCount;

            for (int y = 0; y < moduleCount; y++)
            {
                for (int x = 0; x < moduleCount; x++)
                {
                    if (qrCode[y, x])
                    {
                        canvas.DrawRect(
                            x * moduleSize,
                            y * moduleSize,
                            moduleSize,
                            moduleSize,
                            paint);
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 在二维码中心绘制 Logo（白底圆角 + 等比缩放居中）
        /// </summary>
        private static SKBitmap DrawLogo(SKBitmap qrBitmap, SKBitmap logoBitmap, int logoSize)
        {
            var size = qrBitmap.Width;
            var result = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);

            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(qrBitmap, 0, 0, SKSamplingOptions.Default);

            var x = (size - logoSize) / 2f;
            var y = (size - logoSize) / 2f;

            using var bgPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawRoundRect(
                new SKRoundRect(new SKRect(x, y, x + logoSize, y + logoSize), 12, 12),
                bgPaint);

            var innerRect = new SKRect(x + 8, y + 8, x + logoSize - 8, y + logoSize - 8);
            var drawRect = FitCenterRect(logoBitmap.Width, logoBitmap.Height, innerRect);

            canvas.DrawBitmap(logoBitmap, drawRect, SKSamplingOptions.Default);

            return result;
        }

        /// <summary>
        /// 等比缩放居中 — 计算 Logo 在目标矩形中的绘制区域
        /// </summary>
        private static SKRect FitCenterRect(int srcWidth, int srcHeight, SKRect destRect)
        {
            var srcRatio = (float)srcWidth / srcHeight;
            var destRatio = destRect.Width / destRect.Height;

            float drawWidth, drawHeight;

            if (srcRatio > destRatio)
            {
                drawWidth = destRect.Width;
                drawHeight = drawWidth / srcRatio;
            }
            else
            {
                drawHeight = destRect.Height;
                drawWidth = drawHeight * srcRatio;
            }

            var x = destRect.Left + (destRect.Width - drawWidth) / 2f;
            var y = destRect.Top + (destRect.Height - drawHeight) / 2f;

            return new SKRect(x, y, x + drawWidth, y + drawHeight);
        }

        /// <summary>
        /// SKBitmap → PNG 字节数组
        /// </summary>
        private static byte[] EncodePng(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
