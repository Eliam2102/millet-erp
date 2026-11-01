using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain.Certificados;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Application.Certificados;

/// <summary>
/// Evalúa el vencimiento de los CSD en uso de todas las empresas (F6.9, P6.4) y
/// manda el aviso del umbral que toque (30, 15, 5 días y el día del
/// vencimiento), una sola vez por certificado y umbral. Lo agenda
/// <c>AlertasCsdWorker</c>; corre con el filtro por empresa levantado.
/// Devuelve cuántos avisos se enviaron.
/// </summary>
public sealed record EvaluarAlertasCsdCommand : IRequest<int>;

/// <summary>Opciones de los avisos de vencimiento del CSD (sección <c>IntegracionesFiscal:AlertasCsd</c>).</summary>
public sealed class AlertasCsdOptions
{
    public const string SectionName = "IntegracionesFiscal:AlertasCsd";

    public bool Disabled { get; init; }

    public int IntervalHours { get; init; } = 24;

    /// <summary>
    /// Roles que reciben el aviso. Supuesto a validar con Millet (V29: Fiscal y
    /// TI): Contador General y administrador del sistema.
    /// </summary>
    public string[] Roles { get; init; } = ["contador-general", "super-admin"];
}

public sealed class EvaluarAlertasCsdHandler(
    IntegracionesFiscalDbContext db,
    FiscalSecretCipher cipher,
    IAvisoCsdPort aviso,
    IDestinatariosAvisoCsdPort destinatarios,
    IOptions<AlertasCsdOptions> options,
    IClock clock,
    ILogger<EvaluarAlertasCsdHandler> logger)
    : IRequestHandler<EvaluarAlertasCsdCommand, int>
{
    public async Task<int> Handle(EvaluarAlertasCsdCommand command, CancellationToken cancellationToken)
    {
        var ahora = clock.UtcNow;
        var configuraciones = await db.ConfiguracionesPac
            .Where(c => c.Activo && c.CsdCertificadoCifrado != null)
            .ToListAsync(cancellationToken);
        var enviados = 0;

        foreach (var config in configuraciones)
        {
            if (config.CsdRevocadoEn is not null) continue;
            CertificadoCsd? certificado;
            try
            {
                certificado = await HistorialCsd.AsegurarAsync(db, cipher, config, ahora, cancellationToken);
            }
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                logger.LogWarning(ex, "AlertasCsd: no se pudo leer el CSD guardado de la empresa {EmpresaId}.", config.EmpresaId);
                continue;
            }
            if (certificado is null) continue;
            await db.SaveChangesAsync(cancellationToken);

            var dias = certificado.DiasParaVencer(ahora);
            var avisados = await db.AlertasCsd
                .Where(a => a.CertificadoCsdId == certificado.Id)
                .Select(a => a.UmbralDias)
                .ToListAsync(cancellationToken);
            if (PoliticaAlertasCsd.UmbralPendiente(dias, avisados) is not { } umbral) continue;

            var para = await destinatarios.ObtenerAsync(config.EmpresaId, options.Value.Roles, cancellationToken);
            await aviso.EnviarAsync(
                new AvisoCsd(config.EmpresaId, certificado.NumeroSerie, certificado.VenceEn, dias, umbral), para, cancellationToken);
            db.AlertasCsd.Add(new AlertaCsd(config.EmpresaId, certificado.Id, umbral, dias, ahora,
                para.Count > 0 ? string.Join(", ", para.Select(d => d.Email)) : $"sin usuarios con rol {string.Join("/", options.Value.Roles)}"));
            await db.SaveChangesAsync(cancellationToken);
            enviados++;
        }

        return enviados;
    }
}
