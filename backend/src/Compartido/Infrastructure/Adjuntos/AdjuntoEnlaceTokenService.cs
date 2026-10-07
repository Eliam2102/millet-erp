using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compartido.Infrastructure.Adjuntos;

/// <summary>Configuración del enlace temporal de descarga (<c>Adjuntos:Enlace</c>).</summary>
public sealed class AdjuntoEnlaceOptions
{
    public const string SectionName = "Adjuntos:Enlace";

    /// <summary>Vida del enlace en segundos (60 por defecto).</summary>
    public int TtlSegundos { get; set; } = 60;
}

/// <summary>
/// Token de descarga con <see cref="ITimeLimitedDataProtector"/> (DataProtection ya configurado, ADR-0037)
/// y un propósito dedicado: un token de otro uso no valida aquí. Atado a adjunto + usuario.
/// La expiración la impone el protector con el reloj real del servidor (no <c>IClock</c>).
/// </summary>
public sealed class AdjuntoEnlaceTokenService : IAdjuntoEnlaceTokenService
{
    private const string Proposito = "Millet.Compartido.Adjuntos.EnlaceDescarga.v1";

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _ttl;

    public AdjuntoEnlaceTokenService(IDataProtectionProvider provider, IOptions<AdjuntoEnlaceOptions> options)
    {
        _protector = provider.CreateProtector(Proposito).ToTimeLimitedDataProtector();
        _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.TtlSegundos));
    }

    public AdjuntoEnlaceEmitido Emitir(Guid adjuntoId, Guid usuarioId)
    {
        var expira = DateTimeOffset.UtcNow.Add(_ttl);
        var token = _protector.Protect($"{adjuntoId:N}.{usuarioId:N}", expira);
        return new AdjuntoEnlaceEmitido(token, expira);
    }

    public AdjuntoEnlaceClaims? Validar(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var plano = _protector.Unprotect(token);
            var partes = plano.Split('.');
            return partes.Length == 2
                && Guid.TryParseExact(partes[0], "N", out var adjuntoId)
                && Guid.TryParseExact(partes[1], "N", out var usuarioId)
                    ? new AdjuntoEnlaceClaims(adjuntoId, usuarioId)
                    : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Firma inválida, manipulado o expirado: todos se tratan igual (sin pistas al llamador).
            return null;
        }
    }
}
