using CloudPass.Application.Interfaces;
using QRCoder;

namespace CloudPass.Infrastructure.Services;

public class QrCodeService : IQrCodeService
{
    public byte[] GenerateQrPngBytes(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(pixelsPerModule, new byte[] { 11, 27, 58 }, new byte[] { 255, 255, 255 });
    }

    public string GenerateQrDataUri(string payload, int pixelsPerModule = 8)
    {
        var bytes = GenerateQrPngBytes(payload, pixelsPerModule);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
