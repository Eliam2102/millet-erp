using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.DevolucionesProveedor;

/// <summary>
/// Agregado raíz <b>DevolucionAProveedor</b> (sub-flujo 8.B, F6-PR1).
/// Tabla <c>almacen.devoluciones_proveedor</c>. Coordina el flujo
/// solicitud → autorización Dirección → registro de salida física →
/// conciliación con NC fiscal de CxP (ciclo bidireccional).
///
/// <para>
/// <b>Invariantes</b>:
/// <list type="bullet">
///   <item>Transiciones de estado válidas (ver
///   <see cref="EstadoDevolucionProveedor"/>).</item>
///   <item><see cref="EstadoDevolucionProveedor.Autorizada"/> requiere
///   al menos una evidencia (mismo patrón que NotaCargo en CxP).</item>
///   <item><see cref="EstadoDevolucionProveedor.Registrada"/> requiere
///   <see cref="MovimientoSalidaId"/> no nulo (movimiento físico ya
///   creado).</item>
///   <item><see cref="EstadoDevolucionProveedor.ConciliadaConNcFiscal"/>
///   requiere <see cref="NotaCreditoFiscalId"/> no nulo.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DevolucionAProveedor : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ProveedorId { get; private set; }
    public Guid? RecepcionOrigenId { get; private set; }
    public Guid? FacturaProveedorOrigenId { get; private set; }
    public Guid? OrdenCompraOrigenId { get; private set; }
    public Guid? SubAlmacenOrigenId { get; private set; }
    public EstadoDevolucionProveedor Estado { get; private set; }
    public string Motivo { get; private set; } = string.Empty;

    public Guid SolicitadaPor { get; private set; }
    public DateTimeOffset SolicitadaAt { get; private set; }
    public Guid? AutorizadaPor { get; private set; }
    public DateTimeOffset? AutorizadaAt { get; private set; }
    public string? MotivoRechazo { get; private set; }
    public DateTimeOffset? RechazadaAt { get; private set; }

    public Guid? MovimientoSalidaId { get; private set; }
    public string? FolioMovimientoSalida { get; private set; }
    public DateTimeOffset? RegistradaAt { get; private set; }

    public Guid? NotaCreditoFiscalId { get; private set; }
    public DateTimeOffset? ConciliadaConNcFiscalAt { get; private set; }

    private readonly List<LineaDevolucionProveedor> _lineas = new();
    public IReadOnlyCollection<LineaDevolucionProveedor> Lineas => _lineas.AsReadOnly();

    private readonly List<EvidenciaDevolucionProveedor> _evidencias = new();
    public IReadOnlyCollection<EvidenciaDevolucionProveedor> Evidencias => _evidencias.AsReadOnly();

    private DevolucionAProveedor() { }

    public DevolucionAProveedor(
        Guid id,
        Guid empresaId,
        Guid proveedorId,
        string motivo,
        Guid solicitadaPor,
        Guid? recepcionOrigenId = null,
        Guid? facturaProveedorOrigenId = null,
        Guid? ordenCompraOrigenId = null,
        Guid? subAlmacenOrigenId = null) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_SIN_EMPRESA",
                "La devolución requiere empresa.");
        if (proveedorId == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_SIN_PROVEEDOR",
                "La devolución requiere proveedor.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 1000)
            throw new BusinessRuleException("DEV_PROV_MOTIVO_INVALIDO",
                "El motivo es requerido (≤1000 chars).");
        if (solicitadaPor == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_SIN_SOLICITANTE",
                "El solicitante es requerido.");

        EmpresaId = empresaId;
        ProveedorId = proveedorId;
        RecepcionOrigenId = recepcionOrigenId;
        FacturaProveedorOrigenId = facturaProveedorOrigenId;
        OrdenCompraOrigenId = ordenCompraOrigenId;
        SubAlmacenOrigenId = subAlmacenOrigenId;
        Motivo = motivo;
        SolicitadaPor = solicitadaPor;
        SolicitadaAt = DateTimeOffset.UtcNow;
        Estado = EstadoDevolucionProveedor.Borrador;
    }

    public void AgregarLinea(LineaDevolucionProveedor linea)
    {
        if (Estado != EstadoDevolucionProveedor.Borrador)
            throw new BusinessRuleException("DEV_PROV_NO_BORRADOR",
                $"Solo se pueden agregar líneas en Borrador (estado: {Estado}).");
        _lineas.Add(linea);
    }

    public void AgregarEvidencia(EvidenciaDevolucionProveedor evidencia)
    {
        if (Estado is EstadoDevolucionProveedor.Registrada
            or EstadoDevolucionProveedor.ConciliadaConNcFiscal
            or EstadoDevolucionProveedor.Rechazada)
        {
            throw new BusinessRuleException("DEV_PROV_NO_PUEDE_AGREGAR_EVIDENCIA",
                $"No se puede agregar evidencia en estado {Estado}.");
        }
        _evidencias.Add(evidencia);
    }

    public void SolicitarAutorizacion()
    {
        if (Estado != EstadoDevolucionProveedor.Borrador)
            throw new BusinessRuleException("DEV_PROV_NO_BORRADOR",
                "Solo se puede solicitar autorización desde Borrador.");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("DEV_PROV_SIN_LINEAS",
                "La devolución no tiene líneas.");
        Estado = EstadoDevolucionProveedor.EnAutorizacion;
    }

    public void Autorizar(Guid autorizadoPor)
    {
        if (Estado != EstadoDevolucionProveedor.EnAutorizacion)
            throw new BusinessRuleException("DEV_PROV_NO_EN_AUTORIZACION",
                $"Solo se puede autorizar desde EnAutorizacion (estado: {Estado}).");
        if (_evidencias.Count == 0)
            throw new BusinessRuleException("DEV_PROV_AUTORIZAR_SIN_EVIDENCIA",
                "Autorizar requiere al menos una evidencia adjunta (patrón NotaCargo CxP).");
        if (autorizadoPor == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_AUTORIZADOR_INVALIDO",
                "Autorizador requerido.");

        Estado = EstadoDevolucionProveedor.Autorizada;
        AutorizadaPor = autorizadoPor;
        AutorizadaAt = DateTimeOffset.UtcNow;
    }

    public void Rechazar(string motivoRechazo)
    {
        if (Estado != EstadoDevolucionProveedor.EnAutorizacion)
            throw new BusinessRuleException("DEV_PROV_NO_EN_AUTORIZACION",
                "Solo se puede rechazar desde EnAutorizacion.");
        if (string.IsNullOrWhiteSpace(motivoRechazo) || motivoRechazo.Length > 500)
            throw new BusinessRuleException("DEV_PROV_RECHAZO_INVALIDO",
                "Motivo de rechazo requerido (≤500 chars).");

        Estado = EstadoDevolucionProveedor.Rechazada;
        MotivoRechazo = motivoRechazo;
        RechazadaAt = DateTimeOffset.UtcNow;
    }

    public void MarcarRegistrada(Guid movimientoSalidaId, string folioSalida)
    {
        if (Estado != EstadoDevolucionProveedor.Autorizada)
            throw new BusinessRuleException("DEV_PROV_NO_AUTORIZADA",
                $"Solo se puede registrar desde Autorizada (estado: {Estado}).");
        if (movimientoSalidaId == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_SIN_MOVIMIENTO",
                "Registrar requiere movimiento_salida_id.");

        Estado = EstadoDevolucionProveedor.Registrada;
        MovimientoSalidaId = movimientoSalidaId;
        FolioMovimientoSalida = folioSalida;
        RegistradaAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Cierra el ciclo bidireccional: CxP confirmó NC fiscal tipo CFDI 03
    /// vinculada a esta devolución. El listener
    /// <c>NotaCreditoProveedorRegistradaHandler</c> invoca este método
    /// cuando recibe el evento de CxP con <c>TipoRelacionCfdi=3</c>.
    /// </summary>
    public void ConciliarConNcFiscal(Guid notaCreditoFiscalId)
    {
        if (Estado != EstadoDevolucionProveedor.Registrada)
            throw new BusinessRuleException("DEV_PROV_NO_REGISTRADA",
                $"Solo se puede conciliar desde Registrada (estado: {Estado}).");
        if (notaCreditoFiscalId == Guid.Empty)
            throw new BusinessRuleException("DEV_PROV_NC_FISCAL_INVALIDA",
                "NotaCreditoFiscalId requerido.");

        Estado = EstadoDevolucionProveedor.ConciliadaConNcFiscal;
        NotaCreditoFiscalId = notaCreditoFiscalId;
        ConciliadaConNcFiscalAt = DateTimeOffset.UtcNow;
    }
}
