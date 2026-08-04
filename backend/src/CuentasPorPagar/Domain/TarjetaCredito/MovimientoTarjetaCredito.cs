using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Movimiento individual de una <see cref="Tarjeta"/> empresarial
/// (§3.1, §4.1 del anexo TC, F7-PR4). Cada cargo es atómico — se
/// concilia, refunda, se paga individualmente.
///
/// <para>
/// F7-PR4 implementa los factory <see cref="CapturarCompraConCfdi"/>
/// (§5.1 Flujo A) y <see cref="CapturarCompraSinCfdi"/> (§5.2 Flujo B).
/// La conciliación con estado de cuenta del banco
/// (<see cref="EstadoMovimientoTc.ConciliadoConEstadoCuenta"/>),
/// refunds y casos especiales (intereses, anualidades, comisiones)
/// entran en F7-PR5/PR6.
/// </para>
///
/// <para>
/// <b>Multi-moneda</b>: si <see cref="MonedaOriginal"/> ≠ MXN, se
/// requiere <see cref="TipoCambioCaptura"/> y se guarda
/// <see cref="MontoMxn"/> derivado. Snapshot inmutable — el TC del
/// corte del banco podría diferir; la diferencia se reconcilia en
/// F7-PR6 (D9 del anexo).
/// </para>
/// </summary>
public sealed class MovimientoTarjetaCredito : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid TarjetaId { get; private set; }
    public Guid UsuarioQueUsoId { get; private set; }

    public DateOnly FechaMovimiento { get; private set; }
    public DateOnly? FechaAplicacionBanco { get; private set; }   // populated al conciliar (F7-PR5)

    public TipoMovimientoTc Tipo { get; private set; }
    public EstadoMovimientoTc Estado { get; private set; }

    // Importes
    public decimal MontoOriginal { get; private set; }
    public string MonedaOriginal { get; private set; } = "MXN";
    public decimal? TipoCambioCaptura { get; private set; }
    public decimal MontoMxn { get; private set; }

    // Comercio
    public string MerchantRaw { get; private set; } = default!;
    public string MerchantNormalizado { get; private set; } = default!;
    public string? DescripcionLibre { get; private set; }

    // Vinculaciones fiscales (Flujo A)
    public Guid? CfdiRecibidoId { get; private set; }
    public Guid? FacturaProveedorId { get; private set; }
    public Guid? ProveedorId { get; private set; }
    public string ConceptoContable { get; private set; } = default!;

    // Refund (F7-PR5/PR6)
    public Guid? MovimientoOriginalId { get; private set; }

    // Conciliación (F7-PR5)
    public Guid? EstadoCuentaTcId { get; private set; }
    public Guid? EstadoCuentaTcLineaId { get; private set; }
    public bool CapturaRetroactiva { get; private set; }

    // Adjuntos / Disputa
    public string? TicketBlobRef { get; private set; }
    public bool EnDisputa { get; private set; }
    public string? MotivoDisputa { get; private set; }
    public DateOnly? FechaInicioDisputa { get; private set; }

    private MovimientoTarjetaCredito() { }

    /// <summary>
    /// Flujo A — Captura con CFDI (§5.1). El handler debe haber creado
    /// la <c>FacturaProveedor</c> previamente y pasar su Id.
    /// </summary>
    public static MovimientoTarjetaCredito CapturarCompraConCfdi(
        Guid empresaId,
        Tarjeta tarjeta,
        Guid usuarioQueUsoId,
        DateOnly fechaMovimiento,
        decimal montoOriginal,
        string monedaOriginal,
        decimal? tipoCambioCaptura,
        string merchantRaw,
        string? descripcionLibre,
        Guid cfdiRecibidoId,
        Guid facturaProveedorId,
        Guid proveedorId,
        string conceptoContable)
    {
        if (cfdiRecibidoId == Guid.Empty)
            throw new BusinessRuleException("TC_MOV_CFDI_VACIO",
                "El CfdiRecibidoId es obligatorio para CompraConCfdi.");
        if (facturaProveedorId == Guid.Empty)
            throw new BusinessRuleException("TC_MOV_FACTURA_VACIA",
                "El FacturaProveedorId es obligatorio para CompraConCfdi.");
        if (proveedorId == Guid.Empty)
            throw new BusinessRuleException("TC_MOV_PROVEEDOR_VACIO",
                "El ProveedorId es obligatorio para CompraConCfdi.");

        var mov = CrearBase(
            empresaId, tarjeta, usuarioQueUsoId, fechaMovimiento,
            TipoMovimientoTc.CompraConCfdi,
            montoOriginal, monedaOriginal, tipoCambioCaptura,
            merchantRaw, descripcionLibre, conceptoContable);

        mov.CfdiRecibidoId = cfdiRecibidoId;
        mov.FacturaProveedorId = facturaProveedorId;
        mov.ProveedorId = proveedorId;
        return mov;
    }

    /// <summary>
    /// Flujo B — Captura sin CFDI (§5.2). Solo ticket; no genera
    /// FacturaProveedor.
    /// </summary>
    public static MovimientoTarjetaCredito CapturarCompraSinCfdi(
        Guid empresaId,
        Tarjeta tarjeta,
        Guid usuarioQueUsoId,
        DateOnly fechaMovimiento,
        decimal montoOriginal,
        string monedaOriginal,
        decimal? tipoCambioCaptura,
        string merchantRaw,
        string? descripcionLibre,
        string conceptoContable,
        string? ticketBlobRef)
    {
        var mov = CrearBase(
            empresaId, tarjeta, usuarioQueUsoId, fechaMovimiento,
            TipoMovimientoTc.CompraSinCfdi,
            montoOriginal, monedaOriginal, tipoCambioCaptura,
            merchantRaw, descripcionLibre, conceptoContable);

        mov.TicketBlobRef = ticketBlobRef;
        return mov;
    }

    private static MovimientoTarjetaCredito CrearBase(
        Guid empresaId,
        Tarjeta tarjeta,
        Guid usuarioQueUsoId,
        DateOnly fechaMovimiento,
        TipoMovimientoTc tipo,
        decimal montoOriginal,
        string monedaOriginal,
        decimal? tipoCambioCaptura,
        string merchantRaw,
        string? descripcionLibre,
        string conceptoContable)
    {
        if (usuarioQueUsoId == Guid.Empty)
            throw new BusinessRuleException("TC_MOV_USUARIO_VACIO",
                "El usuario que usó la TC es obligatorio.");
        if (montoOriginal <= 0)
            throw new BusinessRuleException("TC_MOV_MONTO_INVALIDO",
                "El monto del cargo debe ser > 0.");
        if (string.IsNullOrWhiteSpace(monedaOriginal) || monedaOriginal.Length != 3)
            throw new BusinessRuleException("TC_MOV_MONEDA_INVALIDA",
                "La moneda original debe ser código ISO 4217 de 3 letras.");
        if (string.IsNullOrWhiteSpace(merchantRaw))
            throw new BusinessRuleException("TC_MOV_MERCHANT_VACIO",
                "El nombre del comercio es obligatorio.");
        if (string.IsNullOrWhiteSpace(conceptoContable))
            throw new BusinessRuleException("TC_MOV_CONCEPTO_VACIO",
                "El concepto contable es obligatorio.");

        var monedaUpper = monedaOriginal.ToUpperInvariant();
        if (monedaUpper == "MXN")
        {
            if (tipoCambioCaptura is not null)
                throw new BusinessRuleException("TC_MOV_TC_INESPERADO",
                    "MXN no acepta tipo de cambio — debe ser null.");
        }
        else
        {
            if (tipoCambioCaptura is not decimal tc || tc <= 0)
                throw new BusinessRuleException("TC_MOV_TC_REQUERIDO",
                    "Moneda extranjera requiere tipo de cambio > 0.");
        }

        tarjeta.AsegurarPuedeAceptarCargo(fechaMovimiento);
        tarjeta.AsegurarUsuarioAutorizado(usuarioQueUsoId, fechaMovimiento);

        var montoMxn = monedaUpper == "MXN"
            ? montoOriginal
            : Math.Round(montoOriginal * tipoCambioCaptura!.Value, 2, MidpointRounding.ToEven);

        return new MovimientoTarjetaCredito
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            TarjetaId = tarjeta.Id,
            UsuarioQueUsoId = usuarioQueUsoId,
            FechaMovimiento = fechaMovimiento,
            Tipo = tipo,
            Estado = EstadoMovimientoTc.Registrado,
            MontoOriginal = montoOriginal,
            MonedaOriginal = monedaUpper,
            TipoCambioCaptura = tipoCambioCaptura,
            MontoMxn = montoMxn,
            MerchantRaw = merchantRaw.Trim(),
            MerchantNormalizado = NormalizarMerchant(merchantRaw),
            DescripcionLibre = descripcionLibre?.Trim(),
            ConceptoContable = conceptoContable.Trim(),
        };
    }

    /// <summary>
    /// Normaliza el nombre del comercio para matching (§3.2 anexo TC):
    /// UPPER + trim + colapsar espacios. Mejora la calidad del match
    /// automático en F7-PR5.
    /// </summary>
    internal static string NormalizarMerchant(string raw)
    {
        var upper = raw.Trim().ToUpperInvariant();
        var colapsado = System.Text.RegularExpressions.Regex.Replace(upper, @"\s+", " ");
        return colapsado;
    }

    /// <summary>
    /// Transición F7-PR5: marca el movimiento como conciliado con el
    /// estado de cuenta del banco, vinculándolo a la línea cruda
    /// específica. Idempotente — si ya está conciliado contra el mismo
    /// estado de cuenta, no hace nada.
    /// </summary>
    public void MarcarConciliadoConEstadoCuenta(
        Guid estadoCuentaTcId,
        Guid lineaBancoId,
        DateOnly fechaAplicacionBanco)
    {
        if (Estado == EstadoMovimientoTc.ConciliadoConEstadoCuenta
            && EstadoCuentaTcId == estadoCuentaTcId)
        {
            return; // idempotente
        }
        if (Estado != EstadoMovimientoTc.Registrado)
        {
            throw new BusinessRuleException(
                "TC_MOV_NO_CONCILIABLE",
                $"Solo se concilia desde Registrado (actual: {Estado}).");
        }

        Estado = EstadoMovimientoTc.ConciliadoConEstadoCuenta;
        EstadoCuentaTcId = estadoCuentaTcId;
        EstadoCuentaTcLineaId = lineaBancoId;
        FechaAplicacionBanco = fechaAplicacionBanco;
    }

    // ============================================================================
    // F7-PR6: factories para tipos especiales + transiciones de pago/disputa.
    // ============================================================================

    /// <summary>
    /// Flujo E (§5.5) — refund de un movimiento original. Monto positivo;
    /// el handler responsable lo contabiliza como abono contra la TC.
    /// </summary>
    public static MovimientoTarjetaCredito CapturarRefund(
        Guid empresaId,
        Tarjeta tarjeta,
        Guid usuarioQueUsoId,
        DateOnly fechaMovimiento,
        decimal montoOriginal,
        string monedaOriginal,
        decimal? tipoCambioCaptura,
        string merchantRaw,
        string conceptoContable,
        Guid movimientoOriginalId)
    {
        if (movimientoOriginalId == Guid.Empty)
            throw new BusinessRuleException("TC_MOV_ORIGINAL_VACIO",
                "El movimiento original es obligatorio para un refund.");

        var mov = CrearBase(
            empresaId, tarjeta, usuarioQueUsoId, fechaMovimiento,
            TipoMovimientoTc.Refund,
            montoOriginal, monedaOriginal, tipoCambioCaptura,
            merchantRaw, descripcionLibre: null, conceptoContable);

        mov.MovimientoOriginalId = movimientoOriginalId;
        return mov;
    }

    /// <summary>
    /// Captura un movimiento especial del banco — Interés moratorio
    /// (§8.4), Anualidad (§8.3), Comisión por divisa (§8.2). Sin CFDI,
    /// sin proveedor de gasto; solo asiento contable interno.
    /// </summary>
    public static MovimientoTarjetaCredito CapturarMovimientoEspecial(
        Guid empresaId,
        Tarjeta tarjeta,
        Guid usuarioQueUsoId,
        DateOnly fechaMovimiento,
        TipoMovimientoTc tipo,
        decimal montoOriginal,
        string monedaOriginal,
        decimal? tipoCambioCaptura,
        string merchantRaw,
        string conceptoContable)
    {
        if (tipo is not (TipoMovimientoTc.GastoFinanciero
                         or TipoMovimientoTc.Anualidad
                         or TipoMovimientoTc.ComisionDivisa))
        {
            throw new BusinessRuleException(
                "TC_MOV_TIPO_NO_ESPECIAL",
                $"CapturarMovimientoEspecial solo acepta GastoFinanciero/Anualidad/ComisionDivisa (recibido: {tipo}).");
        }

        return CrearBase(
            empresaId, tarjeta, usuarioQueUsoId, fechaMovimiento, tipo,
            montoOriginal, monedaOriginal, tipoCambioCaptura,
            merchantRaw, descripcionLibre: null, conceptoContable);
    }

    /// <summary>
    /// Marca el movimiento como capturado retroactivamente desde la
    /// pantalla de conciliación (§5.3 paso 7, D13).
    /// </summary>
    public void MarcarCapturaRetroactiva()
    {
        CapturaRetroactiva = true;
    }

    /// <summary>
    /// Inicia disputa sobre este movimiento (§8.5 anexo). Excluido del
    /// cierre hasta resolver.
    /// </summary>
    public void Disputar(string motivo, DateOnly fecha)
    {
        if (Estado == EstadoMovimientoTc.PagadoAlBanco)
            throw new BusinessRuleException(
                "TC_MOV_PAGADO_NO_DISPUTABLE",
                "Un movimiento ya pagado al banco no se puede disputar — investigar refund.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("TC_MOV_MOTIVO_DISPUTA_VACIO",
                "El motivo de la disputa es obligatorio.");

        EnDisputa = true;
        MotivoDisputa = motivo.Trim();
        FechaInicioDisputa = fecha;
        Estado = EstadoMovimientoTc.EnDisputa;
    }

    /// <summary>
    /// Resuelve la disputa: legítimo → vuelve a Registrado; no legítimo
    /// → Reversado (fue refundado o anulado por el banco).
    /// </summary>
    public void ResolverDisputa(bool fueLegitimo)
    {
        if (!EnDisputa)
            throw new BusinessRuleException("TC_MOV_NO_EN_DISPUTA",
                "No se puede resolver disputa de un movimiento que no está en disputa.");

        EnDisputa = false;
        MotivoDisputa = null;
        FechaInicioDisputa = null;
        Estado = fueLegitimo ? EstadoMovimientoTc.Registrado : EstadoMovimientoTc.Reversado;
    }

    /// <summary>
    /// Marca el movimiento como pagado al banco — invocado en batch
    /// cuando el <see cref="EstadoCuentaTc"/> asociado transiciona a
    /// <see cref="EstadoCuentaTcStatus.PagadoBanco"/> (§5.4 paso 5).
    /// Movs en disputa / reversados se omiten silenciosamente.
    /// </summary>
    public void MarcarPagadoAlBanco()
    {
        if (Estado is EstadoMovimientoTc.EnDisputa or EstadoMovimientoTc.Reversado)
            return;

        if (Estado != EstadoMovimientoTc.ConciliadoConEstadoCuenta)
        {
            throw new BusinessRuleException(
                "TC_MOV_NO_PAGABLE_BANCO",
                $"Solo movimientos ConciliadoConEstadoCuenta se pagan al banco (actual: {Estado}).");
        }

        Estado = EstadoMovimientoTc.PagadoAlBanco;
    }
}
