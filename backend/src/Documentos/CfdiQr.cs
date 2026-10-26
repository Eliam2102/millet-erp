using System.Globalization;
using QRCoder;

namespace Millet.Documentos;

public static class CfdiQr
{
    public static string Url(string uuid, string rfcEmisor, string rfcReceptor, decimal total, string selloCfdi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rfcEmisor);
        ArgumentException.ThrowIfNullOrWhiteSpace(rfcReceptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(selloCfdi);
        if (selloCfdi.Length < 8)
            throw new ArgumentException("El sello del CFDI debe contener al menos 8 caracteres. Proporcione el sello completo.", nameof(selloCfdi));
        if (total < 0 || total > 9_999_999_999.999999m)
            throw new ArgumentOutOfRangeException(nameof(total), "El total del QR debe ser positivo o cero y tener como máximo 10 enteros y 6 decimales.");
        var importe = decimal.Round(total, 6, MidpointRounding.AwayFromZero).ToString("0000000000.000000", CultureInfo.InvariantCulture);
        return $"https://verificacfdi.facturaelectronica.sat.gob.mx/default.aspx?id={Uri.EscapeDataString(uuid)}&re={Uri.EscapeDataString(rfcEmisor)}&rr={Uri.EscapeDataString(rfcReceptor)}&tt={importe}&fe={Uri.EscapeDataString(selloCfdi[^8..])}";
    }

    public static byte[] Png(string url, int pixelesPorModulo = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelesPorModulo);
        using var datos = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(datos);
        return qr.GetGraphic(pixelesPorModulo);
    }
}
