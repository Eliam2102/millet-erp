using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
/// <summary>Adjunto inmutable de la solicitud; separado del expediente documental de P5.</summary>
public static class EvidenciaBaja
{
    public static void Validar(string? data)
    {
        if (data == null || data.Length > 2_700_000) Rechazar();
        var partes = data!.Split(',', 2);
        if (partes.Length != 2) Rechazar();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(partes[1]); }
        catch (FormatException) { Rechazar(); return; }
        if (bytes.Length == 0 || bytes.Length > 2_000_000) Rechazar();
        var valido = partes[0] switch {
            "data:application/pdf;base64" => bytes.AsSpan().StartsWith("%PDF-"u8),
            "data:image/png;base64" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "data:image/jpeg;base64" => bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }), _ => false };
        if (!valido) Rechazar();
    }
    private static void Rechazar() => throw new BusinessRuleException("AF_EVIDENCIA_INVALIDA", "Adjunte PDF, PNG o JPEG válido de hasta 2 MB.");
}
