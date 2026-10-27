using Millet.Facturacion.Domain.Facturas;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Pedidos;

/// <summary>
/// Pedido listo para facturar (§3.bis.1, §3.bis.7 diseño). Representación
/// normalizada en <c>facturacion</c> de un pedido de cualquiera de los tres
/// orígenes (A+W, Planta Pintura, Manual). <b>Binario</b> respecto a facturación
/// (D19) y separado de la <c>FacturaVenta</c> (Facturado = "tiene CFDI vigente",
/// no la factura).
///
/// <para>
/// F1-PR2 implementa el <b>origen Manual</b>: el operador captura encabezado +
/// líneas inline (estilo Requisiciones). La edición sólo se permite en
/// <see cref="EstadoPedidoFacturable.Importado"/> con ETag (concurrencia
/// optimista). La ingesta A+W/Planta Pintura + soft-lock entran en F3/F10.
/// </para>
/// </summary>
public sealed class PedidoFacturable : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    public Guid EmpresaId { get; set; }

    public OrigenPedido Origen { get; private set; }

    /// <summary>Folio/llave del origen; null o referencia interna para Manual.</summary>
    public string? NumeroPedido { get; private set; }

    public Guid SucursalId { get; private set; }

    // Cliente (snapshot de selección del operador; validación contra master en F3).
    public Guid ClienteId { get; private set; }
    public string ClienteNombre { get; private set; } = string.Empty;

    public short CanalVentaId { get; private set; }
    public ComportamientoFiscal ComportamientoFiscal { get; private set; }

    public string Moneda { get; private set; } = "MXN";

    // Obra (snapshot: id numérico + nombre; no va al XML, §7.4).
    public long? ObraId { get; private set; }
    public string? ObraNombre { get; private set; }

    /// <summary>Total del pedido = Σ importe de líneas.</summary>
    public decimal Total { get; private set; }

    /// <summary>
    /// Descuento "ranura" a nivel cabecera del pedido A+W
    /// (<c>BW_AUFTR_KOPF.KO_FALZ</c>, RANURA-PR1). BRUTO (con IVA). No se
    /// resta en el CFDI de la venta: se documenta con una NC automática
    /// post-timbrado (relación 01) y la caja cobra el total neto de NC
    /// ([Decisión 13-K]). <c>null</c> = sin ranura (siempre en Manual y
    /// Planta Pintura).
    /// </summary>
    public decimal? Ranura { get; private set; }

    public EstadoPedidoFacturable Estado { get; private set; }

    /// <summary>La <c>FacturaVenta</c> vigente (no cancelada); null si Importado.</summary>
    public Guid? ComprobanteVigenteId { get; private set; }

    /// <summary>Usuario que capturó el pedido (set si Manual).</summary>
    public Guid? CapturadoPor { get; private set; }

    /// <summary>Comentarios del pedido — <b>jamás</b> llegan al XML (§7.1, invariante 10).</summary>
    public string? Comentarios { get; private set; }

    /// <summary>Versión del pedido en el origen (orden + idempotencia). Null para Manual.</summary>
    public long? VersionOrigen { get; private set; }

    /// <summary>Estatus del pedido en el origen (catálogo A+W: 15/69/70/115…). Null para Manual.</summary>
    public string? EstadoOrigen { get; private set; }

    /// <summary>
    /// Clase del pedido manual (R2 / F6.1): venta directa o emergencia. Sólo
    /// aplica a <see cref="OrigenPedido.Manual"/>; <c>null</c> en A+W/Planta
    /// Pintura y en pedidos manuales capturados antes de F6.1 (legacy), que
    /// deben clasificarse antes de facturarse.
    /// </summary>
    public ClasePedidoManual? ClaseManual { get; private set; }

    /// <summary>Subtipo de la venta directa (activo, almacenable, servicio). Sólo con <see cref="ClasePedidoManual.VentaDirecta"/>.</summary>
    public SubtipoVentaDirecta? SubtipoVentaDirecta { get; private set; }

    /// <summary>Motivo de la captura de emergencia (≤ 500). Obligatorio con <see cref="ClasePedidoManual.Emergencia"/>.</summary>
    public string? MotivoEmergencia { get; private set; }

    /// <summary>
    /// Referencia opcional al pedido A+W relacionado (≤ 50), p. ej. el pedido
    /// que se capturará en A+W cuando vuelva el servicio. Informativa: no se
    /// usa para conciliar ni para write-back.
    /// </summary>
    public string? ReferenciaPedidoAw { get; private set; }

    /// <summary>
    /// Pedido manual clasificado (venta directa o emergencia): se factura con la
    /// serie dedicada <c>VentaDirecta</c> de la sucursal y no se informa a A+W (F6.1).
    /// </summary>
    public bool EsVentaDirectaManual => Origen == OrigenPedido.Manual && ClaseManual is not null;

    /// <summary>Pedido manual capturado en emergencia (A+W fuera de servicio).</summary>
    public bool EsEmergencia => Origen == OrigenPedido.Manual && ClaseManual == ClasePedidoManual.Emergencia;

    /// <summary>Pedido manual capturado antes de F6.1 (sin clase): no se factura hasta clasificarlo.</summary>
    public bool EsManualSinClasificar => Origen == OrigenPedido.Manual && ClaseManual is null;

    public const int MetodoPagoOrigenMaxLength = 5;
    public const int CondicionPagoOrigenMaxLength = 50;

    /// <summary>
    /// Método de pago SAT que manda el origen (A+W: CONTADO→PUE, resto→PPD;
    /// doc 04 §3). Informativo (86e3tfdw2); null en Manual/Planta Pintura.
    /// </summary>
    public string? MetodoPagoOrigen { get; private set; }

    /// <summary>
    /// Condición de pago del pedido en el origen (A+W, catálogo KA_ZAHLBED:
    /// CONTADO, CRÉDITO 30 DÍAS, REPARTO…). Null en Manual/Planta Pintura.
    /// </summary>
    public string? CondicionPagoOrigen { get; private set; }

    /// <summary>
    /// Pedido a crédito (86e3tfdw2): condición de pago del origen distinta de
    /// CONTADO o, sin condición, método PPD. Se persiste (columna filtrable en
    /// bandeja) y se recalcula en cada ingesta/refresco del origen. Los
    /// pedidos manuales nunca son a crédito.
    /// </summary>
    public bool EsCredito { get; private set; }

    private readonly List<PedidoFacturableLinea> _lineas = [];
    public IReadOnlyCollection<PedidoFacturableLinea> Lineas => _lineas.AsReadOnly();

    private PedidoFacturable() { }

    private PedidoFacturable(
        Guid id,
        Guid empresaId,
        OrigenPedido origen,
        string? numeroPedido,
        Guid sucursalId,
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        Guid? capturadoPor,
        long? versionOrigen,
        string? estadoOrigen,
        EstadoPedidoFacturable estado) : base(id)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("PEDIDO_CLIENTE_INVALIDO", "El cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(clienteNombre))
            throw new BusinessRuleException("PEDIDO_CLIENTE_NOMBRE_INVALIDO", "El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("PEDIDO_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");

        EmpresaId = empresaId;
        Origen = origen;
        NumeroPedido = numeroPedido;
        SucursalId = sucursalId;
        ClienteId = clienteId;
        ClienteNombre = clienteNombre;
        CanalVentaId = canalVentaId;
        ComportamientoFiscal = comportamientoFiscal;
        Moneda = moneda.ToUpperInvariant();
        ObraId = obraId;
        ObraNombre = obraNombre;
        Comentarios = comentarios;
        CapturadoPor = capturadoPor;
        VersionOrigen = versionOrigen;
        EstadoOrigen = estadoOrigen;
        Estado = estado;
    }

    /// <summary>Captura un pedido manual en <see cref="EstadoPedidoFacturable.Importado"/>.</summary>
    public static PedidoFacturable CrearManual(
        Guid empresaId,
        string? numeroPedido,
        Guid sucursalId,
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        Guid? capturadoPor,
        ClasePedidoManual? claseManual,
        SubtipoVentaDirecta? subtipoVentaDirecta,
        string? motivoEmergencia,
        string? referenciaPedidoAw)
    {
        var pedido = new PedidoFacturable(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            origen: OrigenPedido.Manual,
            numeroPedido: numeroPedido,
            sucursalId: sucursalId,
            clienteId: clienteId,
            clienteNombre: clienteNombre,
            canalVentaId: canalVentaId,
            comportamientoFiscal: comportamientoFiscal,
            moneda: moneda,
            obraId: obraId,
            obraNombre: obraNombre,
            comentarios: comentarios,
            capturadoPor: capturadoPor,
            versionOrigen: null,
            estadoOrigen: null,
            estado: EstadoPedidoFacturable.Importado);
        pedido.EstablecerClasificacionManual(claseManual, subtipoVentaDirecta, motivoEmergencia, referenciaPedidoAw);
        return pedido;
    }

    /// <summary>
    /// Ingesta un pedido desde A+W en <see cref="EstadoPedidoFacturable.Importado"/>
    /// (§12.1). <c>origen = Aw</c>; lleva la versión + estatus del origen.
    /// </summary>
    public static PedidoFacturable ImportarDesdeAw(
        Guid empresaId,
        string numeroPedido,
        Guid sucursalId,
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        long versionOrigen,
        string? estadoOrigen,
        decimal? ranura = null,
        string? metodoPagoOrigen = null,
        string? condicionPagoOrigen = null)
    {
        var pedido = new PedidoFacturable(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            origen: OrigenPedido.Aw,
            numeroPedido: numeroPedido,
            sucursalId: sucursalId,
            clienteId: clienteId,
            clienteNombre: clienteNombre,
            canalVentaId: canalVentaId,
            comportamientoFiscal: comportamientoFiscal,
            moneda: moneda,
            obraId: obraId,
            obraNombre: obraNombre,
            comentarios: comentarios,
            capturadoPor: null,
            versionOrigen: versionOrigen,
            estadoOrigen: estadoOrigen,
            estado: EstadoPedidoFacturable.Importado);
        pedido.EstablecerRanura(ranura);
        pedido.EstablecerPagoOrigen(metodoPagoOrigen, condicionPagoOrigen);
        return pedido;
    }

    /// <summary>
    /// Ingesta un pedido desde Planta Pintura en <see cref="EstadoPedidoFacturable.Importado"/>
    /// (§12.1, F10-PR2). <c>origen = PlantaPintura</c>. A diferencia de A+W,
    /// <b>exige master preexistente</b> (sin auto-provisión): el handler valida
    /// cliente/artículo antes de llamar a este factory.
    /// </summary>
    public static PedidoFacturable ImportarDesdePlantaPintura(
        Guid empresaId,
        string numeroPedido,
        Guid sucursalId,
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        long versionOrigen,
        string? estadoOrigen) =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            origen: OrigenPedido.PlantaPintura,
            numeroPedido: numeroPedido,
            sucursalId: sucursalId,
            clienteId: clienteId,
            clienteNombre: clienteNombre,
            canalVentaId: canalVentaId,
            comportamientoFiscal: comportamientoFiscal,
            moneda: moneda,
            obraId: obraId,
            obraNombre: obraNombre,
            comentarios: comentarios,
            capturadoPor: null,
            versionOrigen: versionOrigen,
            estadoOrigen: estadoOrigen,
            estado: EstadoPedidoFacturable.Importado);

    /// <summary>
    /// Refresca la cabecera desde el origen (Modificación A+W). <b>La factura
    /// manda</b> (D20): si ya está <c>Facturado</c>, lanza (va a revisión manual).
    /// Si estaba en <c>Excepcion</c> y ahora resuelve, vuelve a <c>Importado</c>.
    /// </summary>
    public void RefrescarCabecera(
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        long versionOrigen,
        string? estadoOrigen,
        decimal? ranura = null,
        string? metodoPagoOrigen = null,
        string? condicionPagoOrigen = null)
    {
        if (Estado == EstadoPedidoFacturable.Facturado)
            throw new BusinessRuleException("PEDIDO_FACTURADO_NO_REFRESCABLE",
                "La factura manda (D20): un pedido facturado no se refresca en automático; va a revisión manual.");
        if (Estado == EstadoPedidoFacturable.Bloqueado)
            throw new BusinessRuleException("PEDIDO_BLOQUEADO",
                "El pedido se está facturando (bloqueado); la modificación se pospone hasta liberarlo.");
        if (Estado == EstadoPedidoFacturable.Cancelado)
            throw new BusinessRuleException("PEDIDO_CANCELADO",
                "Un pedido cancelado requiere un Alta nueva del origen.");

        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("PEDIDO_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");

        ClienteId = clienteId;
        ClienteNombre = clienteNombre;
        CanalVentaId = canalVentaId;
        ComportamientoFiscal = comportamientoFiscal;
        Moneda = moneda.ToUpperInvariant();
        ObraId = obraId;
        ObraNombre = obraNombre;
        Comentarios = comentarios;
        VersionOrigen = versionOrigen;
        EstadoOrigen = estadoOrigen;
        EstablecerRanura(ranura);
        EstablecerPagoOrigen(metodoPagoOrigen, condicionPagoOrigen);
        Estado = EstadoPedidoFacturable.Importado;
    }

    private void EstablecerRanura(decimal? ranura)
    {
        if (ranura is < 0m)
            throw new BusinessRuleException("PEDIDO_RANURA_INVALIDA", "La ranura no puede ser negativa.");
        Ranura = ranura is > 0m ? ranura : null;
    }

    /// <summary>
    /// Fija método/condición de pago del origen y deriva <see cref="EsCredito"/>
    /// (86e3tfdw2). Dato informativo de la ingesta: valores más largos que la
    /// columna se recortan en lugar de rechazar el pedido.
    /// </summary>
    private void EstablecerPagoOrigen(string? metodoPago, string? condicionPago)
    {
        MetodoPagoOrigen = Normalizar(metodoPago, MetodoPagoOrigenMaxLength)?.ToUpperInvariant();
        CondicionPagoOrigen = Normalizar(condicionPago, CondicionPagoOrigenMaxLength);
        EsCredito = CondicionPagoOrigen is not null
            ? !string.Equals(CondicionPagoOrigen, "CONTADO", StringComparison.OrdinalIgnoreCase)
            : MetodoPagoOrigen == "PPD";
    }

    private static string? Normalizar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        var limpio = valor.Trim();
        return limpio.Length > maximo ? limpio[..maximo].TrimEnd() : limpio;
    }

    /// <summary>
    /// Cancela el pedido (Cancelación A+W sin CFDI). <b>La factura manda</b> (D20):
    /// si ya está <c>Facturado</c>, lanza (la cancelación del CFDI va por flujo SAT
    /// con humano). Idempotente si ya está <c>Cancelado</c>.
    /// </summary>
    public void Cancelar()
    {
        if (Estado == EstadoPedidoFacturable.Facturado)
            throw new BusinessRuleException("PEDIDO_FACTURADO_NO_CANCELABLE_AUTO",
                "La factura manda (D20): cancelar un pedido facturado requiere cancelar el CFDI por flujo SAT; va a revisión manual.");
        if (Estado == EstadoPedidoFacturable.Cancelado)
            return;

        Estado = EstadoPedidoFacturable.Cancelado;
        ComprobanteVigenteId = null;
    }

    /// <summary>Bloquea el pedido (soft-lock durante facturación). Sólo desde Importado.</summary>
    public void Bloquear()
    {
        if (Estado != EstadoPedidoFacturable.Importado)
            throw new BusinessRuleException("PEDIDO_NO_BLOQUEABLE",
                $"Sólo un pedido Importado puede bloquearse (actual: {Estado}).");
        Estado = EstadoPedidoFacturable.Bloqueado;
    }

    /// <summary>Libera el soft-lock (vuelve a Importado). Idempotente.</summary>
    public void Liberar()
    {
        if (Estado == EstadoPedidoFacturable.Bloqueado)
            Estado = EstadoPedidoFacturable.Importado;
    }

    /// <summary>Marca el pedido como caído en excepción (validación de ingesta falló).</summary>
    public void MarcarExcepcion()
    {
        if (Estado is EstadoPedidoFacturable.Importado or EstadoPedidoFacturable.Excepcion)
            Estado = EstadoPedidoFacturable.Excepcion;
    }

    /// <summary>Agrega una línea. Sólo permitido mientras el pedido es editable (Importado, sin lock).</summary>
    public PedidoFacturableLinea AgregarLinea(
        Guid? productoId,
        string productoDescripcion,
        string? claveProdServSat,
        string? claveUnidadSat,
        decimal cantidad,
        decimal precio,
        decimal descuento,
        bool requierePedimento,
        string? bomJson = null,
        decimal? tasaIva = null)
    {
        AsegurarEditable("agregar línea");

        var linea = new PedidoFacturableLinea(
            id: Guid.CreateVersion7(),
            pedidoFacturableId: Id,
            posicion: _lineas.Count + 1,
            productoId: productoId,
            productoDescripcion: productoDescripcion,
            claveProdServSat: claveProdServSat,
            claveUnidadSat: claveUnidadSat,
            cantidad: cantidad,
            precio: precio,
            descuento: descuento,
            requierePedimento: requierePedimento,
            bomJson: bomJson,
            tasaIva: tasaIva);

        _lineas.Add(linea);
        return linea;
    }

    /// <summary>Edita el encabezado de un pedido manual aún no facturado.</summary>
    public void EditarCabecera(
        Guid clienteId,
        string clienteNombre,
        short canalVentaId,
        ComportamientoFiscal comportamientoFiscal,
        string moneda,
        long? obraId,
        string? obraNombre,
        string? comentarios,
        ClasePedidoManual? claseManual,
        SubtipoVentaDirecta? subtipoVentaDirecta,
        string? motivoEmergencia,
        string? referenciaPedidoAw)
    {
        AsegurarEditable("editar cabecera");

        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("PEDIDO_CLIENTE_INVALIDO", "El cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(clienteNombre))
            throw new BusinessRuleException("PEDIDO_CLIENTE_NOMBRE_INVALIDO", "El nombre del cliente es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("PEDIDO_MONEDA_INVALIDA", "La moneda debe ser código ISO 4217 de 3 letras.");

        ClienteId = clienteId;
        ClienteNombre = clienteNombre;
        CanalVentaId = canalVentaId;
        ComportamientoFiscal = comportamientoFiscal;
        Moneda = moneda.ToUpperInvariant();
        ObraId = obraId;
        ObraNombre = obraNombre;
        Comentarios = comentarios;

        // F6.1: un pedido manual legacy (sin clase) queda clasificado al editarlo;
        // la validación exige la clase igual que en la captura.
        EstablecerClasificacionManual(claseManual, subtipoVentaDirecta, motivoEmergencia, referenciaPedidoAw);
    }

    /// <summary>
    /// Compuerta de facturación F6.1: un pedido manual capturado antes de la
    /// clasificación (sin clase) no se factura hasta que el operador lo
    /// clasifique como venta directa o emergencia.
    /// </summary>
    public void AsegurarClasificadoParaFacturar()
    {
        if (EsManualSinClasificar)
            throw new BusinessRuleException(
                "PEDIDO_MANUAL_SIN_CLASIFICAR",
                "Clasifica el pedido manual (venta directa o emergencia) antes de facturarlo.");
    }

    /// <summary>
    /// Valida y aplica la clase del pedido manual (R2 / F6.1). No existe clase
    /// «producción» (F6.1-b): sin clase válida el pedido manual se rechaza.
    /// <list type="bullet">
    ///   <item>Venta directa exige subtipo; Emergencia exige motivo y no lleva subtipo.</item>
    ///   <item>Subtipo Activo ⇔ comportamiento fiscal <c>VentaActivoFijo</c>
    ///   (en ambos sentidos), para que la autorización del Director de Administración y Finanzas
    ///   se exija siempre al timbrar (CA6.12).</item>
    /// </list>
    /// </summary>
    private void EstablecerClasificacionManual(
        ClasePedidoManual? claseManual,
        SubtipoVentaDirecta? subtipoVentaDirecta,
        string? motivoEmergencia,
        string? referenciaPedidoAw)
    {
        if (claseManual is null || !Enum.IsDefined(claseManual.Value))
            throw new BusinessRuleException(
                "PEDIDO_MANUAL_CLASE_REQUERIDA",
                "Indica la clase del pedido manual: venta directa o emergencia.");
        if (subtipoVentaDirecta is { } st && !Enum.IsDefined(st))
            throw new BusinessRuleException(
                "PEDIDO_VD_SUBTIPO_REQUERIDO",
                "El subtipo de venta directa no es válido (activo, almacenable o servicio).");

        var motivo = string.IsNullOrWhiteSpace(motivoEmergencia) ? null : motivoEmergencia.Trim();
        var referencia = string.IsNullOrWhiteSpace(referenciaPedidoAw) ? null : referenciaPedidoAw.Trim();
        if (motivo is { Length: > 500 })
            throw new BusinessRuleException("PEDIDO_EMERGENCIA_MOTIVO_INVALIDO", "El motivo de emergencia no puede exceder 500 caracteres.");
        if (referencia is { Length: > 50 })
            throw new BusinessRuleException("PEDIDO_REFERENCIA_AW_INVALIDA", "La referencia al pedido A+W no puede exceder 50 caracteres.");

        if (claseManual == ClasePedidoManual.VentaDirecta)
        {
            if (subtipoVentaDirecta is null)
                throw new BusinessRuleException(
                    "PEDIDO_VD_SUBTIPO_REQUERIDO",
                    "Indica el subtipo de la venta directa: activo, almacenable o servicio.");
        }
        else
        {
            if (motivo is null)
                throw new BusinessRuleException(
                    "PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO",
                    "Indica el motivo de la captura de emergencia.");
            if (subtipoVentaDirecta is not null)
                throw new BusinessRuleException(
                    "PEDIDO_EMERGENCIA_SIN_SUBTIPO",
                    "Un pedido de emergencia no lleva subtipo de venta directa.");
        }

        var esActivo = subtipoVentaDirecta == Pedidos.SubtipoVentaDirecta.Activo;
        if (esActivo != (ComportamientoFiscal == ComportamientoFiscal.VentaActivoFijo))
            throw new BusinessRuleException(
                "PEDIDO_ACTIVO_COMPORTAMIENTO",
                "La venta de un activo fijo va con el comportamiento fiscal «Venta de activo fijo», y ese comportamiento sólo aplica al subtipo Activo.");

        ClaseManual = claseManual;
        SubtipoVentaDirecta = subtipoVentaDirecta;
        MotivoEmergencia = claseManual == ClasePedidoManual.Emergencia ? motivo : null;
        ReferenciaPedidoAw = referencia;
    }

    /// <summary>Reemplaza todas las líneas (edición). Sólo Importado.</summary>
    public void LimpiarLineas()
    {
        AsegurarEditable("reemplazar líneas");
        _lineas.Clear();
    }

    /// <summary>Recalcula el total sumando las líneas.</summary>
    public void RecalcularTotal() => Total = _lineas.Sum(l => l.Importe);

    /// <summary>
    /// Marca el pedido como facturado, ligándolo a su CFDI vigente. Se invoca al
    /// emitir una factura desde el pedido (flujo con soft-lock, F3). Incluido en
    /// F1-PR2 para completar la FSM; el wireup de emisión-desde-pedido es posterior.
    /// </summary>
    public void MarcarFacturado(Guid comprobanteVigenteId)
    {
        if (Estado is EstadoPedidoFacturable.Facturado)
            throw new BusinessRuleException("PEDIDO_YA_FACTURADO", "El pedido ya tiene un CFDI vigente.");
        if (Estado is EstadoPedidoFacturable.Cancelado)
            throw new BusinessRuleException("PEDIDO_CANCELADO", "Un pedido cancelado no puede facturarse.");

        Estado = EstadoPedidoFacturable.Facturado;
        ComprobanteVigenteId = comprobanteVigenteId;
    }

    /// <summary>
    /// Tras cancelar el CFDI vigente, el pedido vuelve a
    /// <see cref="EstadoPedidoFacturable.Importado"/> y queda re-facturable
    /// (invariante 5, §4.3). No aplica a un pedido cancelado en origen.
    /// </summary>
    public void RevertirAFacturable()
    {
        if (Estado == EstadoPedidoFacturable.Cancelado)
            throw new BusinessRuleException(
                "PEDIDO_CANCELADO",
                "Un pedido cancelado en origen no vuelve a facturable.");

        Estado = EstadoPedidoFacturable.Importado;
        ComprobanteVigenteId = null;
    }

    private void AsegurarEditable(string operacion)
    {
        if (Estado != EstadoPedidoFacturable.Importado)
            throw new BusinessRuleException(
                "PEDIDO_NO_EDITABLE",
                $"No se puede {operacion}: el pedido está en estado {Estado} (sólo Importado es editable).");
    }
}
