using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>Área que gestiona la solicitud junto con Contabilidad (P8.7, R21).</summary>
public enum AreaSolicitudCancelacion
{
    Compras = 1,
    Transportes = 2,
    MateriaPrima = 3,
    ComercioExterior = 4,
}

public enum EstadoSolicitudCancelacion
{
    Pendiente = 1,
    Aceptada = 2,
    Rechazada = 3,
}

/// <summary>
/// Solicitud de cancelación de un CFDI recibido que un proveedor pidió en el
/// portal del SAT (P8.7). Fiscal la registra; Contabilidad decide con el área.
/// Aceptar o rechazar solo deja la decisión y su evidencia; el pasivo se cancela
/// cuando la sincronización del SAT confirma la cancelación del CFDI.
/// </summary>
public sealed class SolicitudCancelacionCfdi : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    /// <summary>Días hábiles que el SAT da al receptor para contestar.</summary>
    public const int PlazoSatDiasHabiles = 3;

    public Guid EmpresaId { get; set; }
    public Guid CfdiRecibidoId { get; private set; }
    public AreaSolicitudCancelacion Area { get; private set; }
    public DateTimeOffset FechaRecepcion { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public string? Notas { get; private set; }

    public EstadoSolicitudCancelacion Estado { get; private set; }
    public DateTimeOffset? FechaDecision { get; private set; }
    public Guid? DecididoPor { get; private set; }
    public string? ComentarioDecision { get; private set; }
    public string? EvidenciaBlobRef { get; private set; }
    public string? EvidenciaNombre { get; private set; }

    /// <summary>M-096: CFDI sustituto (ya ingresado) con el que se aceptó la cancelación de una factura ya pagada.</summary>
    public Guid? SustitutoCfdiId { get; private set; }

    /// <summary>M-096: quien autorizó aceptar la cancelación de una factura con pagos (Contador General).</summary>
    public Guid? AutorizadoPor { get; private set; }

    private SolicitudCancelacionCfdi() { }

    /// <summary>
    /// Vencimiento del plazo del SAT (calculado; los festivos no se consideran). Solo informa o alerta:
    /// al vencer el ERP no cancela ni cambia nada por silencio. Supuesto pendiente de Fiscal.
    /// </summary>
    public DateTimeOffset PlazoSatVence => SumarDiasHabiles(FechaRecepcion, PlazoSatDiasHabiles);

    public static SolicitudCancelacionCfdi Registrar(
        Guid empresaId, Guid cfdiRecibidoId, AreaSolicitudCancelacion area,
        DateTimeOffset fechaRecepcion, Guid? responsableId, string? notas) => new()
    {
        Id = Guid.CreateVersion7(),
        EmpresaId = empresaId,
        CfdiRecibidoId = cfdiRecibidoId,
        Area = area,
        FechaRecepcion = fechaRecepcion.ToUniversalTime(),
        ResponsableId = responsableId,
        Notas = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
        Estado = EstadoSolicitudCancelacion.Pendiente,
    };

    public void Decidir(
        bool aceptar, string comentario, string? evidenciaBlobRef, string? evidenciaNombre,
        Guid? usuarioId, DateTimeOffset ahora, Guid? sustitutoCfdiId = null, Guid? autorizadoPor = null)
    {
        if (Estado != EstadoSolicitudCancelacion.Pendiente)
            throw new BusinessRuleException(
                "SOLICITUD_CANCELACION_YA_DECIDIDA", $"La solicitud ya fue decidida (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(comentario))
            throw new BusinessRuleException(
                "SOLICITUD_CANCELACION_EVIDENCIA_VACIA", "La decisión requiere un comentario de evidencia.");

        Estado = aceptar ? EstadoSolicitudCancelacion.Aceptada : EstadoSolicitudCancelacion.Rechazada;
        FechaDecision = ahora;
        DecididoPor = usuarioId;
        ComentarioDecision = comentario.Trim();
        EvidenciaBlobRef = evidenciaBlobRef;
        EvidenciaNombre = evidenciaNombre;
        SustitutoCfdiId = aceptar ? sustitutoCfdiId : null;
        AutorizadoPor = aceptar ? autorizadoPor : null;
    }

    public static DateTimeOffset SumarDiasHabiles(DateTimeOffset desde, int dias)
    {
        var f = desde;
        while (dias > 0)
        {
            f = f.AddDays(1);
            if (f.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dias--;
        }
        return f;
    }
}
