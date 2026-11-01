using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain.Certificados;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.Certificados;

/// <summary>
/// Registro del historial de CSD (F6.9): al capturar un certificado nuevo, el
/// anterior queda Sustituido y el nuevo Vigente. Un certificado revocado no se
/// vuelve a aceptar.
/// </summary>
internal static class HistorialCsd
{
    public static async Task ValidarNoRevocadoAsync(
        IntegracionesFiscalDbContext db, Guid empresaId, string numeroSerie, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(numeroSerie)) return;
        if (await db.CertificadosCsd.AnyAsync(
                c => c.EmpresaId == empresaId && c.NumeroSerie == numeroSerie && c.Estado == EstadoCertificadoCsd.Revocado, ct))
            throw new BusinessRuleException("CONFIG_PAC_CSD_REVOCADO",
                $"El certificado {numeroSerie} se revocó; captura un CSD distinto.");
    }

    /// <summary>
    /// CSD capturado antes de F6.9 sin fila en el historial: se registra leyendo
    /// el .cer guardado. Devuelve el certificado vigente o null si no hay CSD.
    /// </summary>
    public static async Task<CertificadoCsd?> AsegurarAsync(
        IntegracionesFiscalDbContext db, FiscalSecretCipher cipher, Domain.ConfiguracionPac config,
        DateTimeOffset ahora, CancellationToken ct)
    {
        if (!config.CsdConfigurado) return null;
        var vigente = await db.CertificadosCsd
            .Where(c => c.EmpresaId == config.EmpresaId && c.Estado == EstadoCertificadoCsd.Vigente)
            .OrderByDescending(c => c.RegistradoEn)
            .FirstOrDefaultAsync(ct);
        if (vigente is not null) return vigente;
        if (config.CsdRevocadoEn is not null) return null;

        var metadatos = CsdValidador.LeerMetadatos(cipher.Decrypt(config.CsdCertificadoCifrado!));
        return await RegistrarAsync(db, config.EmpresaId, config.Id, metadatos, ahora, "sistema", ct);
    }

    public static async Task<CertificadoCsd> RegistrarAsync(
        IntegracionesFiscalDbContext db, Guid empresaId, Guid configuracionPacId, VigenciaCsd vigencia,
        DateTimeOffset ahora, string? usuario, CancellationToken ct)
    {
        var vigentes = await db.CertificadosCsd
            .Where(c => c.EmpresaId == empresaId && c.Estado == EstadoCertificadoCsd.Vigente)
            .ToListAsync(ct);
        var mismo = vigentes.FirstOrDefault(c => c.NumeroSerie == vigencia.NumeroSerie);
        if (mismo is not null) return mismo;

        foreach (var anterior in vigentes) anterior.Sustituir(ahora, usuario);
        var nuevo = CertificadoCsd.Registrar(empresaId, configuracionPacId, vigencia.NumeroSerie, vigencia.Rfc,
            vigencia.NotBefore, vigencia.NotAfter, ahora, usuario);
        db.CertificadosCsd.Add(nuevo);
        return nuevo;
    }
}
