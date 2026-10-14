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
/// Tolerancia por material (A5): límite = solicitada × (1 + tolerancia del artículo) − máx(recibido en Compras, recibido propio de Almacén), con lock por OC.
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
    Guid? UbicacionHelperId = null) : IRequest<RegistrarRecepcionResponse>;

public sealed record RegistrarRecepcionLineaInput(
    Guid ArticuloId,
    Guid? LineaOcId,
    decimal Cantidad,
    string? UbicacionReferencia,
    string? Comentario,
    // C7.2b: bin real destino (rack N4). Obligatorio en entradas — el
    // artículo debe estar asignado y no puede ir a la ÚNICA. Nullable en el
    // tipo por compat de construcción; el validator lo exige.
    Guid? UbicacionId = null, string? UnidadCapturada = null);

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

public sealed class RegistrarRecepcionConFacturaHandler
    : IRequestHandler<RegistrarRecepcionConFacturaCommand, RegistrarRecepcionResponse>
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
    private readonly ICxpDocumentosReadPort _cxpDocumentos;
    private readonly IProveedorReadPort _proveedores;

    public RegistrarRecepcionConFacturaHandler(
        AlmacenDbContext db,
        IComprasOcReadPort ocPort,
        IArticuloReadPort articuloPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard,
        IPeriodoContableReadPort periodoContable, IConversionUnidadPort conversion,
        ICxpDocumentosReadPort cxpDocumentos, IProveedorReadPort proveedores)
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
        await RecepcionOcGuard.ValidarAsync(oc, request.Lineas.Select(l => string.IsNullOrWhiteSpace(l.UnidadCapturada) ? l : l with { Cantidad = 0m }).ToList(), _articuloPort, recibidoPropio, cancellationToken);
        // El cierre se rechaza antes de consultar equivalencias o ubicaciones (P1/P7).
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "RECEPCION_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado (cross-cutting §6.1).
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, _periodoContable, cancellationToken);

        var conversiones = new List<ConversionUnidad>();
        foreach (var input in request.Lineas)
            conversiones.Add(await _conversion.ConvertirAsync(input.ArticuloId, input.Cantidad,
                input.UnidadCapturada, oc!.Lineas.Single(l => l.LineaId == input.LineaOcId).UnidadMedida!, cancellationToken));
        var equivalentes = request.Lineas.Select((l, i) => l with { Cantidad = conversiones[i].CantidadDocumento }).ToList();
        var validada = await RecepcionOcGuard.ValidarAsync(oc, equivalentes, _articuloPort, recibidoPropio, cancellationToken);
        var lineasOc = validada.Lineas;

        // 3. Construir el movimiento (Borrador → AgregarLineas → Registrar).
        //    El sub-almacén ya no viene en cabecera: se deriva del bin de cada
        //    línea (ver el guard en el loop) y el chequeo de existencia lo cubre
        //    la FK del bin.
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
                // PR4: bin N4 real de la línea (el guard de arriba ya garantizó
                // que viene poblado y es válido para el sub-almacén).
                UbicacionId: input.UbicacionId!.Value,
                SubAlmacenId: subLinea,
                ToleranciaPorcentaje: validada.ToleranciaPorLinea[lineaOcId],
                CantidadRecibidaAcumulada: validada.RecibidoPorLinea[lineaOcId] + equivalentes.Where(e => e.LineaOcId == lineaOcId).Sum(e => e.Cantidad)));
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

        // F3: el CFDI vinculado debe existir en CxP, estar por procesar como ingreso y ser del RFC del proveedor de la OC.
        if (request.CfdiRecibidoId is Guid cfdiId)
        {
            var cfdi = await _cxpDocumentos.ObtenerCfdiAsync(cfdiId, cancellationToken);
            if (cfdi is null)
                throw new BusinessRuleException("RECEPCION_CFDI_NO_ENCONTRADO", "El CFDI seleccionado no existe en el repositorio de CxP; vuelve a elegirlo o captura el folio fiscal.");
            if (cfdi.Estado != "PorProcesar" || cfdi.Tipo != "Ingreso")
                throw new BusinessRuleException("RECEPCION_CFDI_NO_VINCULABLE", $"El CFDI está en estado {cfdi.Estado} y es de tipo {cfdi.Tipo}; solo se vinculan CFDI de ingreso por procesar.");
            var rfcProveedor = (await _proveedores.ObtenerAsync(oc!.ProveedorId, cancellationToken))?.Rfc?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(rfcProveedor) || !string.Equals(rfcProveedor, cfdi.RfcEmisor.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("RECEPCION_CFDI_PROVEEDOR_DISTINTO", $"El CFDI es del RFC {cfdi.RfcEmisor} y el proveedor de la orden de compra tiene RFC {(string.IsNullOrEmpty(rfcProveedor) ? "sin registrar" : rfcProveedor)}; vincula un CFDI del proveedor.");
        }

        // 5. Reservar folio atómicamente. El UPSERT en folio_secuencias_movimiento
        //    con SELECT ... FOR UPDATE (lo hace EF + el lock del INSERT/UPDATE)
        //    garantiza secuencias sin huecos por race condition.
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

        // 6. Pasar a Registrado (firma + sello + folio).
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "RECEPCION_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        // 7. Publicar evento al outbox (interceptor lo persiste en la misma TX).
        await _events.PublishAsync(new OcRecepcionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            RecepcionId: movimientoId,
            FolioRecepcion: folio.Valor,
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

        return new RegistrarRecepcionResponse(movimientoId, folio.Valor);
    }
}
