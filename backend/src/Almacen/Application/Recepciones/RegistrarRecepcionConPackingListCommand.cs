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
        });
    }
}

public sealed class RegistrarRecepcionConPackingListHandler
    : IRequestHandler<RegistrarRecepcionConPackingListCommand, RegistrarRecepcionResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IArticuloReadPort _articuloPort;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IPeriodoContableReadPort _periodoContable;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IConversionUnidadPort _conversion;

    public RegistrarRecepcionConPackingListHandler(
        AlmacenDbContext db,
        IComprasOcReadPort ocPort,
        IArticuloReadPort articuloPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard,
        IPeriodoContableReadPort periodoContable, IConversionUnidadPort conversion)
    {
        _db = db;
        _ocPort = ocPort;
        _articuloPort = articuloPort;
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
        // 1. Validar OC.
        var oc = await _ocPort.ObtenerAsync(request.OrdenCompraId, cancellationToken);
        // Primero valida identidad/estado; después aplica el techo a cantidades equivalentes.
        await RecepcionOcGuard.ValidarAsync(oc, request.Lineas.Select(l => string.IsNullOrWhiteSpace(l.UnidadCapturada) ? l : l with { Cantidad = 0m }).ToList(), _articuloPort, cancellationToken);
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
        var lineasOc = await RecepcionOcGuard.ValidarAsync(oc, equivalentes, _articuloPort, cancellationToken);

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
        var payloadLineas = new List<LineaRecepcionPayload>(request.Lineas.Count);
        var subsDerivados = new HashSet<Guid>();
        foreach (var input in request.Lineas)
        {
            // C7.2b: bin real obligatorio + asignación activa + no ÚNICA. Sin
            // sub de cabecera: el guard DERIVA el sub del bin y lo retorna.
            var subLinea = await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(
                _db, input.UbicacionId!.Value, input.ArticuloId, cancellationToken);
            subsDerivados.Add(subLinea);

            var origen = lineasOc[input.LineaOcId!.Value];
            var (costo, um, lineaOcId) = (origen.PrecioUnitarioMxn, origen.UnidadMedida, origen.LineaId);

            var conversion = conversiones[posicion - 1];
            var lineaId = Guid.CreateVersion7();
            var linea = new LineaMovimiento(
                id: lineaId,
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: input.ArticuloId,
                cantidad: conversion.CantidadBase,
                unidadMedida: conversion.UnidadBase,
                costoUnitarioMxn: costo / conversion.FactorDocumentoABase,
                centroCostoId: origen.CentroCostoId,
                ubicacionReferencia: input.UbicacionReferencia,
                comentarioLinea: input.Comentario,
                ubicacionId: input.UbicacionId,
                lineaOcId: lineaOcId);
            linea.AsentarCaptura(conversion.CantidadCapturada, conversion.UnidadCapturada);
            movimiento.AgregarLinea(linea);

            payloadLineas.Add(new LineaRecepcionPayload(
                LineaRecepcionId: lineaId,
                LineaOcId: lineaOcId,
                ArticuloId: input.ArticuloId,
                UnidadMedida: um,
                Cantidad: conversion.CantidadDocumento,
                CostoUnitarioMxn: costo,
                MontoTotalMxn: Math.Round(conversion.CantidadDocumento * costo, 2),
                // PR4: bin N4 real de la línea (el guard ya garantizó que viene
                // poblado y es válido para el sub-almacén).
                UbicacionId: input.UbicacionId!.Value,
                SubAlmacenId: subLinea));
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

        // 5. Reservar folio.
        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.EntradaCompra.PrefijoFolio();
        var secuencia = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        var siguiente = secuencia.Incrementar();
        var folio = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, anio, siguiente);

        // 6. Registrar.
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "RECEPCION_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        // 7. Evento con FacturaPendiente=true (variante B).
        await _events.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            RecepcionId: movimientoId,
            FolioRecepcion: folio.Valor,
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

        return new RegistrarRecepcionResponse(movimientoId, folio.Valor);
    }

    private async Task<(decimal costoMxn, string unidadMedida, Guid? lineaOcId)>
        ResolverCostoYUmAsync(
            OcLectura? oc,
            RegistrarRecepcionLineaInput input,
            CancellationToken cancellationToken)
    {
        if (oc is not null && input.LineaOcId is Guid lineaOcId)
        {
            var lineaOc = oc.Lineas.FirstOrDefault(l => l.LineaId == lineaOcId)
                ?? throw new EntityNotFoundException(
                    "LINEA_OC_NO_ENCONTRADA",
                    $"No existe línea '{lineaOcId}' en la OC '{oc.Folio}'.");
            if (lineaOc.ArticuloId != input.ArticuloId)
            {
                throw new BusinessRuleException(
                    "RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE",
                    "El artículo de la línea recibida no coincide con la línea de OC.");
            }
            var maxTolerable = lineaOc.CantidadSolicitada - lineaOc.CantidadRecibida;
            var articulo = await _articuloPort.ObtenerAsync(input.ArticuloId, cancellationToken);
            var tolerancia = articulo?.ToleranciaCantidadPorcentaje ?? 0m;
            maxTolerable += lineaOc.CantidadSolicitada * (tolerancia / 100m);
            if (input.Cantidad > maxTolerable)
            {
                throw new BusinessRuleException(
                    "RECEPCION_EXCEDE_TOLERANCIA",
                    $"Cantidad recibida {input.Cantidad} excede el saldo+tolerancia ({maxTolerable}) de la línea OC '{lineaOcId}'.");
            }
            return (lineaOc.PrecioUnitarioMxn, lineaOc.UnidadMedida, lineaOcId);
        }

        if (oc is not null)
        {
            var lineaOc = oc.Lineas.FirstOrDefault(l => l.ArticuloId == input.ArticuloId);
            if (lineaOc is not null)
            {
                return (lineaOc.PrecioUnitarioMxn, lineaOc.UnidadMedida, lineaOc.LineaId);
            }

            // CA2.10: si la OC es conocida, no se permite recibir un artículo que no esté en sus líneas recibibles (p. ej. servicios).
            throw new BusinessRuleException(
                "RECEPCION_ARTICULO_NO_EN_OC",
                $"El artículo '{input.ArticuloId}' no pertenece a las líneas recibibles de la OC '{oc.Folio}'.");
        }

        var articulo2 = await _articuloPort.ObtenerAsync(input.ArticuloId, cancellationToken);
        return (0m, articulo2?.UnidadMedida ?? "PZA", null);
    }
}
