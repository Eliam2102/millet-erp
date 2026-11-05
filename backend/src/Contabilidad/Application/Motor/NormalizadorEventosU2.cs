using Millet.Contabilidad.Application.Polizas;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Motor;

/// <summary>
/// U-2 (86e3yxngq) · eventos que ya se publicaban y Contabilidad no recibía: Tesorería (depósito del corte, diferencias de corte,
/// movimiento bancario del extracto y reembolso de NC), costo de importación aplicado por Almacén. La salida sin requisición (G-1) la
/// lee #445 en NormalizadorEventosC15; U-2 solo le agrega los importes por causa. Mismo molde que C1.5: espejo del contrato JSON, importes con nombre y plantilla sembrada por migración.
/// Un importe con signo (p. ej. <c>Neto</c>) invierte el lado en el motor: así una sola plantilla cubre faltante y sobrante, egreso e
/// ingreso, o un costo que sube y uno que baja.
/// </summary>
public static partial class NormalizadorEventos
{
    // ---- Wire types (código de develop) ----
    public const string DepositoCajaConfirmado = "tesoreria.deposito-caja.confirmado.v1";
    public const string DiferenciaCorteAutorizada = "tesoreria.diferencia-corte.autorizada.v1";
    public const string DiferenciaCorteDevolucionAplicada = "tesoreria.diferencia-corte.devolucion-aplicada.v1";
    public const string MovimientoBancarioRegistrado = "tesoreria.movimiento-bancario.registrado.v1";
    public const string ReembolsoNotaCreditoActualizado = "tesoreria.reembolso-nota-credito.actualizado.v1";
    public const string ExpedienteCostoAplicado = "almacen.expediente.costo-aplicado.v1";

    // ---- Documento origen ----
    public static readonly (string Modulo, string Tipo) DocDepositoCaja = ("tesoreria", "deposito_caja");
    public static readonly (string Modulo, string Tipo) DocDiferenciaCorte = ("tesoreria", "diferencia_corte");
    public static readonly (string Modulo, string Tipo) DocDevolucionFaltante = ("tesoreria", "devolucion_faltante");
    public static readonly (string Modulo, string Tipo) DocMovimientoBancario = ("tesoreria", "movimiento_bancario");
    public static readonly (string Modulo, string Tipo) DocReembolsoCliente = ("tesoreria", "reembolso_nota_credito");
    public static readonly (string Modulo, string Tipo) DocExpedienteCosto = ("almacen", "expediente_costo");

    /// <summary>Tesorería publica el estado de la devolución; solo «Pagada» mueve dinero.</summary>
    private const string ReembolsoPagado = "Pagada";

    private static EventoContable? NormalizarU2(string tipo, Guid eventoId, string payload) => tipo switch
    {
        DepositoCajaConfirmado => DepositoCaja(eventoId, payload),
        DiferenciaCorteAutorizada => DiferenciaCorte(eventoId, payload),
        DiferenciaCorteDevolucionAplicada => DevolucionFaltante(eventoId, payload),
        MovimientoBancarioRegistrado => MovimientoBancario(eventoId, payload),
        ReembolsoNotaCreditoActualizado => ReembolsoNotaCredito(eventoId, payload),
        ExpedienteCostoAplicado => CostoImportacion(eventoId, payload),
        _ => null,
    };

    // ---------------------------------------------------------------- Tesorería

    /// <summary>
    /// DE: una ficha de depósito del corte de efectivo. Cargo BANCOS · abono EFECTIVO_CAJA_COBRANZA (la caja que cargó la liquidación
    /// #11b). Una póliza por ficha (supuesto a validar: el evento sugiere agrupar por día y sucursal, pero cada ficha es un movimiento
    /// de banco distinto). Fecha contable = la de la ficha.
    /// </summary>
    private static EventoContable DepositoCaja(Guid eventoId, string payload)
    {
        var d = Leer<DepositoCajaContrato>(payload);
        return new(DepositoCajaConfirmado, eventoId, d.EmpresaId, d.FechaFicha, Doc(DocDepositoCaja, d.DepositoId, d.NumeroFicha),
            d.SucursalId, null, null, null, null, MonedaDe(d.Moneda), null, new Dictionary<string, decimal?> { ["Total"] = d.Importe }, [], payload);
    }

    /// <summary>
    /// DC: faltante (Neto +) cargo DEUDORES_FUNCIONARIOS_EMPLEADOS · abono EFECTIVO_CAJA_COBRANZA; sobrante (Neto −) al revés, a favor
    /// del cajero. El motor no tiene auxiliar de empleado: el cajero queda en el documento (la diferencia de Tesorería).
    /// </summary>
    private static EventoContable DiferenciaCorte(Guid eventoId, string payload)
    {
        var d = Leer<DiferenciaCorteContrato>(payload);
        var faltante = string.Equals(d.Tipo, "Faltante", StringComparison.OrdinalIgnoreCase);
        var importe = Math.Abs(d.Importe);
        return new(DiferenciaCorteAutorizada, eventoId, d.EmpresaId, d.DiaCorte, Doc(DocDiferenciaCorte, d.DiferenciaId, null),
            d.SucursalId, null, null, null, null, MonedaDe(d.Moneda), null,
            new Dictionary<string, decimal?> { ["Neto"] = faltante ? importe : -importe }, [], payload);
    }

    /// <summary>DC: el cajero devuelve el faltante a la caja. Cargo EFECTIVO_CAJA_COBRANZA · abono DEUDORES_FUNCIONARIOS_EMPLEADOS.</summary>
    private static EventoContable DevolucionFaltante(Guid eventoId, string payload)
    {
        var d = Leer<DevolucionFaltanteContrato>(payload);
        // Cada devolución es un documento propio (varias por diferencia); sin movimiento de caja, la llave del evento.
        return new(DiferenciaCorteDevolucionAplicada, eventoId, d.EmpresaId, FechaContable.HoyLocal(d.OcurridoEn),
            Doc(DocDevolucionFaltante, d.CajaMovimientoId ?? eventoId, null), d.SucursalId, null, null, null, null, MonedaDe(d.Moneda), null,
            new Dictionary<string, decimal?> { ["Total"] = d.Importe }, [], payload);
    }

    /// <summary>
    /// MB: alta desde el extracto (comisión, interés, depósito no registrado). Contrapartida por concepto: rol
    /// <c>CONCEPTO_BANCARIO_{concepto}</c> (clasificador), con el IVA de la comisión a IVA_ACREDITABLE_PAGADO, contra BANCOS. Egreso con
    /// importes positivos (cargo concepto + IVA · abono bancos), ingreso con negativos (los lados se invierten). El movimiento de IVA
    /// que Tesorería registra aparte (<c>MovimientoBaseId</c>) ya va en la póliza de su base: sin movimiento.
    /// </summary>
    private static EventoContable MovimientoBancario(Guid eventoId, string payload)
    {
        var m = Leer<MovimientoBancarioContrato>(payload);
        var doc = Doc(DocMovimientoBancario, m.MovimientoBancarioId, m.ReferenciaBancaria);
        if (m.MovimientoBaseId is not null)
            return new(MovimientoBancarioRegistrado, eventoId, m.EmpresaId, m.FechaValor, doc, m.SucursalId, null, null, null, null,
                MonedaDe(m.Moneda), null, new Dictionary<string, decimal?> { ["Total"] = 0m, ["Iva"] = 0m }, [], payload);

        var signo = string.Equals(m.Sentido, SentidoEgreso, StringComparison.OrdinalIgnoreCase) ? 1m : -1m;
        var iva = m.Iva ?? 0m;
        var concepto = string.IsNullOrWhiteSpace(m.ConceptoNombre) ? null : m.ConceptoNombre.Trim();
        return new(MovimientoBancarioRegistrado, eventoId, m.EmpresaId, m.FechaValor, doc, m.SucursalId, null, null, null, null,
            MonedaDe(m.Moneda), null,
            new Dictionary<string, decimal?> { ["Base"] = signo * m.Monto, ["Iva"] = signo * iva, ["Total"] = signo * (m.Monto + iva) },
            [new LineaEventoContable(null, signo * m.Monto, null, false, Clasificador: concepto)], payload);
    }

    /// <summary>
    /// DV: devolución de dinero al cliente por una NC, al pagarse. Cargo CLIENTES (auxiliar = cliente) · abono BANCOS. Cliente y sucursal
    /// salen de la póliza RC de la NC (enriquecedor); si aún no existe, espera. Otros estados (autorizada, rechazada) no mueven dinero.
    /// </summary>
    private static EventoContable ReembolsoNotaCredito(Guid eventoId, string payload)
    {
        var r = Leer<ReembolsoContrato>(payload);
        var pagada = string.Equals(r.Estado, ReembolsoPagado, StringComparison.OrdinalIgnoreCase);
        return new(ReembolsoNotaCreditoActualizado, eventoId, r.EmpresaId, r.FechaPago ?? FechaContable.HoyLocal(r.OcurridoEn),
            Doc(DocReembolsoCliente, r.DevolucionId, r.DevolucionFolio), null, null, null, null, null, MonedaDe(r.Moneda), null,
            new Dictionary<string, decimal?> { ["Total"] = pagada ? r.Importe : 0m }, [], payload, FacturaOrigenId: r.NotaCreditoId);
    }

    // ---------------------------------------------------------------- Almacén

    /// <summary>
    /// CI: costo de importación aplicado. Cargo INVENTARIO por artículo (rol por grupo) con lo que quedó en existencia + cargo
    /// COSTO_IMPORTACION_A_RESULTADOS con lo que Almacén mandó a gasto (piezas ya vendidas; Opción A del 26-oct) · abono
    /// GASTOS_IMPORTACION_POR_APLICAR por la diferencia total. Una versión que baja el costo trae importes negativos.
    /// </summary>
    private static EventoContable CostoImportacion(Guid eventoId, string payload)
    {
        var c = Leer<CostoImportacionContrato>(payload);
        var partidas = c.Partidas ?? [];
        var lineas = partidas.Select(p => new LineaEventoContable(null, p.InventarioMxn, null, false, ArticuloId: p.ArticuloId)).ToList();
        return Mxn(ExpedienteCostoAplicado, eventoId, c.EmpresaId, FechaContable.HoyLocal(c.OcurridoEn),
            Doc(DocExpedienteCosto, c.ExpedienteId, $"v{c.NumeroVersion}"), c.SucursalId,
            new()
            {
                ["Inventario"] = lineas.Sum(l => l.Importe),
                ["Gasto"] = partidas.Sum(p => p.GastoImportacionMxn),
                ["Total"] = partidas.Sum(p => p.DiferenciaMxn),
            },
            lineas, payload);
    }

    // ---- Espejos de contrato ----

    private sealed record DepositoCajaContrato(Guid EmpresaId, Guid DepositoId, Guid? SucursalId, decimal Importe, string? Moneda,
        DateOnly FechaFicha, string? NumeroFicha);

    private sealed record DiferenciaCorteContrato(Guid EmpresaId, Guid DiferenciaId, DateOnly DiaCorte, Guid SucursalId, string? Tipo,
        decimal Importe, string? Moneda);

    private sealed record DevolucionFaltanteContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DiferenciaId, Guid SucursalId,
        Guid? CajaMovimientoId, decimal Importe, string? Moneda);

    /// <summary><c>Sentido</c> viaja como número (el outbox no convierte enums): 1 = Ingreso, 2 = Egreso; se acepta también el nombre.</summary>
    private sealed record MovimientoBancarioContrato(Guid EmpresaId, Guid MovimientoBancarioId,
        [property: System.Text.Json.Serialization.JsonConverter(typeof(SentidoJsonConverter))] string? Sentido,
        decimal Monto, string? Moneda, DateOnly FechaValor, string? ReferenciaBancaria, string? ConceptoNombre, decimal? Iva, Guid? SucursalId,
        Guid? MovimientoBaseId);

    private sealed record ReembolsoContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid DevolucionId, string? DevolucionFolio,
        Guid NotaCreditoId, string? Estado, decimal Importe, string? Moneda, DateOnly? FechaPago);

    private sealed record CostoImportacionContrato(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ExpedienteId, int NumeroVersion,
        IReadOnlyList<CostoPartidaContrato>? Partidas, Guid? SucursalId);

    private sealed record CostoPartidaContrato(Guid ArticuloId, decimal DiferenciaMxn, decimal InventarioMxn, decimal GastoImportacionMxn);


    private const string SentidoEgreso = "Egreso";

    /// <summary>Sentido del movimiento de Tesorería (<c>SentidoMovimiento</c>: 1 Ingreso, 2 Egreso) como texto.</summary>
    private sealed class SentidoJsonConverter : System.Text.Json.Serialization.JsonConverter<string?>
    {
        public override string? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                System.Text.Json.JsonTokenType.Number => reader.GetInt32() == 2 ? SentidoEgreso : "Ingreso",
                System.Text.Json.JsonTokenType.String => reader.GetString(),
                _ => null,
            };

        public override void Write(System.Text.Json.Utf8JsonWriter writer, string? value, System.Text.Json.JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
