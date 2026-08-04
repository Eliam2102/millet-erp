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
/// Tolerancia por material (A5): F2-PR2 valida que la cantidad recibida
/// no exceda la cantidad solicitada en la OC más el porcentaje de
/// tolerancia del artículo. Como <see cref="IArticuloReadPort"/> es
/// NoOp en F0-PR1 (devuelve null para cualquier id), el handler
/// trata "sin master" como tolerancia 0% (igualdad estricta vs OC).
/// Cuando el adapter real entre, leerá la tolerancia configurada.
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
    Guid? UbicacionId = null);

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
        // Vínculo fiscal obligatorio en variante A (§5.4): CFDI del
        // repositorio o folio fiscal capturado del impreso.
        RuleFor(c => c.CfdiRecibidoId)
            .NotNull()
            .When(c => string.IsNullOrWhiteSpace(c.CfdiUuidFiscal))
            .WithMessage(
                "La recepción con factura requiere el CFDI: vincula el CFDI recibido o captura su folio fiscal (UUID).");
        RuleFor(c => c.CfdiUuidFiscal!)
            .Matches(@"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")
            .When(c => !string.IsNullOrWhiteSpace(c.CfdiUuidFiscal))
            .WithMessage("El folio fiscal no cumple el formato UUID del SAT (8-4-4-4-12 hex).");
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ArticuloId).NotEqual(Guid.Empty);
            linea.RuleFor(l => l.Cantidad).GreaterThan(0);
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
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public RegistrarRecepcionConFacturaHandler(
        AlmacenDbContext db,
        IComprasOcReadPort ocPort,
        IArticuloReadPort articuloPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _ocPort = ocPort;
        _articuloPort = articuloPort;
        _events = events;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<RegistrarRecepcionResponse> Handle(
        RegistrarRecepcionConFacturaCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar OC: existe + autorizada/parcial + no cancelada.
        //    Stub NoOp en F0-PR1 devuelve null — el handler tratará la
        //    ausencia de validación cross-módulo como aceptable hasta
        //    que el adapter real entre. Cuando entre, rechazará si la
        //    OC no está autorizada.
        var oc = await _ocPort.ObtenerAsync(request.OrdenCompraId, cancellationToken);
        if (oc is not null)
        {
            if (!string.Equals(oc.Estado, "Autorizada", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(oc.Estado, "Recibida", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException(
                    "RECEPCION_OC_NO_AUTORIZADA",
                    $"La OC '{oc.Folio}' está en estado '{oc.Estado}'; no acepta recepción.");
            }
        }

        // 3. Construir el movimiento (Borrador → AgregarLineas → Registrar).
        //    El sub-almacén ya no viene en cabecera: se deriva del bin de cada
        //    línea (ver el guard en el loop) y el chequeo de existencia lo cubre
        //    la FK del bin.
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "RECEPCION_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado (cross-cutting §6.1).
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, cancellationToken);

        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select(l => new CantidadAValidar(l.ArticuloId, l.Cantidad)),
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
            ocLineaId: null, // F2-PR2: lineaOcId opcional, se infiere si OC presente.
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

            var (costo, um, lineaOcId) = await ResolverCostoYUmAsync(oc, input, cancellationToken);

            var lineaId = Guid.CreateVersion7();
            var linea = new LineaMovimiento(
                id: lineaId,
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: input.ArticuloId,
                cantidad: input.Cantidad,
                unidadMedida: um,
                costoUnitarioMxn: costo,
                ubicacionReferencia: input.UbicacionReferencia,
                comentarioLinea: input.Comentario,
                ubicacionId: input.UbicacionId);
            movimiento.AgregarLinea(linea);

            payloadLineas.Add(new LineaRecepcionPayload(
                LineaRecepcionId: lineaId,
                LineaOcId: lineaOcId,
                ArticuloId: input.ArticuloId,
                UnidadMedida: um,
                Cantidad: input.Cantidad,
                CostoUnitarioMxn: costo,
                MontoTotalMxn: Math.Round(input.Cantidad * costo, 2),
                // PR4: bin N4 real de la línea (el guard de arriba ya garantizó
                // que viene poblado y es válido para el sub-almacén).
                UbicacionId: input.UbicacionId!.Value));
        }

        // Invariante 'un movimiento = un sub-almacén': antes lo garantizaba el
        // sub de cabecera; ahora se deriva del bin de cada línea y se compara.
        // El trigger MOVIMIENTO_MULTI_SUBALMACEN es el backstop (23514 genérico);
        // acá lo adelantamos con un mensaje legible.
        if (subsDerivados.Count > 1)
            throw new BusinessRuleException(
                "RECEPCION_MULTI_SUBALMACEN",
                "Todas las líneas de la recepción deben ir al mismo sub-almacén; hay bins de sub-almacenes distintos.");

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
            Lineas: payloadLineas), cancellationToken);

        // 8. SaveChanges: dispara el trigger PG que actualiza saldos +
        //    el interceptor del outbox que persiste el evento. Todo en
        //    la misma TX (atomicidad).
        await _db.SaveChangesAsync(cancellationToken);

        return new RegistrarRecepcionResponse(movimientoId, folio.Valor);
    }

    private async Task<(decimal costoMxn, string unidadMedida, Guid? lineaOcId)>
        ResolverCostoYUmAsync(
            OcLectura? oc,
            RegistrarRecepcionLineaInput input,
            CancellationToken cancellationToken)
    {
        // 1) Si el caller especificó LineaOcId y la OC es conocida, usar esa línea.
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
            // Tolerancia (A5): articulo readport es NoOp → 0%.
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

        // 2) Si la OC es conocida pero sin LineaOcId, intentar match por artículo.
        if (oc is not null)
        {
            var lineaOc = oc.Lineas.FirstOrDefault(l => l.ArticuloId == input.ArticuloId);
            if (lineaOc is not null)
            {
                return (lineaOc.PrecioUnitarioMxn, lineaOc.UnidadMedida, lineaOc.LineaId);
            }
        }

        // 3) Fallback (OC stub NoOp): leer UM del artículo si existe; costo = 0.
        var articulo2 = await _articuloPort.ObtenerAsync(input.ArticuloId, cancellationToken);
        return (0m, articulo2?.UnidadMedida ?? "PZA", null);
    }
}
