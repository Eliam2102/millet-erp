using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// F2-PR2: comando que registra una <b>recepción Variante A</b> (con
/// factura/CFDI ya conocido — insumos y refacciones). Valida la OC
/// contra <see cref="IComprasOcReadPort"/>, calcula el costo unitario
/// como el precio de la línea de OC, genera el movimiento tipo
/// <c>EntradaCompra</c> en estado Registrado (el trigger PG actualiza
/// el saldo en la misma TX), reserva folio y publica el evento
/// <c>almacen.oc_recepcion.registrada.v1</c> al outbox para que Compras
/// y CxP lo consuman.
///
/// <para>
/// Pendiente exacto (86e3tab1k, sin tolerancia en piezas): límite = solicitada − máx(recibido en Compras, recibido propio de Almacén), con lock por OC.
/// </para>
/// </summary>
public sealed record RegistrarRecepcionConFacturaCommand(
    Guid OrdenCompraId,
    DateOnly FechaMovimiento,
    // Vínculo fiscal obligatorio (§5.4): CfdiRecibidoId cuando el CFDI ya
    // está en el repositorio de CxP, o CfdiUuidFiscal (folio fiscal del
    // impreso) cuando aún no llegó por el canal SAT/mailbox — CxP enlaza
    // después al procesar el XML. El validator exige al menos uno.
    Guid? CfdiRecibidoId,
    string? CfdiUuidFiscal,
    string? Observaciones,
    IReadOnlyList<RegistrarRecepcionLineaInput> Lineas,
    // PR4: helper de cabecera (bin N4 que el almacenista auto-aplicó a las
    // líneas vacías en el sheet). Opcional — null = no usó el helper. Solo
    // reportería; la ubicación efectiva de cada línea viaja en cada
    // RegistrarRecepcionLineaInput.UbicacionId y es la que manda.
    Guid? UbicacionHelperId = null, ImportacionRecepcionInput? Importacion = null) : IRequest<RegistrarRecepcionResponse>;

public sealed record RegistrarRecepcionLineaInput(
    Guid ArticuloId,
    Guid? LineaOcId,
    decimal Cantidad,
    string? UbicacionReferencia,
    string? Comentario,
    // C7.2b: bin real destino (rack N4). Obligatorio en entradas — el
    // artículo debe estar asignado y no puede ir a la ÚNICA. Nullable en el
    // tipo por compat de construcción; el validator lo exige.
    Guid? UbicacionId = null, string? UnidadCapturada = null,
    // ALM-04: parte de Cantidad que llegó dañada; va a material en revisión (MAT-REV), no a disponible.
    decimal CantidadDanada = 0m, string? MotivoDano = null, string? FraccionArancelaria = null,
    // F1-MP-01 S4: lote del proveedor. Obligatorio si el artículo está ligado a A+W (LOTE_REQUERIDO), opcional si no.
    string? Lote = null);

public sealed record RegistrarRecepcionResponse(
    Guid RecepcionId,
    string Folio);

public sealed class RegistrarRecepcionConFacturaValidator
    : AbstractValidator<RegistrarRecepcionConFacturaCommand>
{
    public RegistrarRecepcionConFacturaValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099)
            .WithMessage("Fecha de movimiento fuera de rango razonable.");
        // El vínculo fiscal obligatorio se valida en el handler (422 RECEPCION_SIN_CFDI) para que la pantalla reciba el código.
        RuleFor(c => c.CfdiUuidFiscal!)
            .Matches(@"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")
            .When(c => !string.IsNullOrWhiteSpace(c.CfdiUuidFiscal))
            .WithMessage("El folio fiscal no cumple el formato UUID del SAT (8-4-4-4-12 hex).");
        RuleFor(c => c.Observaciones!).MaximumLength(500)
            .When(c => c.Observaciones is not null)
            .WithMessage("Las observaciones no pueden exceder 500 caracteres.");
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ArticuloId).NotEqual(Guid.Empty);
            linea.RuleFor(l => l.Cantidad).GreaterThan(0);
            linea.RuleFor(l => l.UnidadCapturada).MaximumLength(20);

            linea.RuleFor(l => l.UbicacionReferencia!).MaximumLength(100)
                .When(l => l.UbicacionReferencia is not null);
            linea.RuleFor(l => l.Comentario!).MaximumLength(500)
                .When(l => l.Comentario is not null);
            linea.RuleFor(l => l.CantidadDanada).GreaterThanOrEqualTo(0)
                .WithMessage("La cantidad dañada no puede ser negativa.");
            linea.RuleFor(l => l.CantidadDanada).LessThanOrEqualTo(l => l.Cantidad)
                .WithMessage("La cantidad dañada no puede ser mayor que la cantidad recibida.");
            linea.RuleFor(l => l.MotivoDano).NotEmpty().When(l => l.CantidadDanada > 0)
                .WithMessage("Indica el motivo del daño.");
            linea.RuleFor(l => l.MotivoDano!).MaximumLength(500)
                .When(l => l.MotivoDano is not null);
            linea.RuleFor(l => l.Lote)
                .Must(x => Domain.Movimientos.LineaMovimiento.NormalizarLote(x) is not { Length: > Domain.Movimientos.LineaMovimiento.LoteMaxLength })
                .WithMessage($"El lote no puede exceder {Domain.Movimientos.LineaMovimiento.LoteMaxLength} caracteres.");
        });
    }
}

public sealed class RegistrarRecepcionConFacturaHandler
    : IRequestHandler<RegistrarRecepcionConFacturaCommand, RegistrarRecepcionResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IPeriodoContableReadPort _periodoContable;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IConversionUnidadPort _conversion;
    private readonly ICxpDocumentosReadPort _cxpDocumentos;
    private readonly IProveedorReadPort _proveedores;
    // Opcionales solo para pruebas unitarias sin medidas; la API los inyecta siempre.
    private readonly IArticuloReadPort? _articulos;
    private readonly IVarianteVidrioReadPort? _variantes;

    public RegistrarRecepcionConFacturaHandler(
        AlmacenDbContext db,
        IComprasOcReadPort ocPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard,
        IPeriodoContableReadPort periodoContable, IConversionUnidadPort conversion,
        ICxpDocumentosReadPort cxpDocumentos, IProveedorReadPort proveedores,
        IArticuloReadPort? articulos = null, IVarianteVidrioReadPort? variantes = null)
    {
        _articulos = articulos;
        _variantes = variantes;
        _db = db;
        _ocPort = ocPort;
        _events = events;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
        _periodoContable = periodoContable;
        _conversion = conversion;
        _cxpDocumentos = cxpDocumentos;
        _proveedores = proveedores;
    }

    public async Task<RegistrarRecepcionResponse> Handle(
        RegistrarRecepcionConFacturaCommand request, CancellationToken cancellationToken)
    {
        if (request.CfdiRecibidoId is null && string.IsNullOrWhiteSpace(request.CfdiUuidFiscal))
            throw new BusinessRuleException("RECEPCION_SIN_CFDI", "La recepción con factura requiere el CFDI: vincula el CFDI recibido o captura su folio fiscal (UUID).");
        // D5: serializa recepciones de la misma OC (advisory lock); ponytail: lock por OC, por línea si el volumen lo pide.
        await using var transaccion = await DevolucionesProveedor.DevolucionOrigenLock.AbrirAsync(_db, request.OrdenCompraId, cancellationToken);
        var oc = await _ocPort.ObtenerAsync(request.OrdenCompraId, cancellationToken);
        var recibidoPropio = await RecibidoPorLineaOc.CalcularAsync(_db, _conversion, oc, cancellationToken);
        // Primero valida identidad/estado; después aplica el techo a cantidades equivalentes.
        await RecepcionOcGuard.ValidarAsync(oc, request.Lineas.Select(l => string.IsNullOrWhiteSpace(l.UnidadCapturada) ? l : l with { Cantidad = 0m }).ToList(), recibidoPropio, cancellationToken);
        // El cierre se rechaza antes de consultar equivalencias o ubicaciones (P1/P7).
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "RECEPCION_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado (cross-cutting §6.1).
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, _periodoContable, cancellationToken);

        var conversiones = new List<ConversionUnidad>();
        // F1-MP-01: la medida se valida contra el producto A+W vigente del artículo en cada recepción.
        var variantes = new List<VarianteVidrioLectura?>();
        var lotes = new List<string?>();
        foreach (var input in request.Lineas)
        {
            var lineaOc = oc!.Lineas.Single(l => l.LineaId == input.LineaOcId);
            // E1: la medida la fija la línea de la OC; quien recibe no la elige.
            var variante = await RecepcionVariante.ResolverAsync(_articulos, _variantes, input.ArticuloId, lineaOc.ProductoAwVarianteId, cancellationToken);
            variantes.Add(variante);
            lotes.Add(RecepcionVariante.ExigirLote(variante, input.Lote));
            conversiones.Add(await RecepcionVariante.ConvertirAsync(_conversion, input.ArticuloId, input.Cantidad,
                input.UnidadCapturada, lineaOc.UnidadMedida!, variante, cancellationToken));
        }
        var equivalentes = request.Lineas.Select((l, i) => l with { Cantidad = conversiones[i].CantidadDocumento }).ToList();
        var validada = await RecepcionOcGuard.ValidarAsync(oc, equivalentes, recibidoPropio, cancellationToken);
        var lineasOc = validada.Lineas;
        var importacion = ImportacionRecepcionGuard.Crear(oc!, request.Importacion, request.Lineas);

        // 3. Construir el movimiento (Borrador → AgregarLineas → Registrar).
        //    El sub-almacén ya no viene en cabecera: se deriva del bin de cada
        //    línea (ver el guard en el loop) y el chequeo de existencia lo cubre
        //    la FK del bin.
        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select((l, i) => new CantidadAValidar(l.ArticuloId, conversiones[i].CantidadBase, Derivada: variantes[i] is not null)),
            cancellationToken);

        var movimientoId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movimientoId,
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento,
            ubicacionHelperId: request.UbicacionHelperId);

        movimiento.VincularRecepcionVarianteA(
            ocId: request.OrdenCompraId,
            ocLineaId: null, // La referencia obligatoria se conserva en cada línea.
            cfdiRecibidoId: request.CfdiRecibidoId,
            cfdiUuidFiscal: request.CfdiUuidFiscal);

        if (!string.IsNullOrWhiteSpace(request.Observaciones))
        {
            // El campo `comentario_libre` queda en el movimiento;
            // alternativamente podría ser un campo separado, pero
            // alineado al §5.1 va aquí.
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!
                .SetValue(movimiento, request.Observaciones);
        }

        // 4. Costo de OC + agregar líneas.
        var posicion = 1;
        var posicionRevision = 1;
        var payloadLineas = new List<LineaRecepcionPayload>(request.Lineas.Count);
        var subsDerivados = new HashSet<Guid>();
        // ALM-04: lo dañado va a un segundo movimiento EntradaCompra en MAT-REV (el trigger prohíbe dos sub-almacenes en uno).
        MovimientoInventario? movimientoRevision = null;
        (Guid SubAlmacenRevisionId, Guid UbicacionRevisionId)? rackRevision = null;
        for (var idx = 0; idx < request.Lineas.Count; idx++)
        {
            var input = request.Lineas[idx];
            var afOrigen = lineasOc[input.LineaOcId!.Value];
            if (afOrigen.EsActivoFijo)
            {
                var cantidadActivo = conversiones[idx].CantidadDocumento;
                if (cantidadActivo != decimal.Truncate(cantidadActivo) || input.CantidadDanada != 0 || afOrigen.ClaseActivoId is null)
                    throw new BusinessRuleException("RECEPCION_ACTIVO_INVALIDO", "Reciba unidades enteras de activos con clase, sin cantidad dañada.");
                var afId = Guid.CreateVersion7();
                _db.RecepcionesActivos.Add(new RecepcionActivo(afId, empresaId, movimientoId, request.OrdenCompraId, afOrigen.LineaId, cantidadActivo));
                payloadLineas.Add(new LineaRecepcionPayload(afId, afOrigen.LineaId, input.ArticuloId, afOrigen.UnidadMedida, cantidadActivo,
                    afOrigen.PrecioUnitarioMxn, decimal.Round(cantidadActivo * afOrigen.PrecioUnitarioMxn, 2), Guid.Empty,
                    CantidadRecibidaAcumulada: validada.RecibidoPorLinea[afOrigen.LineaId] + equivalentes.Where(e => e.LineaOcId == afOrigen.LineaId).Sum(e => e.Cantidad),
                    EsActivoFijo: true, ClaseActivoId: afOrigen.ClaseActivoId, CentroCostoId: afOrigen.CentroCostoId));
                continue;
            }
            // C7.2b: bin real obligatorio + asignación activa + no ÚNICA. Sin
            // sub de cabecera: el guard DERIVA el sub del bin y lo retorna.
            if (input.UbicacionId is null || input.UbicacionId == Guid.Empty)
                throw new BusinessRuleException("RECEPCION_UBICACION_REQUERIDA", "La línea de inventario requiere ubicación (rack o almacén general).");
            var subLinea = await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
                _db, input.UbicacionId!.Value, input.ArticuloId, cancellationToken, ligadoAw: variantes[idx] is not null);
            subsDerivados.Add(subLinea);

            var origen = lineasOc[input.LineaOcId!.Value];
            var (costo, um, lineaOcId) = (origen.PrecioUnitarioMxn, origen.UnidadMedida, origen.LineaId);

            var conversion = conversiones[idx];
            var acumulado = validada.RecibidoPorLinea[lineaOcId] + equivalentes.Where(e => e.LineaOcId == lineaOcId).Sum(e => e.Cantidad);
            var convDanada = input.CantidadDanada > 0
                ? await RecepcionVariante.ConvertirAsync(_conversion, input.ArticuloId, input.CantidadDanada, input.UnidadCapturada,
                    oc!.Lineas.Single(l => l.LineaId == input.LineaOcId).UnidadMedida!, variantes[idx], cancellationToken)
                : null;
            var baseBuena = conversion.CantidadBase - (convDanada?.CantidadBase ?? 0m);

            if (baseBuena > 0)
            {
                var lineaId = Guid.CreateVersion7();
                var linea = new LineaMovimiento(
                    id: lineaId,
                    movimientoId: movimientoId,
                    posicion: posicion++,
                    articuloId: input.ArticuloId,
                    cantidad: baseBuena,
                    unidadMedida: conversion.UnidadBase,
                    costoUnitarioMxn: costo / conversion.FactorDocumentoABase,
                    centroCostoId: origen.CentroCostoId,
                    ubicacionReferencia: input.UbicacionReferencia,
                    comentarioLinea: input.Comentario,
                    ubicacionId: input.UbicacionId,
                    lineaOcId: lineaOcId,
                    productoAwVarianteId: variantes[idx]?.VarianteId, anchoMm: variantes[idx]?.AnchoMm, altoMm: variantes[idx]?.AltoMm, lote: lotes[idx]);
                if (convDanada is null) linea.AsentarCaptura(conversion.CantidadCapturada, conversion.UnidadCapturada);
                else linea.AsentarCaptura(input.Cantidad - input.CantidadDanada, conversion.UnidadCapturada);
                movimiento.AgregarLinea(linea);

                var cantidadBuenaDoc = conversion.CantidadDocumento - (convDanada?.CantidadDocumento ?? 0m);
                payloadLineas.Add(new LineaRecepcionPayload(
                    LineaRecepcionId: lineaId,
                    LineaOcId: lineaOcId,
                    ArticuloId: input.ArticuloId,
                    UnidadMedida: um,
                    Cantidad: cantidadBuenaDoc,
                    CostoUnitarioMxn: costo,
                    MontoTotalMxn: Math.Round(cantidadBuenaDoc * costo, 2),
                    UbicacionId: input.UbicacionId!.Value,
                    SubAlmacenId: subLinea,
                    CantidadRecibidaAcumulada: acumulado));
            }

            if (convDanada is not null)
            {
                rackRevision ??= await UbicacionRevisionResolver.ResolverAsync(_db, subLinea, cancellationToken);
                if (movimientoRevision is null)
                {
                    movimientoRevision = new MovimientoInventario(
                        id: Guid.CreateVersion7(),
                        tipo: TipoMovimiento.EntradaCompra,
                        empresaId: empresaId,
                        fechaMovimiento: request.FechaMovimiento);
                    movimientoRevision.VincularRecepcionVarianteA(request.OrdenCompraId, null, request.CfdiRecibidoId, request.CfdiUuidFiscal);
                }
                var lineaRevId = Guid.CreateVersion7();
                var lineaRev = new LineaMovimiento(
                    id: lineaRevId,
                    movimientoId: movimientoRevision.Id,
                    posicion: posicionRevision++,
                    articuloId: input.ArticuloId,
                    cantidad: convDanada.CantidadBase,
                    unidadMedida: conversion.UnidadBase,
                    costoUnitarioMxn: costo / conversion.FactorDocumentoABase,
                    centroCostoId: origen.CentroCostoId,
                    comentarioLinea: input.MotivoDano,
                    ubicacionId: rackRevision.Value.UbicacionRevisionId,
                    lineaOcId: lineaOcId,
                    productoAwVarianteId: variantes[idx]?.VarianteId, anchoMm: variantes[idx]?.AnchoMm, altoMm: variantes[idx]?.AltoMm, lote: lotes[idx]);
                lineaRev.AsentarCaptura(input.CantidadDanada, conversion.UnidadCapturada);
                movimientoRevision.AgregarLinea(lineaRev);

                payloadLineas.Add(new LineaRecepcionPayload(
                    LineaRecepcionId: lineaRevId,
                    LineaOcId: lineaOcId,
                    ArticuloId: input.ArticuloId,
                    UnidadMedida: um,
                    Cantidad: convDanada.CantidadDocumento,
                    CostoUnitarioMxn: costo,
                    MontoTotalMxn: Math.Round(convDanada.CantidadDocumento * costo, 2),
                    UbicacionId: rackRevision.Value.UbicacionRevisionId,
                    SubAlmacenId: rackRevision.Value.SubAlmacenRevisionId,
                    CantidadRecibidaAcumulada: acumulado));
            }
        }

        // F3: el CFDI vinculado debe existir en CxP, estar por procesar como ingreso y ser del RFC del proveedor de la OC.
        if (request.CfdiRecibidoId is Guid cfdiId)
        {
            var cfdi = await _cxpDocumentos.ObtenerCfdiAsync(cfdiId, cancellationToken);
            if (cfdi is null)
                throw new BusinessRuleException("RECEPCION_CFDI_NO_ENCONTRADO", "El CFDI seleccionado no existe en el repositorio de CxP; vuelve a elegirlo o captura el folio fiscal.");
            // 86e3tabdp: el CFDI de una recepción cancelada ya es pasivo en CxP; sigue vinculable a la recepción correcta de la misma OC
            // mientras su factura no tenga recepción y ninguna recepción vigente lo use.
            var libreTrasCancelacion = cfdi.Estado == "ConvertidoEnPasivo" && cfdi.OcDeFacturaSinRecepcionId == request.OrdenCompraId
                && !await _db.Movimientos.AnyAsync(m => m.Tipo == TipoMovimiento.EntradaCompra && m.CfdiRecibidoId == cfdiId
                    && m.Estado == EstadoMovimiento.Registrado, cancellationToken);
            if ((cfdi.Estado != "PorProcesar" && !libreTrasCancelacion) || cfdi.Tipo != "Ingreso")
                throw new BusinessRuleException("RECEPCION_CFDI_NO_VINCULABLE", $"El CFDI está en estado {cfdi.Estado} y es de tipo {cfdi.Tipo}; solo se vinculan CFDI de ingreso por procesar.");
            var rfcProveedor = (await _proveedores.ObtenerAsync(oc!.ProveedorId, cancellationToken))?.Rfc?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(rfcProveedor) || !string.Equals(rfcProveedor, cfdi.RfcEmisor.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("RECEPCION_CFDI_PROVEEDOR_DISTINTO", $"El CFDI es del RFC {cfdi.RfcEmisor} y el proveedor de la orden de compra tiene RFC {(string.IsNullOrEmpty(rfcProveedor) ? "sin registrar" : rfcProveedor)}; vincula un CFDI del proveedor.");
        }

        if (payloadLineas.All(x => x.EsActivoFijo))
        {
            if (importacion is not null) { importacion.VincularRecepcion(movimientoId); _db.RecepcionesImportacion.Add(importacion); }
            // Sin bin no hay almacén que comparar: la sucursal destino de la OC la verifica el endpoint (alcance del usuario sobre la OC o permiso de todas las sucursales).
            var folioActivo = await Catalogo.FolioMovimientoLock.SiguienteEntradaCompraAsync(_db, request.FechaMovimiento.Year, cancellationToken);
            foreach (var ra in _db.RecepcionesActivos.Local.Where(x => x.RecepcionId == movimientoId)) ra.Folio = folioActivo;
            await _events.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(empresaId, DateTimeOffset.UtcNow,
                movimientoId, folioActivo, request.OrdenCompraId, request.FechaMovimiento, false, request.CfdiRecibidoId, request.CfdiUuidFiscal,
                request.Observaciones, payloadLineas, SucursalId: oc!.SucursalDestinoId), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaccion is not null) await transaccion.CommitAsync(cancellationToken);
            return new RegistrarRecepcionResponse(movimientoId, folioActivo);
        }

        // Invariante 'un movimiento = un sub-almacén': antes lo garantizaba el
        // sub de cabecera; ahora se deriva del bin de cada línea y se compara.
        // El trigger MOVIMIENTO_MULTI_SUBALMACEN es el backstop (23514 genérico);
        // acá lo adelantamos con un mensaje legible.
        if (subsDerivados.Count > 1)
            throw new BusinessRuleException(
                "RECEPCION_MULTI_SUBALMACEN",
                "Todas las líneas de la recepción deben ir al mismo sub-almacén; hay bins de sub-almacenes distintos.");

        // G1.6: almacén/sucursal del único sub-almacén derivado (una consulta).
        var (almacenId, sucursalId) = await Catalogo.AlmacenSucursalResolver
            .ResolverAsync(_db, subsDerivados.Single(), cancellationToken);
        Catalogo.SucursalDestinoGuard.Validar(oc!.SucursalDestinoId, sucursalId, "RECEPCION_SUCURSAL_DISTINTA", "La orden de compra está dirigida a otra sucursal: recibe la mercancía en un almacén de la sucursal destino de la orden.");

        // ALM-04: principal efectivo = el de lo disponible; si todo llegó dañado, el de revisión.
        var principal = movimiento.Lineas.Count > 0 ? movimiento : movimientoRevision!;
        if (importacion is not null) { importacion.VincularRecepcion(principal.Id); _db.RecepcionesImportacion.Add(importacion); }
        foreach (var recepcionActivo in _db.RecepcionesActivos.Local.Where(x => x.RecepcionId == movimientoId))
            recepcionActivo.VincularRecepcion(principal.Id);
        if (movimientoRevision is not null && ReferenceEquals(principal, movimiento))
            movimientoRevision.VincularRecepcionPrincipal(movimiento.Id);
        else if (!string.IsNullOrWhiteSpace(request.Observaciones))
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!.SetValue(principal, request.Observaciones);

        // 5. Reservar folio por movimiento con líneas (lock por prefijo/año antes de leer la secuencia, M-094; la transacción ya está abierta).
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "RECEPCION_SIN_USUARIO", "Se requiere usuario autenticado.");
        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.EntradaCompra.PrefijoFolio();
        _ = await Catalogo.FolioMovimientoLock.AbrirAsync(_db, prefijo, anio, cancellationToken);
        var secuencia = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        // Una sola lectura de la secuencia para ambos movimientos (la fila nueva aún no está en BD).
        foreach (var m in new[] { movimiento, movimientoRevision }.Where(m => m is not null && m.Lineas.Count > 0))
        {
            // 6. Pasar a Registrado (firma + sello + folio).
            m!.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, anio, secuencia.Incrementar()), registradoPor);
            _db.Movimientos.Add(m);
        }
        foreach (var ra in _db.RecepcionesActivos.Local.Where(x => x.RecepcionId == principal.Id)) ra.Folio = principal.Folio;

        // 7. Publicar evento al outbox (interceptor lo persiste en la misma TX).
        await _events.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            RecepcionId: principal.Id,
            FolioRecepcion: principal.Folio!,
            OrdenCompraId: request.OrdenCompraId,
            FechaMovimiento: request.FechaMovimiento,
            FacturaPendiente: false, // Variante A — factura/CFDI ya conocido.
            CfdiRecibidoId: request.CfdiRecibidoId,
            CfdiUuidFiscal: movimiento.CfdiUuidFiscal,
            Observaciones: request.Observaciones,
            Lineas: payloadLineas,
            AlmacenId: almacenId,
            SucursalId: sucursalId), cancellationToken);

        // 8. SaveChanges: dispara el trigger PG que actualiza saldos +
        //    el interceptor del outbox que persiste el evento. Todo en
        //    la misma TX (atomicidad).
        await _db.SaveChangesAsync(cancellationToken);
        if (transaccion is not null) await transaccion.CommitAsync(cancellationToken);

        return new RegistrarRecepcionResponse(principal.Id, principal.Folio!);
    }
}
