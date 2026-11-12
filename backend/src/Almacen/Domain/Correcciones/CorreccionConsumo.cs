using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Correcciones;

/// <summary>
/// Corrección de un consumo (M-334, M-331). Tabla <c>almacen.correcciones_consumo</c>. Almacén la solicita contra una salida por
/// consumo (<see cref="Pendiente"/>, sin efecto en el saldo) y Costos la autoriza o la rechaza (Fernando Alejos, 11-nov: «Costos
/// autoriza»). Al autorizarla se registra la devolución interna (8.A) ligada a la salida, con fecha de hoy y al costo con que salió:
/// un mes cerrado no se reabre, la corrección queda en el mes corriente (Fernando, 10-nov: «No hay excepciones»).
/// </summary>
public sealed class CorreccionConsumo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string Pendiente = "PENDIENTE";
    public const string Autorizada = "AUTORIZADA";
    public const string Rechazada = "RECHAZADA";

    /// <summary>Tipo (código → texto). El error de captura no tuvo movimiento físico; la devolución sí regresa material.</summary>
    public static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>
    {
        [ErrorCaptura] = "Error de captura (no salió material)",
        [DevolucionUtilizable] = "Devolución física utilizable",
        [DevolucionDanada] = "Devolución física dañada",
    };
    public const string ErrorCaptura = "ERROR_CAPTURA";
    public const string DevolucionUtilizable = "DEVOLUCION_UTILIZABLE";
    public const string DevolucionDanada = "DEVOLUCION_DANADA";

    public Guid EmpresaId { get; set; }
    public Guid SalidaOrigenId { get; private set; }
    public string FolioSalida { get; private set; } = string.Empty;
    public Guid SubAlmacenDestinoId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public string Motivo { get; private set; } = string.Empty;
    public string? Observaciones { get; private set; }
    public List<CorreccionConsumoLinea> Lineas { get; private set; } = [];
    public string Estado { get; private set; } = string.Empty;
    public Guid SolicitadoPor { get; private set; }
    public Guid? DecididoPor { get; private set; }
    public DateTimeOffset? DecididoAt { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public Guid? DevolucionId { get; private set; }
    public string? FolioDevolucion { get; private set; }

    private CorreccionConsumo() { }

    public CorreccionConsumo(Guid empresaId, Guid salidaOrigenId, string folioSalida, Guid subAlmacenDestinoId, string tipo,
        string motivo, string? observaciones, IReadOnlyList<CorreccionConsumoLinea> lineas, Guid solicitadoPor) : base(Guid.CreateVersion7())
    {
        tipo = (tipo ?? string.Empty).Trim();
        if (!Tipos.ContainsKey(tipo))
            throw new BusinessRuleException("CORRECCION_TIPO_INVALIDO", "Elige el tipo: error de captura, devolución utilizable o dañada.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 500)
            throw new BusinessRuleException("CORRECCION_SIN_MOTIVO", "El motivo de la corrección es obligatorio (hasta 500 caracteres).");
        if (lineas.Count == 0 || lineas.Any(l => l.Cantidad <= 0))
            throw new BusinessRuleException("CORRECCION_CANTIDAD_INVALIDA", "Cada renglón corregido debe tener cantidad mayor que cero.");
        if (solicitadoPor == Guid.Empty)
            throw new BusinessRuleException("CORRECCION_SIN_USUARIO", "La solicitud requiere usuario.");
        EmpresaId = empresaId; SalidaOrigenId = salidaOrigenId; FolioSalida = folioSalida; SubAlmacenDestinoId = subAlmacenDestinoId;
        Tipo = tipo; Motivo = motivo.Trim(); Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
        Lineas = [.. lineas]; SolicitadoPor = solicitadoPor; Estado = Pendiente;
    }

    /// <summary>Estado material que entiende la devolución interna (A15: lo dañado va a material en revisión).</summary>
    public string EstadoMaterial => Tipo == DevolucionDanada ? "Danado" : "Integro";

    /// <summary>Separación de funciones: quien solicitó no autoriza su propia corrección.</summary>
    public void VerificarDecision(Guid usuarioId)
    {
        if (Estado != Pendiente)
            throw new BusinessRuleException("CORRECCION_NO_PENDIENTE", $"Solo se decide una corrección pendiente (estado actual: {Estado}).");
        if (usuarioId == Guid.Empty || usuarioId == SolicitadoPor)
            throw new BusinessRuleException("CORRECCION_MISMO_USUARIO", "La corrección debe autorizarla Costos, un usuario distinto de quien la solicitó.");
    }

    public void Autorizar(Guid usuarioId, Guid devolucionId, string folioDevolucion)
    {
        VerificarDecision(usuarioId);
        Estado = Autorizada; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow;
        DevolucionId = devolucionId; FolioDevolucion = folioDevolucion;
    }

    public void Rechazar(Guid usuarioId, string motivo)
    {
        VerificarDecision(usuarioId);
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 500)
            throw new BusinessRuleException("CORRECCION_RECHAZO_SIN_MOTIVO", "El motivo del rechazo es obligatorio (hasta 500 caracteres).");
        Estado = Rechazada; DecididoPor = usuarioId; DecididoAt = DateTimeOffset.UtcNow; MotivoRechazo = motivo.Trim();
    }
}

/// <summary>Renglón corregido: línea de la salida, cantidad y ubicación (rack) a donde regresa.</summary>
public sealed record CorreccionConsumoLinea(Guid LineaSalidaOrigenId, decimal Cantidad, Guid UbicacionId);
