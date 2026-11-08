using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.SharedKernel.Application.Correo;

namespace Millet.CuentasPorPagar.Infrastructure.Notificaciones;

/// <summary>
/// Servicio de notificaciones SLA para Cuentas por Pagar (86e3txv0n).
/// Si Graph y el transporte real <see cref="ICorreoSaliente"/> están habilitados,
/// despacha el correo; en entornos sin Graph/Mail.Send, loggea el aviso con los
/// destinatarios configurados sin emitirlo.
/// Deduplicación gestionada en el worker con <c>avisos_sla_revision</c>.
/// </summary>
public sealed class NoOpNotificacionService : INotificacionService
{
    private readonly ILogger<NoOpNotificacionService> _logger;
    private readonly AvisosRevisionOptions _options;
    private readonly ICorreoSaliente? _correo;

    public NoOpNotificacionService(
        ILogger<NoOpNotificacionService> logger,
        IOptions<AvisosRevisionOptions> options)
        : this(logger, options, null)
    {
    }

    public NoOpNotificacionService(
        ILogger<NoOpNotificacionService> logger,
        IOptions<AvisosRevisionOptions> options,
        ICorreoSaliente? correo)
    {
        _logger = logger;
        _options = options.Value;
        _correo = correo;
    }

    public async Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
    {
        var para = _options.Destinatarios(notificacion);
        if (para.Count == 0)
        {
            _logger.LogWarning("[NoOpNotificacionService] Sin destinatarios en {Seccion}; el aviso de factura {Factura} no se envió.",
                AvisosRevisionOptions.SectionName, notificacion.FacturaProveedorId);
            return;
        }

        if (_correo is not null && !_correo.EsSimulado && _options.Proveedor.Equals("Graph", StringComparison.OrdinalIgnoreCase))
        {
            var resultado = await _correo.EnviarAsync(new MensajeCorreo(
                notificacion.EmpresaId,
                "cuentas-por-pagar",
                "aviso-revision",
                notificacion.FacturaProveedorId,
                para,
                $"Factura en revisión: {NombreNivel(notificacion.Nivel)}",
                ConstruirHtml(notificacion)), cancellationToken);

            if (resultado.Estado == EstadoCorreoSaliente.Error)
                throw new InvalidOperationException($"{resultado.Codigo}: {resultado.Motivo}");
            return;
        }

        _logger.LogInformation(
            "[NoOpNotificacionService] SLA {Nivel} factura={Factura} folio={Folio} dep={Dep} motivo={Motivo} dias={Dias}/{SlaDias} Para={Destinatarios}",
            notificacion.Nivel, notificacion.FacturaProveedorId, notificacion.Folio, notificacion.DependenciaRevisoraId,
            notificacion.MotivoRevisionId, notificacion.DiasEnRevision, notificacion.SlaDias, string.Join(", ", para));
    }

    public static string NombreNivel(NivelEscalamientoSla nivel) => nivel switch
    {
        NivelEscalamientoSla.AvisoGerente => "primer aviso",
        NivelEscalamientoSla.SlaVencido => "plazo vencido",
        _ => "escalamiento",
    };

    public static string ConstruirHtml(SlaRevisionNotificacion n)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        return "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
               "<h2>Factura de proveedor en revisión</h2>" +
               $"<p>Factura <strong>{E(n.Folio ?? "sin folio")}</strong> con motivo «{E(n.MotivoNombre)}».</p>" +
               $"<p>Lleva <strong>{n.DiasEnRevision}</strong> días hábiles en revisión (plazo: {n.SlaDias}). Nivel: {E(NombreNivel(n.Nivel))}.</p>" +
               "<p>Revísala en el ERP, bandeja «Revisión por área».</p></div>";
    }
}
