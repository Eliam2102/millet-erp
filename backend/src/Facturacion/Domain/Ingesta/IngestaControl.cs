using Millet.Facturacion.Domain.Pedidos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Ingesta;

/// <summary>
/// Memoria de la ingesta (§5 diseño): <b>fuente de verdad</b> de la idempotencia
/// y detección de cambios, independiente de la cola on-prem. Único por
/// <c>(origen, clave_natural)</c>. Sobrevive aunque A+W reescriba la cola (doble
/// candado, D18). No aplica a <c>origen = Manual</c>.
/// </summary>
public sealed class IngestaControl : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public OrigenPedido Origen { get; private set; }

    /// <summary>Llave natural del pedido en el origen (numero_pedido).</summary>
    public string ClaveNatural { get; private set; } = string.Empty;

    /// <summary>Hash del contenido aplicado (detección de cambios).</summary>
    public string HashContenido { get; private set; } = string.Empty;

    /// <summary>Última versión de la cola aplicada (orden + idempotencia).</summary>
    public long UltimaVersionAplicada { get; private set; }

    public EstadoIngesta Estado { get; private set; }

    public Guid? PedidoFacturableId { get; private set; }

    public DateTimeOffset UltimaLecturaAt { get; private set; }

    // ===== Write-back hacia la tabla-puente (ADR-0048 D3, PR5) =====
    // "Outbox local" del canal on-prem: si el UPDATE a aw_solicitud_pedido
    // falla (HC caída) o el evento nace fuera de una solicitud (timbrado/
    // cancelación), aquí queda lo pendiente y WriteBackResultadoWorker lo
    // drena con reintentos. Idempotente: re-escribir lo mismo es inocuo.

    /// <summary>Hay un write-back sin entregar a la tabla-puente.</summary>
    public bool WriteBackPendiente { get; private set; }

    /// <summary>
    /// Solicitud originadora si el pendiente es un reintento del write-back
    /// síncrono por-solicitud; <c>null</c> = write-back por-pedido
    /// (timbrado/cancelación → el adapter usa <c>Guid.Empty</c>).
    /// </summary>
    public Guid? WriteBackSolicitudId { get; private set; }

    public ResultadoSolicitudAw? WriteBackResultado { get; private set; }
    public string? WriteBackEstado { get; private set; }
    public string? WriteBackUuid { get; private set; }
    public string? WriteBackMotivo { get; private set; }
    public int WriteBackIntentos { get; private set; }
    public string? WriteBackUltimoError { get; private set; }
    public DateTimeOffset? WriteBackAt { get; private set; }

    private IngestaControl() { }

    private IngestaControl(
        Guid id, Guid empresaId, OrigenPedido origen, string claveNatural,
        string hashContenido, long version, EstadoIngesta estado,
        Guid? pedidoFacturableId, DateTimeOffset ahora) : base(id)
    {
        if (string.IsNullOrWhiteSpace(claveNatural))
            throw new BusinessRuleException("INGESTA_CLAVE_INVALIDA", "La clave natural es obligatoria.");

        EmpresaId = empresaId;
        Origen = origen;
        ClaveNatural = claveNatural;
        HashContenido = hashContenido;
        UltimaVersionAplicada = version;
        Estado = estado;
        PedidoFacturableId = pedidoFacturableId;
        UltimaLecturaAt = ahora;
    }

    public static IngestaControl Crear(
        Guid empresaId, OrigenPedido origen, string claveNatural,
        string hashContenido, long version, EstadoIngesta estado,
        Guid? pedidoFacturableId, DateTimeOffset ahora) =>
        new(Guid.CreateVersion7(), empresaId, origen, claveNatural, hashContenido, version, estado, pedidoFacturableId, ahora);

    /// <summary>Registra la aplicación de una nueva versión de la cola.</summary>
    public void Aplicar(string hashContenido, long version, EstadoIngesta estado, Guid? pedidoFacturableId, DateTimeOffset ahora)
    {
        HashContenido = hashContenido;
        UltimaVersionAplicada = version;
        Estado = estado;
        PedidoFacturableId = pedidoFacturableId;
        UltimaLecturaAt = ahora;
    }

    /// <summary>Sólo registra que se leyó (sin aplicar — p.ej. versión repetida o pospuesta).</summary>
    public void TocarLectura(DateTimeOffset ahora) => UltimaLecturaAt = ahora;

    public void CambiarEstado(EstadoIngesta estado) => Estado = estado;

    /// <summary>
    /// Encola el write-back POR-PEDIDO (timbrado/cancelación del CFDI): el
    /// worker escribirá <c>uuid</c> + <c>estado_facturacion</c> en todas las
    /// filas del pedido en la tabla-puente (adapter con <c>Guid.Empty</c>).
    /// Reemplaza cualquier pendiente anterior — el estado más reciente manda.
    /// </summary>
    public void SolicitarWriteBackEstado(string estadoFacturacion, string? uuid, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(estadoFacturacion))
            throw new BusinessRuleException("WRITEBACK_ESTADO_INVALIDO", "El estado de facturación es obligatorio.");

        WriteBackPendiente = true;
        WriteBackSolicitudId = null;
        WriteBackResultado = null;
        WriteBackEstado = estadoFacturacion;
        WriteBackUuid = uuid;
        WriteBackMotivo = null;
        WriteBackIntentos = 0;
        WriteBackUltimoError = null;
        UltimaLecturaAt = ahora;
    }

    /// <summary>
    /// Encola el reintento del write-back POR-SOLICITUD cuando la escritura
    /// síncrona falló (HC caída): claim + resultado + motivo de esa solicitud.
    /// La ingesta ya quedó aplicada en el ERP — solo falta avisarle a A+W.
    /// </summary>
    public void SolicitarReintentoWriteBack(
        Guid solicitudId, ResultadoSolicitudAw resultado, string? motivo,
        string? estadoFacturacion, DateTimeOffset ahora)
    {
        WriteBackPendiente = true;
        WriteBackSolicitudId = solicitudId;
        WriteBackResultado = resultado;
        WriteBackEstado = estadoFacturacion;
        WriteBackMotivo = motivo;
        WriteBackIntentos = 0;
        WriteBackUltimoError = null;
        UltimaLecturaAt = ahora;
    }

    /// <summary>El worker entregó el pendiente a la tabla-puente.</summary>
    public void ConfirmarWriteBack(DateTimeOffset ahora)
    {
        WriteBackPendiente = false;
        WriteBackUltimoError = null;
        WriteBackAt = ahora;
    }

    /// <summary>Registra un intento fallido (el worker respeta MaxIntentos).</summary>
    public void RegistrarFalloWriteBack(string error, DateTimeOffset ahora)
    {
        WriteBackIntentos++;
        WriteBackUltimoError = error.Length > 500 ? error[..500] : error;
        UltimaLecturaAt = ahora;
    }
}
