using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Almacen.Infrastructure.Notificaciones;

/// <summary>
/// Destinatarios de los avisos de Almacén (sección <c>Almacen:Avisos</c>,
/// 86e3txv0n). Sin correos configurados el aviso no sale y queda en el log.
/// </summary>
public sealed class AvisosAlmacenOptions
{
    public const string SectionName = "Almacen:Avisos";

    /// <summary>Finanzas: ajuste de inventario de nivel 3 aprobado.</summary>
    public List<string> Finanzas { get; set; } = [];

    /// <summary>Coordinador: vale urgente sin regularizar, día 1 del plazo vencido (A14).</summary>
    public List<string> Coordinador { get; set; } = [];

    /// <summary>Jefe de Almacén: vale urgente sin regularizar, día 2 en adelante (A14).</summary>
    public List<string> JefeAlmacen { get; set; } = [];

    internal static IReadOnlyList<string> Validos(IEnumerable<string> correos) =>
        [.. correos.Select(c => c.Trim()).Where(c => c.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// Avisos internos de Almacén por el transporte único de correo (86e3txv0n).
/// Un fallo del transporte queda en <c>core.correo_envio</c>; el aviso no
/// rompe la aprobación ni el barrido del worker.
/// </summary>
public sealed class CorreoNotificacionService(
    ICorreoSaliente correo,
    IOptions<AvisosAlmacenOptions> options,
    ILogger<CorreoNotificacionService> logger) : INotificacionService
{
    public const string Modulo = "almacen";
    private static readonly CultureInfo Mx = CultureInfo.GetCultureInfo("es-MX");

    public async Task NotificarAjusteNivel3AprobadoAsync(AvisoAjusteNivel3 aviso, CancellationToken cancellationToken)
    {
        var para = AvisosAlmacenOptions.Validos(options.Value.Finanzas);
        if (para.Count == 0)
        {
            logger.LogWarning("[AvisosAlmacen] Sin correos en {Seccion}:Finanzas; el aviso del conteo {ConteoId} no se envió.",
                AvisosAlmacenOptions.SectionName, aviso.ConteoId);
            return;
        }
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   "<h2>Ajuste de inventario de nivel 3 aprobado</h2>" +
                   $"<p>Se aprobó un ajuste por <strong>{aviso.MontoNetoMxn.ToString("C2", Mx)} MXN</strong> " +
                   $"el {aviso.Fecha.ToString("dd/MM/yyyy HH:mm", Mx)} en {aviso.Almacenes.Count} almacén(es).</p>" +
                   "<p>Revísalo en el ERP, Almacén › Conteos.</p></div>";
        await correo.EnviarAsync(new MensajeCorreo(aviso.EmpresaId, Modulo, "ajuste-nivel-3", aviso.ConteoId, para,
            "Ajuste de inventario de nivel 3 aprobado", html), cancellationToken);
    }

    public async Task NotificarValeSinRegularizarAsync(
        Guid movimientoValeId,
        string folioVale,
        Guid? personaDestinatariaId,
        DateTimeOffset fechaLimite,
        int diaDelSla,
        CancellationToken cancellationToken)
    {
        // PLATFORM-TODO(<NotificacionService>): avisar también a la persona que recibió el vale
        // (personaDestinatariaId) cuando exista un puerto de lectura de su correo.
        // A14: día 1 → Coordinador; día 2 en adelante → Jefe de Almacén.
        var (clave, lista) = diaDelSla <= 1
            ? ("Coordinador", options.Value.Coordinador)
            : ("JefeAlmacen", options.Value.JefeAlmacen);
        var para = AvisosAlmacenOptions.Validos(lista);
        if (para.Count == 0)
        {
            logger.LogWarning("[AvisosAlmacen] Sin correos en {Seccion}:{Clave}; el aviso del vale {Folio} no se envió.",
                AvisosAlmacenOptions.SectionName, clave, folioVale);
            return;
        }
        var folio = WebUtility.HtmlEncode(folioVale);
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   "<h2>Vale urgente sin regularizar</h2>" +
                   $"<p>El vale <strong>{folio}</strong> venció el {fechaLimite.ToString("dd/MM/yyyy HH:mm", Mx)} " +
                   $"y sigue sin regularizar (día {diaDelSla} del plazo).</p>" +
                   "<p>Regularízalo en el ERP, Almacén › Salidas.</p></div>";
        await correo.EnviarAsync(new MensajeCorreo(null, Modulo, "vale-sin-regularizar", movimientoValeId, para,
            $"Vale {folioVale} sin regularizar (día {diaDelSla})", html), cancellationToken);
    }
}
