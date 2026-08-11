using Viv.Sandrone.Magic;

namespace Viv.Sandrone.Tests;

public class QrCodeMagicTests
{
    [Fact]
    public async Task 生成PNG字节带PNG头()
    {
        var bytes = await QrCodeMagic.GeneratePngBytesAsync("https://viv.example.com", 128);

        Assert.NotEmpty(bytes);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task Base64输出可还原为同一字节()
    {
        var bytes = await QrCodeMagic.GeneratePngBytesAsync("hello", 128);
        var base64 = await QrCodeMagic.GenerateBase64Async("hello", 128);

        Assert.Equal(bytes, Convert.FromBase64String(base64));
    }

    [Fact]
    public async Task 超大尺寸收口不异常()
    {
        var bytes = await QrCodeMagic.GeneratePngBytesAsync("data", 6000); // clamp 到 2048
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task 内容为空抛异常()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => QrCodeMagic.GeneratePngBytesAsync(" "));
    }

    [Fact]
    public async Task 保存到文件可读回PNG头()
    {
        var path = Path.Combine(Path.GetTempPath(), $"viv-qr-{Guid.NewGuid():N}.png");
        try
        {
            await QrCodeMagic.SaveToFileAsync("file-test", path, 64);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(0x89, bytes[0]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
