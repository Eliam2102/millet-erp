using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.Facturacion.Domain.NotasCredito;

/// <summary>
/// Nota de crédito — especialización de <see cref="Comprobante"/> tipo Egreso
/// (§4.4 levantamiento). F4-PR2 cubre el motivo <see cref="MotivoNotaCredito.Amortizacion"/>
/// (aplicación de anticipo): se autogenera y timbra en la misma transacción que
/// la factura final, relacionando (07) tanto la factura de anticipo como la
/// factura final (§6.3). Bonificación / devolución entran en F5.
/// </summary>
public sealed class NotaCredito : Comprobante
{
    public MotivoNotaCredito Motivo { get; private set; }

    public const int ComentariosMaxLength = 1000;

    /// <summary>
    /// Comentarios libres de la NC (86e3yy1bh, Millet 4-nov): amplían o explican la nota y/o su factura relacionada.
    /// Se imprimen en el PDF debajo del motivo y <b>jamás</b> llegan al XML del CFDI.
    /// </summary>
    public string? Comentarios { get; private set; }

    /// <summary>Fija los comentarios (≤ <see cref="ComentariosMaxLength"/>). Vacío = sin comentarios. Solo antes de timbrar.</summary>
    public void FijarComentarios(string? texto)
    {
        if (Estado is not EstadoTimbrado.Borrador)
            throw new BusinessRuleException("NC_INMUTABLE",
                $"No se pueden cambiar los comentarios de una nota de crédito en estado {Estado}.");

        var limpio = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        if (limpio is { Length: > ComentariosMaxLength })
            throw new BusinessRuleException("NC_COMENTARIOS_LARGOS",
                $"Los comentarios no pueden exceder {ComentariosMaxLength} caracteres.");

        Comentarios = limpio;
    }

    /// <summary>Anticipo que se amortiza (solo motivo Amortización).</summary>
    public Guid? AnticipoOrigenId { get; private set; }

    /// <summary>Factura final / origen relacionada.</summary>
    public Guid? FacturaRelacionadaId { get; private set; }

    /// <summary>Importe que afecta inventario: 0 en amortización y bonificación, ≠0 en devolución.</summary>
    public decimal ImporteAfectaInventario { get; private set; }

    // ---- Concepto único (el XML real lo arma F12) ----
    public string ClaveProdServSat { get; private set; } = "84111506";
    public string ClaveUnidadSat { get; private set; } = "ACT";
    public string Descripcion { get; private set; } = "Aplicación de anticipo";

    /// <summary>Momento en que se pidió a Tesorería el reembolso en dinero de esta NC (null = no solicitado).</summary>
    public DateTimeOffset? ReembolsoSolicitadoEn { get; private set; }

    /// <summary>Importe del reembolso solicitado a Tesorería (≤ <see cref="Comprobante.Total"/>).</summary>
    public decimal? ImporteReembolsoSolicitado { get; private set; }

    // ---- F6.3: reembolso programado al emitir y su estado en Tesorería ----

    /// <summary>
    /// La NC se emitió con «Devolver dinero al cliente»: al quedar timbrada (también en un reintento) se pide el reembolso
    /// por el total, con esta cuenta destino. No cambia el timbrado.
    /// </summary>
    public bool ReembolsoAlTimbrar { get; private set; }
    public string? ReembolsoBanco { get; private set; }
    public string? ReembolsoClabe { get; private set; }
    public string? ReembolsoTitular { get; private set; }

    /// <summary>Estado del reembolso que reporta Tesorería (null = sin reembolso).</summary>
    public EstadoReembolsoNotaCredito? EstadoReembolso { get; private set; }
    /// <summary>Fecha del último cambio de estado del reembolso en Tesorería (autorización, rechazo, cancelación o pago).</summary>
    public DateTimeOffset? ReembolsoActualizadoEn { get; private set; }
    /// <summary>Fecha valor del pago del reembolso.</summary>
    public DateOnly? ReembolsoPagadoEl { get; private set; }
    /// <summary>Referencia bancaria del pago del reembolso.</summary>
    public string? ReembolsoReferencia { get; private set; }
    /// <summary>Motivo del rechazo o de la cancelación en Tesorería.</summary>
    public string? ReembolsoMotivoResolucion { get; private set; }

    /// <summary>
    /// F6.3: deja programado el reembolso para cuando la NC quede timbrada. Solo antes de timbrar y solo en motivos que
    /// devuelven dinero; la CLABE se valida aquí (18 dígitos y dígito verificador).
    /// </summary>
    public void ProgramarReembolso(string banco, string clabe, string titular)
    {
        if (Estado is not (EstadoTimbrado.Borrador or EstadoTimbrado.PorAutorizar))
            throw new BusinessRuleException("NC_REEMBOLSO_YA_TIMBRADA",
                "El reembolso se programa al emitir o autorizar la nota; si ya está timbrada, solicítalo desde la nota.");
        if (Motivo is MotivoNotaCredito.Amortizacion or MotivoNotaCredito.Ranura)
            throw new BusinessRuleException("NC_REEMBOLSO_MOTIVO_NO_PERMITIDO",
                "Las notas de amortización de anticipo y de ranura se aplican a la factura; no se reembolsan en dinero.");
        if (string.IsNullOrWhiteSpace(banco) || banco.Trim().Length > 80
            || string.IsNullOrWhiteSpace(titular) || titular.Trim().Length > 200)
            throw new BusinessRuleException("NC_REEMBOLSO_DESTINO_INCOMPLETO",
                "Captura banco, CLABE y titular de la cuenta a la que se devuelve el dinero.");

        ReembolsoAlTimbrar = true;
        ReembolsoBanco = banco.Trim();
        ReembolsoClabe = Clabe.Crear(clabe).Valor;
        ReembolsoTitular = titular.Trim();
    }

    /// <summary>F6.3: ¿hay que pedir el reembolso ahora? Timbrada, programada y aún no solicitada.</summary>
    public bool DebeSolicitarReembolsoProgramado =>
        ReembolsoAlTimbrar && Estado == EstadoTimbrado.Timbrado && ReembolsoSolicitadoEn is null;

    /// <summary>
    /// F6.3: Tesorería reportó un cambio del reembolso. Idempotente: un estado repetido o anterior al actual no cambia nada
    /// (los eventos pueden llegar duplicados o en desorden); «Pagado» es final.
    /// </summary>
    public void RegistrarEstadoReembolso(EstadoReembolsoNotaCredito estado, DateTimeOffset ocurridoEn,
        DateOnly? pagadoEl, string? referencia, string? motivo)
    {
        if (ReembolsoSolicitadoEn is null)
            throw new BusinessRuleException("NC_REEMBOLSO_NO_SOLICITADO", "Esta nota de crédito no tiene un reembolso solicitado.");
        if (EstadoReembolso is EstadoReembolsoNotaCredito.Pagado) return;
        if (estado == EstadoReembolso) return;
        if (estado == EstadoReembolsoNotaCredito.Autorizado && EstadoReembolso is not (null or EstadoReembolsoNotaCredito.Solicitado)) return;

        EstadoReembolso = estado;
        ReembolsoActualizadoEn = ocurridoEn;
        if (estado == EstadoReembolsoNotaCredito.Pagado)
        {
            ReembolsoPagadoEl = pagadoEl;
            ReembolsoReferencia = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim();
        }
        if (estado is EstadoReembolsoNotaCredito.Rechazado or EstadoReembolsoNotaCredito.Cancelado)
            ReembolsoMotivoResolucion = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
    }

    /// <summary>
    /// T9.6 · CA6.17: Tesorería devuelve en dinero una NC ya timbrada. Una sola solicitud por NC;
    /// no cambia el saldo por cobrar ([Decisión 13-K]).
    /// </summary>
    public void SolicitarReembolso(decimal importe, DateTimeOffset ahora)
    {
        if (Estado != EstadoTimbrado.Timbrado)
            throw new BusinessRuleException("NC_REEMBOLSO_NO_TIMBRADA", "Solo una nota de crédito timbrada puede reembolsarse.");
        if (Motivo is MotivoNotaCredito.Amortizacion or MotivoNotaCredito.Ranura)
            throw new BusinessRuleException("NC_REEMBOLSO_MOTIVO_NO_PERMITIDO",
                "Las notas de amortización de anticipo y de ranura se aplican a la factura; no se reembolsan en dinero.");
        if (ReembolsoSolicitadoEn is not null)
            throw new BusinessRuleException("NC_REEMBOLSO_YA_SOLICITADO", "El reembolso de esta nota de crédito ya se solicitó a Tesorería.");
        if (importe <= 0 || importe > Total)
            throw new BusinessRuleException("NC_REEMBOLSO_IMPORTE_INVALIDO",
                $"El importe a reembolsar debe ser mayor a cero y no exceder el total de la nota ({Total:N2}).");

        ReembolsoSolicitadoEn = ahora;
        ImporteReembolsoSolicitado = importe;
        EstadoReembolso = EstadoReembolsoNotaCredito.Solicitado;
    }

    // ---- Origen A+W (86e3wjt5r): la nota nace en A+W y llega por la tabla puente ----

    /// <summary>Número de la nota en A+W (null = nota del ERP). Único por empresa: idempotencia de la ingesta.</summary>
    public string? AwNumeroNota { get; private set; }

    /// <summary>Última versión de la nota recibida de A+W.</summary>
    public long? AwVersionNota { get; private set; }

    /// <summary>Pedido A+W de la factura origen y su versión al recibir la nota: salen en la fila NotaCredito de <c>aw_retorno</c>.</summary>
    public string? AwNumeroPedido { get; private set; }
    public long? AwVersionPedido { get; private set; }

    public Guid? AutorizadaPorId { get; private set; }
    public DateTimeOffset? AutorizadaEn { get; private set; }

    /// <summary>
    /// <see cref="EstadoTimbrado.PorAutorizar"/> → <see cref="EstadoTimbrado.Borrador"/>: queda lista para timbrar.
    /// Solo notas de A+W (las del ERP nacen Borrador).
    /// </summary>
    public void Autorizar(Guid? usuarioId, DateTimeOffset ahora)
    {
        AprobarAutorizacion(ahora);
        AutorizadaPorId = usuarioId;
        AutorizadaEn = ahora;
    }

    private NotaCredito() { }

    private NotaCredito(
        Guid id,
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        MotivoNotaCredito motivo,
        Guid? anticipoOrigenId,
        Guid? facturaRelacionadaId,
        string claveProdServSat,
        string claveUnidadSat,
        string descripcion)
        : base(id, empresaId, TipoComprobante.Egreso, folio, folioNumero, sucursalId, cajaId,
               usuarioEmisorId, receptor, emisor, metodoPago: "PUE",
               formaPago, moneda, tipoCambio, periodoAnio, periodoMes)
    {
        // La NC hereda dimensiones (sucursal/canal) de la factura relacionada
        // — la de amortización, de la factura final ([Decisión 12-10]).
        EstablecerCanalVenta(canalVentaId);
        Motivo = motivo;
        AnticipoOrigenId = anticipoOrigenId;
        FacturaRelacionadaId = facturaRelacionadaId;
        ImporteAfectaInventario = 0m;
        ClaveProdServSat = claveProdServSat;
        ClaveUnidadSat = claveUnidadSat;
        Descripcion = descripcion;
    }

    /// <summary>
    /// Crea la NC de amortización de un anticipo en <see cref="EstadoTimbrado.Borrador"/>.
    /// <paramref name="montoTotal"/> es el importe a amortizar (con IVA); se
    /// desglosa con <paramref name="tasaIva"/> para los totales. El handler agrega
    /// las relaciones 07 (factura de anticipo + factura final) antes de timbrar.
    /// </summary>
    public static NotaCredito CrearAmortizacion(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid anticipoOrigenId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Amortizacion,
            anticipoOrigenId: anticipoOrigenId,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "ACT",
            descripcion: "Aplicación de anticipo");

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>
    /// Crea la NC por bonificación de una factura de venta en
    /// <see cref="EstadoTimbrado.Borrador"/> (§7.1 levantamiento: el descuento
    /// comercial va por NC posterior, <b>nunca</b> en el XML de la venta).
    /// Relación 01 a la factura origen (la agrega el handler). El handler valida
    /// que la factura origen no esté cancelada (invariante 6).
    /// </summary>
    /// <summary>
    /// F6.3 · CA6.17: NC por devolución en dinero de un anticipo no aplicado, en <see cref="EstadoTimbrado.Borrador"/>.
    /// Relación 01 al CFDI del anticipo (la agrega el handler). Sin factura relacionada: no toca la cartera de ninguna
    /// factura; descuenta el saldo del anticipo (<c>Anticipo.RegistrarDevolucion</c>). El reembolso queda programado: al
    /// timbrar se pide a Tesorería por el total.
    /// </summary>
    public static NotaCredito CrearDevolucionAnticipo(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid anticipoId,
        decimal montoTotal,
        decimal? tasaIva,
        string banco,
        string clabe,
        string titular)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: null,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.DevolucionAnticipo,
            anticipoOrigenId: anticipoId,
            facturaRelacionadaId: null,
            claveProdServSat: "84111506",
            claveUnidadSat: "ACT",
            descripcion: "Devolución de anticipo");

        DesglosarTotales(nc, montoTotal, tasaIva);
        nc.ProgramarReembolso(banco, clabe, titular);
        return nc;
    }

    public static NotaCredito CrearBonificacion(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva,
        string? descripcion)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Bonificacion,
            anticipoOrigenId: null,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "ACT",
            descripcion: string.IsNullOrWhiteSpace(descripcion) ? "Bonificación" : descripcion!);

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>
    /// Crea la NC por la "ranura" del pedido A+W en
    /// <see cref="EstadoTimbrado.Borrador"/> (RANURA-PR2). La factura de la
    /// venta va por el total y esta NC documenta el descuento de cabecera;
    /// se autogenera y timbra en la misma transacción que la factura
    /// (patrón de la amortización). Relación 01 a la factura origen (la
    /// agrega el emisor). <paramref name="montoTotal"/> es la ranura BRUTA
    /// (con IVA), tal como la entrega A+W.
    /// </summary>
    public static NotaCredito CrearRanura(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        Guid? usuarioEmisorId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva,
        string descripcion)
    {
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioNumero: folioNumero,
            sucursalId: sucursalId,
            cajaId: cajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: emisor,
            formaPago: formaPago,
            moneda: moneda,
            tipoCambio: tipoCambio,
            periodoAnio: periodoAnio,
            periodoMes: periodoMes,
            canalVentaId: canalVentaId,
            motivo: MotivoNotaCredito.Ranura,
            anticipoOrigenId: null,
            facturaRelacionadaId: facturaRelacionadaId,
            claveProdServSat: "84111506",
            claveUnidadSat: "ACT",
            descripcion: descripcion);

        DesglosarTotales(nc, montoTotal, tasaIva);
        return nc;
    }

    /// <summary>
    /// Crea la NC que llegó de A+W (bonificación o devolución) en <see cref="EstadoTimbrado.PorAutorizar"/>,
    /// ligada a la factura origen. La devolución marca el subtotal como importe que afecta inventario
    /// (el movimiento de almacén queda fuera de esta tarjeta).
    /// </summary>
    public static NotaCredito CrearDesdeAw(
        Guid empresaId,
        string folio,
        long folioNumero,
        Guid sucursalId,
        Guid? cajaId,
        DatosFiscalesReceptor receptor,
        DatosFiscalesEmisor emisor,
        string formaPago,
        string moneda,
        decimal? tipoCambio,
        int periodoAnio,
        int periodoMes,
        short? canalVentaId,
        MotivoNotaCredito motivo,
        Guid facturaRelacionadaId,
        decimal montoTotal,
        decimal? tasaIva,
        string? descripcion,
        string awNumeroNota,
        long awVersionNota,
        string? awNumeroPedido,
        long? awVersionPedido)
    {
        if (motivo is not (MotivoNotaCredito.Bonificacion or MotivoNotaCredito.Devolucion))
            throw new BusinessRuleException("NC_MOTIVO_AW_INVALIDO", "A+W solo manda notas de bonificación o devolución.");
        if (montoTotal <= 0)
            throw new BusinessRuleException("NC_MONTO_INVALIDO", "El monto de la nota de crédito debe ser mayor que cero.");

        var nc = new NotaCredito(
            id: Guid.CreateVersion7(), empresaId: empresaId, folio: folio, folioNumero: folioNumero,
            sucursalId: sucursalId, cajaId: cajaId, usuarioEmisorId: null, receptor: receptor, emisor: emisor,
            formaPago: formaPago, moneda: moneda, tipoCambio: tipoCambio, periodoAnio: periodoAnio,
            periodoMes: periodoMes, canalVentaId: canalVentaId, motivo: motivo, anticipoOrigenId: null,
            facturaRelacionadaId: facturaRelacionadaId, claveProdServSat: "84111506", claveUnidadSat: "ACT",
            descripcion: string.IsNullOrWhiteSpace(descripcion)
                ? (motivo == MotivoNotaCredito.Devolucion ? "Devolución" : "Bonificación")
                : descripcion!);

        nc.AwNumeroNota = awNumeroNota;
        nc.AwVersionNota = awVersionNota;
        nc.AwNumeroPedido = awNumeroPedido;
        nc.AwVersionPedido = awVersionPedido;
        DesglosarTotales(nc, montoTotal, tasaIva);
        if (motivo == MotivoNotaCredito.Devolucion)
            nc.ImporteAfectaInventario = nc.Subtotal;
        nc.EsperarAutorizacion();
        return nc;
    }

    /// <summary>Desglosa el total en subtotal + IVA (el detalle fiscal exacto lo afina F12).</summary>
    private static void DesglosarTotales(NotaCredito nc, decimal montoTotal, decimal? tasaIva)
    {
        var subtotal = tasaIva is > 0 ? Math.Round(montoTotal / (1 + tasaIva.Value), 2) : montoTotal;
        var iva = montoTotal - subtotal;
        nc.EstablecerTotales(subtotal, descuento: 0m, impuestosTrasladados: iva, retenciones: 0m, total: montoTotal);
    }
}
