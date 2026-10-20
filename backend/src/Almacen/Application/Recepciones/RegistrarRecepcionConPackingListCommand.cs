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
/// F3-PR1: comando que registra una <b>recepción Variante B</b>
/// (con packing list, sin factura todavía — materiales directos
/// no-vidrio: interlayer, silicones, pinturas). Costo de OC. Marca el
/// movimiento con <c>factura_pendiente=true</c> y persiste el blob ref
/// del packing list adjunto. Publica <c>OcRecepcionRegistradaEvent</c>
/// con <c>FacturaPendiente=true</c> para que CxP sepa que falta factura.
///
/// <para>
/// Diferencias clave vs Variante A (<see cref="RegistrarRecepcionConFacturaCommand"/>):
/// <list type="bullet">
///   <item>No vincula <c>cfdiRecibidoId</c> ni <c>facturaId</c>.</item>
///   <item>Persiste <c>packing_list_blob_ref</c> (referencia al adjunto).</item>
///   <item>Marca <c>FacturaPendiente=true</c> en el evento.</item>
///   <item>Cuando llegue la factura (CxP publica
///   <c>FacturaProveedorRegistradaEvent</c>), el listener
///   <c>FacturaProveedorRegistradaListener</c> matchea por OC y vincula
///   la factura a la recepción pendiente.</item>
/// </list>
/// </para>
/// <para>
/// Pendiente exacto (86e3tab1k, sin tolerancia en piezas): límite = solicitada − máx(recibido en Compras, recibido propio de Almacén), con lock por OC.
/// </para>
/// </summary>
public sealed record RegistrarRecepcionConPackingListCommand(
    Guid OrdenCompraId,
    DateOnly FechaMovimiento,
    string PackingListBlobRef,
    string? Observaciones,
    IReadOnlyList<RegistrarRecepcionLineaInput> Lineas,
    // PR4: helper de cabecera (bin N4 que el almacenista auto-aplicó a las
    // líneas vacías en el sheet). Opcional — null = no usó el helper. Solo
    // reportería; la ubicación efectiva de cada línea viaja en cada
    // RegistrarRecepcionLineaInput.UbicacionId y es la que manda.
    Guid? UbicacionHelperId = null) : IRequest<RegistrarRecepcionResponse>;

public sealed class RegistrarRecepcionConPackingListValidator
    : AbstractValidator<RegistrarRecepcionConPackingListCommand>
{
    public RegistrarRecepcionConPackingListValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099)
            .WithMessage("Fecha de movimiento fuera de rango razonable.");
        RuleFor(c => c.PackingListBlobRef).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Observaciones!).MaximumLength(500)
            .When(c => c.Observaciones is not null)
            .WithMessage("Las observaciones no pueden exceder 500 caracteres.");
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ArticuloId).NotEqual(Guid.Empty);
            linea.RuleFor(l => l.Cantidad).GreaterThan(0);
            linea.RuleFor(l => l.UnidadCapturada).MaximumLength(20);
            linea.RuleFor(l => l.UbicacionId)
                .NotNull().NotEqual(Guid.Empty)
                .WithMessage("La ubicación (rack) es obligatoria en la entrada.");
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
        });
    }
}

public sealed class RegistrarRecepcionConPackingListHandler
    : IRequestHandler<RegistrarRecepcionConPackingListCommand, RegistrarRecepcionResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IPeriodoContableReadPort _periodoContable;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IConversionUnidadPort _conversion;

    public RegistrarRecepcionConPackingListHandler(
        AlmacenDbContext db,
        IComprasOcReadPort ocPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard,
        IPeriodoContableReadPort periodoContable, IConversionUnidadPort conversion)
    {
        _db = db;
        _ocPort = ocPort;
        _events = events;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
        _periodoContable = periodoContable;
        _conversion = conversion;
    }

    public async Task<RegistrarRecepcionResponse> Handle(
        RegistrarRecepcionConPackingListCommand request, CancellationToken cancellationToken)
    {
        // D5: serializa recepciones de la misma OC (advisory lock); ponytail: lock por OC, por línea si el volumen lo pide.
        await using var transaccion = await DevolucionesProveedor.DevolucionOrigenLock.AbrirAsync(_db, request.OrdenCompraId, cancellationToken);
        // 1. Validar OC.
        var oc = await _ocPort.ObtenerAsync(request.OrdenCompraId, cancellationToken);
        var recibidoPropio = await RecibidoPorLineaOc.CalcularAsync(_db, _conversion, oc, cancellationToken);
        // Primero valida identidad/estado; después aplica el techo a cantidades equivalentes.
        await RecepcionOcGuard.ValidarAsync(oc, request.Lineas.Select(l => string.IsNullOrWhiteSpace(l.UnidadCapturada) ? l : l with { Cantidad = 0m }).ToList(), recibidoPropio, cancellationToken);
        // El cierre se rechaza antes de consultar equivalencias o ubicaciones (P1/P7).
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "RECEPCION_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado.
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, _periodoContable, cancellationToken);

        var conversiones = new List<ConversionUnidad>();
        foreach (var input in request.Lineas)
            conversiones.Add(await _conversion.ConvertirAsync(input.ArticuloId, input.Cantidad,
                input.UnidadCapturada, oc!.Lineas.Single(l => l.LineaId == input.LineaOcId).UnidadMedida!, cancellationToken));
        var equivalentes = request.Lineas.Select((l, i) => l with { Cantidad = conversiones[i].CantidadDocumento }).ToList();
        var validada = await RecepcionOcGuard.ValidarAsync(oc, equivalentes, recibidoPropio, cancellationToken);
        var lineasOc = validada.Lineas;

        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select((l, i) => new CantidadAValidar(l.ArticuloId, conversiones[i].CantidadBase)),
            cancellationToken);

        var movimientoId = Guid.CreateVersion7();
        var movimiento = new MovimientoInventario(
            id: movimientoId,
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento,
            ubicacionHelperId: request.UbicacionHelperId);

        movimiento.VincularRecepcionVarianteB(
            ocId: request.OrdenCompraId,
            ocLineaId: null,
            packingListBlobRef: request.PackingListBlobRef);

        if (!string.IsNullOrWhiteSpace(request.Observaciones))
        {
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!
                .SetValue(movimiento, request.Observaciones);
        }

        // 4. Líneas con costo de OC.
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
            // C7.2b: bin real obligatorio + asignación activa + no ÚNICA. Sin
            // sub de cabecera: el guard DERIVA el sub del bin y lo retorna.
            var subLinea = await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
                _db, input.UbicacionId!.Value, input.ArticuloId, cancellationToken);
            subsDerivados.Add(subLinea);

            var origen = lineasOc[input.LineaOcId!.Value];
            var (costo, um, lineaOcId) = (origen.PrecioUnitarioMxn, origen.UnidadMedida, origen.LineaId);

            var conversion = conversiones[idx];
            var acumulado = validada.RecibidoPorLinea[lineaOcId] + equivalentes.Where(e => e.LineaOcId == lineaOcId).Sum(e => e.Cantidad);
            var convDanada = input.CantidadDanada > 0
                ? await _conversion.ConvertirAsync(input.ArticuloId, input.CantidadDanada, input.UnidadCapturada,
                    oc!.Lineas.Single(l => l.LineaId == input.LineaOcId).UnidadMedida!, cancellationToken)
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
                    lineaOcId: lineaOcId);
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
                    movimientoRevision.VincularRecepcionVarianteB(request.OrdenCompraId, null, request.PackingListBlobRef);
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
                    lineaOcId: lineaOcId);
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

        // Invariante 'un movimiento = un sub-almacén': el sub se deriva del bin
        // de cada línea y se compara. El trigger MOVIMIENTO_MULTI_SUBALMACEN es
        // el backstop (23514 genérico); acá lo adelantamos con mensaje legible.
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

        // 7. Evento con FacturaPendiente=true (variante B).
        await _events.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            RecepcionId: principal.Id,
            FolioRecepcion: principal.Folio!,
            OrdenCompraId: request.OrdenCompraId,
            FechaMovimiento: request.FechaMovimiento,
            FacturaPendiente: true, // Variante B — falta la factura.
            CfdiRecibidoId: null,
            CfdiUuidFiscal: null,
            Observaciones: request.Observaciones,
            Lineas: payloadLineas,
            AlmacenId: almacenId,
            SucursalId: sucursalId), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        if (transaccion is not null) await transaccion.CommitAsync(cancellationToken);

        return new RegistrarRecepcionResponse(principal.Id, principal.Folio!);
    }
}
