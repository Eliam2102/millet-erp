using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.NotaCargo;

/// <summary>
/// Documento interno emitido por Millet al proveedor (§4.6 del
/// 00-levantamiento). Folio interno NCG. Ciclo:
/// <c>Borrador → Autorizada → Aplicada → Formalizada</c> con cancelación
/// posible desde Borrador o Autorizada.
///
/// <para>
/// **F6-PR2 alcance**: factory + autorización + aplicación al saldo de
/// factura + cancelación. La formalización con NC fiscal del proveedor
/// (transición a <see cref="EstadoNotaCargo.Formalizada"/>) llega en
/// F6-PR3 cuando el ciclo de devolución con Almacén cierre.
/// </para>
/// </summary>
public sealed class NotaCargo : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante
{
    public Guid EmpresaId { get; set; }

    public FolioInternoNotaCargo Folio { get; private set; } = default!;
    public short FolioAnio { get; private set; }

    public Guid ProveedorId { get; private set; }

    /// <summary>
    /// Sucursal a cargar contablemente. Nullable porque las NotaCargo
    /// auto-generadas desde una devolución a proveedor (F6-PR3) no
    /// tienen sucursal en el payload del evento; el operador la
    /// completa al autorizar si aplica.
    /// </summary>
    public Guid? SucursalId { get; private set; }

    public string Concepto { get; private set; } = default!;
    public Guid? ConceptoContableId { get; private set; }

    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public decimal? TipoCambio { get; private set; }

    public Guid? FacturaOrigenId { get; private set; }
    public Guid? DevolucionAProveedorId { get; private set; }

    public EstadoNotaCargo Estado { get; private set; }

    /// <summary>FK al <c>NotaCreditoProveedor</c> que la formaliza fiscalmente. F6-PR3+.</summary>
    public Guid? NotaCreditoProveedorId { get; private set; }

    public Guid? CreadoPor { get; private set; }
    public Guid? AutorizadoPor { get; private set; }
    public Guid? AplicadoPor { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }
    public DateTimeOffset? FechaAutorizacion { get; private set; }
    public DateTimeOffset? FechaAplicacion { get; private set; }
    public DateTimeOffset? FechaFormalizacion { get; private set; }
    public DateTimeOffset? FechaCancelacion { get; private set; }

    public string? MotivoCancelacion { get; private set; }

    private NotaCargo() { }

    public static NotaCargo Crear(
        Guid empresaId,
        FolioInternoNotaCargo folio,
        Guid proveedorId,
        Guid? sucursalId,
        string concepto,
        Guid? conceptoContableId,
        decimal monto,
        string moneda,
        decimal? tipoCambio,
        Guid? facturaOrigenId,
        Guid? devolucionAProveedorId,
        Guid? creadoPor,
        DateTimeOffset ahora)
    {
        if (monto <= 0)
            throw new BusinessRuleException("NCG_MONTO_INVALIDO", "El monto de la nota de cargo debe ser > 0.");
        if (string.IsNullOrWhiteSpace(concepto))
            throw new BusinessRuleException("NCG_CONCEPTO_VACIO", "El concepto es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("NCG_MONEDA_INVALIDA", "Moneda ISO 4217 (3 letras).");

        return new NotaCargo
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Folio = folio,
            FolioAnio = (short)folio.Anio,
            ProveedorId = proveedorId,
            SucursalId = sucursalId,
            Concepto = concepto.Trim(),
            ConceptoContableId = conceptoContableId,
            Monto = monto,
            Moneda = moneda.ToUpperInvariant(),
            TipoCambio = tipoCambio,
            FacturaOrigenId = facturaOrigenId,
            DevolucionAProveedorId = devolucionAProveedorId,
            Estado = EstadoNotaCargo.Borrador,
            CreadoPor = creadoPor,
            FechaCreacion = ahora,
        };
    }

    public void Autorizar(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoNotaCargo.Borrador)
        {
            throw new BusinessRuleException(
                "NCG_NO_AUTORIZABLE",
                $"Solo notas en Borrador pueden autorizarse (actual: {Estado}).");
        }
        Estado = EstadoNotaCargo.Autorizada;
        AutorizadoPor = usuarioId;
        FechaAutorizacion = ahora;
    }

    public void Aplicar(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoNotaCargo.Autorizada)
        {
            throw new BusinessRuleException(
                "NCG_NO_APLICABLE",
                $"Solo notas Autorizadas pueden aplicarse (actual: {Estado}).");
        }
        Estado = EstadoNotaCargo.Aplicada;
        AplicadoPor = usuarioId;
        FechaAplicacion = ahora;
    }

    public void Formalizar(Guid notaCreditoProveedorId, DateTimeOffset ahora)
    {
        if (Estado != EstadoNotaCargo.Aplicada)
        {
            throw new BusinessRuleException(
                "NCG_NO_FORMALIZABLE",
                $"Solo notas Aplicadas pueden formalizarse (actual: {Estado}).");
        }
        Estado = EstadoNotaCargo.Formalizada;
        NotaCreditoProveedorId = notaCreditoProveedorId;
        FechaFormalizacion = ahora;
    }

    public void Cancelar(string motivo, DateTimeOffset ahora)
    {
        if (Estado is EstadoNotaCargo.Aplicada or EstadoNotaCargo.Formalizada)
        {
            throw new BusinessRuleException(
                "NCG_NO_CANCELABLE",
                $"No se puede cancelar nota en estado {Estado}.");
        }
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("NCG_MOTIVO_VACIO", "El motivo es obligatorio.");

        Estado = EstadoNotaCargo.Cancelada;
        FechaCancelacion = ahora;
        MotivoCancelacion = motivo.Trim();
    }
}
