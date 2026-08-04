using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Salidas;

/// <summary>
/// F4-PR1: comando que registra una <b>salida normal con RQ</b>
/// (Variante A — el flujo principal). Valida RQ contra
/// <see cref="IComprasRequisicionReadPort"/>, calcula costo unitario
/// como el costo promedio ponderado vigente (snapshot al momento de la
/// salida, A9), genera movimiento tipo <c>SalidaConsumo</c> en estado
/// Registrado (el trigger PG valida y decrementa el saldo), reserva
/// folio, y publica <c>SalidaRequisicionRegistradaEvent</c> al outbox.
///
/// <para>
/// <b>Consumo de reserva (A19)</b>: si la RQ tiene reserva activa
/// matching (DocumentoOrigenTipo='Requisicion', DocumentoOrigenId=RqId,
/// estado=Activa), el handler consume la reserva. Esto:
/// <list type="number">
///   <item>Marca <c>ReservaStock</c> como Consumida con
///   <c>movimiento_consumo_id</c>.</item>
///   <item>Decrementa <c>cantidad_reservada</c> en el saldo (la línea
///   del trigger PG decrementa <c>cantidad</c>; aquí restamos
///   solo <c>cantidad_reservada</c>).</item>
/// </list>
/// Si NO hay reserva activa, salida directa: el trigger PG decrementa
/// <c>cantidad</c> con validación de saldo suficiente.
/// </para>
/// </summary>
// Salida-por-línea C2: el sub-almacén ya NO viaja en la cabecera. Se DERIVA del
// bin (Ubicacion.SubAlmacenId) de las líneas, que ahora es obligatorio. El
// almacenista elige el bin y con eso queda determinado el sub y el CPP a congelar.
public sealed record RegistrarSalidaConRequisicionCommand(
    Guid RequisicionId,
    DateOnly FechaMovimiento,
    Guid? PersonaDestinatariaId,
    string? Observaciones,
    IReadOnlyList<RegistrarSalidaLineaInput> Lineas) : IRequest<RegistrarSalidaResponse>;

public sealed record RegistrarSalidaLineaInput(
    Guid ArticuloId,
    Guid? LineaRqId,
    decimal Cantidad,
    Guid? CentroCostoId,
    Guid? ProyectoId,
    string? UbicacionReferencia,
    string? Comentario,
    // C7.2b: bin real de salida (rack con saldo, o la ÚNICA para agotar
    // histórico). El tipo se mantiene nullable porque lo comparte el vale
    // (variante B); en salida-con-RQ (variante A) el validator lo exige.
    Guid? UbicacionId = null);

public sealed record RegistrarSalidaResponse(
    Guid SalidaId,
    string Folio);

public sealed class RegistrarSalidaConRequisicionValidator
    : AbstractValidator<RegistrarSalidaConRequisicionCommand>
{
    public RegistrarSalidaConRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEqual(Guid.Empty);
        RuleFor(c => c.FechaMovimiento)
            .Must(f => f.Year is >= 2020 and <= 2099)
            .WithMessage("Fecha de movimiento fuera de rango razonable.");
        RuleFor(c => c.Lineas).NotEmpty();
        RuleForEach(c => c.Lineas).ChildRules(linea =>
        {
            linea.RuleFor(l => l.ArticuloId).NotEqual(Guid.Empty);
            linea.RuleFor(l => l.Cantidad).GreaterThan(0);
            // Salida-por-línea C2: el bin es obligatorio — el almacenista elige
            // de dónde sale (y con eso se deriva el sub y se congela el CPP).
            linea.RuleFor(l => l.UbicacionId)
                .NotNull()
                .WithMessage("Elige la ubicación (bin) de donde sale la línea.");
            linea.RuleFor(l => l.UbicacionReferencia!).MaximumLength(100)
                .When(l => l.UbicacionReferencia is not null);
            linea.RuleFor(l => l.Comentario!).MaximumLength(500)
                .When(l => l.Comentario is not null);
        });
    }
}

public sealed class RegistrarSalidaConRequisicionHandler
    : IRequestHandler<RegistrarSalidaConRequisicionCommand, RegistrarSalidaResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasRequisicionReadPort _rqPort;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public RegistrarSalidaConRequisicionHandler(
        AlmacenDbContext db,
        IComprasRequisicionReadPort rqPort,
        IIntegrationEventPublisher events,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _rqPort = rqPort;
        _events = events;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<RegistrarSalidaResponse> Handle(
        RegistrarSalidaConRequisicionCommand request, CancellationToken cancellationToken)
    {
        // 1. Validar RQ. Stub NoOp acepta cualquier id en F0-PR1; el adapter
        //    real (ComprasRequisicionReadAdapter) ya gatea por estado y devuelve
        //    null salvo {Autorizada, EnSurtido}, así que si llega no-null la RQ
        //    acepta surtido.
        var rq = await _rqPort.ObtenerAsync(request.RequisicionId, cancellationToken);
        if (rq is not null)
        {
            // ADR-0043 #3 (conmutación): el único estado de surtido vivo es
            // EnSurtido (la autorización + cubrimiento siempre desemboca ahí;
            // tras #3 también el caso 100% stock). Los strings viejos "Aprobada"
            // y "ParcialmenteSurtida" no existían en EstadoRequisicion — eran
            // letra muerta. Se compara contra el nombre del enum (cross-módulo:
            // el puerto expone Estado como string, sin acoplar el enum de Compras).
            if (!string.Equals(rq.Estado, "EnSurtido", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException(
                    "SALIDA_RQ_NO_APROBADA",
                    $"La RQ '{rq.Folio}' está en estado '{rq.Estado}'; no acepta salida.");
            }
        }

        // 2. Salida-por-línea C2: el sub-almacén ya NO viene en la cabecera — se
        //    DERIVA del bin de cada línea. El validator exige UbicacionId por
        //    línea; acá se re-valida (defensa: el handler puede invocarse fuera
        //    del pipeline) y se deriva el sub único del movimiento. El invariante
        //    "un movimiento = un sub-almacén" lo respalda el trigger PG
        //    (MOVIMIENTO_MULTI_SUBALMACEN); acá se anticipa con error legible.
        if (request.Lineas.Any(l => l.UbicacionId is null))
        {
            throw new BusinessRuleException(
                "SALIDA_UBICACION_REQUERIDA",
                "Cada línea de salida debe indicar la ubicación (bin) de donde sale.");
        }
        var binIds = request.Lineas.Select(l => l.UbicacionId!.Value).Distinct().ToList();
        var subsDerivados = await _db.Ubicaciones.AsNoTracking()
            .Where(u => binIds.Contains(u.Id))
            .Select(u => u.SubAlmacenId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (subsDerivados.Count != 1)
        {
            throw new BusinessRuleException(
                "SALIDA_MULTI_SUBALMACEN",
                "Todas las líneas de la salida deben salir de ubicaciones del " +
                "mismo sub-almacén. Revisa los bins elegidos.");
        }
        var subAlmacenId = subsDerivados[0];

        // F7-PR3 (A18): si hay bloqueo activo de salidas para el sub DERIVADO,
        // rechazar — está en conteo anual.
        var bloqueado = await _db.Set<Domain.Conteos.BloqueoInventario>()
            .AsNoTracking()
            .AnyAsync(b => b.SubAlmacenId == subAlmacenId
                && b.Activo
                && b.BloqueaSalidas, cancellationToken);
        if (bloqueado)
        {
            throw new BusinessRuleException(
                "SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL",
                $"El sub-almacén '{subAlmacenId}' está en conteo anual; las salidas están bloqueadas hasta que se aplique o rechace.");
        }

        // 3. Costo unitario = costo promedio vigente del saldo (A9 — snapshot).
        //    Para cada línea, cargamos el saldo del par (sub_almacen, articulo)
        //    y tomamos su costo_promedio_mxn como precio congelado en la línea.
        //    Si no hay saldo, costo = 0 (defensa, el trigger validará insuficiencia).
        var movimientoId = Guid.CreateVersion7();
        var empresaId = _currentEmpresa.Current ?? throw new BusinessRuleException(
            "SALIDA_SIN_EMPRESA", "El contexto de empresa es requerido.");

        // F8-PR2: validar periodo cerrado.
        await Cierre.PeriodoCerradoValidator.LanzarSiCerradoAsync(
            _db, empresaId, request.FechaMovimiento, cancellationToken);

        // ADR-0046 Etapa 2: valida los decimales de cada línea contra la unidad
        // del artículo (FK NULL → no valida). Batch, un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            request.Lineas.Select(l => new CantidadAValidar(l.ArticuloId, l.Cantidad)),
            cancellationToken);

        var movimiento = new MovimientoInventario(
            id: movimientoId,
            tipo: TipoMovimiento.SalidaConsumo,
            empresaId: empresaId,
            fechaMovimiento: request.FechaMovimiento);

        // Vincular salida a RQ + destinatarios.
        typeof(MovimientoInventario)
            .GetMethod("VincularSalida", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(movimiento, new object?[]
            {
                request.RequisicionId,
                null, // valeBlobRef
                request.PersonaDestinatariaId,
            });

        if (!string.IsNullOrWhiteSpace(request.Observaciones))
        {
            typeof(MovimientoInventario).GetProperty("ComentarioLibre")!
                .SetValue(movimiento, request.Observaciones);
        }

        // C7.2b / salida-por-línea C2: el costo snapshot se lee de la fila de
        // saldo que esta salida va a golpear — el bin ELEGIDO en cada línea (ya
        // obligatorio; sin fallback a la ÚNICA). Solo se llavea la lectura; la
        // fórmula del promedio NO cambia (candado de regresión de costeo).
        var posicion = 1;
        var payloadLineas = new List<LineaSalidaPayload>(request.Lineas.Count);
        foreach (var input in request.Lineas)
        {
            // Salida-por-línea C2: el bin es obligatorio (validado arriba y en el
            // validator); ya no hay coalesce a la ÚNICA. Defensa por si el handler
            // se invoca fuera del pipeline. El bin resuelto es el que se persiste,
            // el que se publica y el que se costea.
            var ubicacionLinea = input.UbicacionId
                ?? throw new BusinessRuleException(
                    "SALIDA_UBICACION_REQUERIDA",
                    "Cada línea de salida debe indicar la ubicación (bin) de donde sale.");
            var saldo = await _db.SaldosInventario.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.UbicacionId == ubicacionLinea && s.ArticuloId == input.ArticuloId,
                    cancellationToken);
            var costoSnapshot = saldo?.CostoPromedioMxn ?? 0m;
            var um = rq?.Lineas.FirstOrDefault(l => l.ArticuloId == input.ArticuloId)?.UnidadMedida
                ?? "PZA";

            // Fase E PR5: el CC-Máquina de la salida-con-RQ es AUTORITATIVO del
            // backend — se hereda de la línea de RQ, NO del caller (input.CentroCostoId
            // se ignora en este camino; la línea es read-only en el FE).
            var centroCostoHeredado = ResolverCentroCostoHeredado(
                rq, input.LineaRqId, input.ArticuloId);

            var lineaId = Guid.CreateVersion7();
            var linea = new LineaMovimiento(
                id: lineaId,
                movimientoId: movimientoId,
                posicion: posicion++,
                articuloId: input.ArticuloId,
                cantidad: input.Cantidad,
                unidadMedida: um,
                costoUnitarioMxn: costoSnapshot,
                centroCostoId: centroCostoHeredado,
                proyectoId: input.ProyectoId,
                ubicacionReferencia: input.UbicacionReferencia,
                comentarioLinea: input.Comentario,
                // ADR-0043: persistir la línea de RQ surtida (antes se
                // descartaba) para que el evento la propague a Compras.
                lineaRqId: input.LineaRqId,
                ubicacionId: ubicacionLinea);
            movimiento.AgregarLinea(linea);

            payloadLineas.Add(new LineaSalidaPayload(
                LineaSalidaId: lineaId,
                ArticuloId: input.ArticuloId,
                UnidadMedida: um,
                Cantidad: input.Cantidad,
                CostoUnitarioMxn: costoSnapshot,
                MontoTotalMxn: Math.Round(input.Cantidad * costoSnapshot, 2),
                CentroCostoId: centroCostoHeredado,
                ProyectoId: input.ProyectoId,
                LineaRqId: input.LineaRqId,
                // Salida-por-línea C2: el bin elegido (obligatorio). El evento
                // refleja el destino físico real; nunca null.
                UbicacionId: ubicacionLinea));
        }

        // 4. Reservar folio.
        var anio = request.FechaMovimiento.Year;
        var prefijo = TipoMovimiento.SalidaConsumo.PrefijoFolio();
        var secuencia = await _db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, cancellationToken);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            _db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        var siguiente = secuencia.Incrementar();
        var folio = FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, anio, siguiente);

        // 5. Firmar movimiento.
        var registradoPor = _currentUser.UserId ?? throw new BusinessRuleException(
            "SALIDA_SIN_USUARIO", "Se requiere usuario autenticado.");
        movimiento.Registrar(folio, registradoPor);
        _db.Movimientos.Add(movimiento);

        // 6. (PR4 / ADR-0047) Las RQ ya no reservan stock. El trigger PG
        //    decrementa la cantidad física al INSERT de la línea; no hay
        //    cantidad_reservada que ajustar.

        // 7. Publicar evento.
        await _events.PublishAsync(new SalidaRequisicionRegistradaIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            SalidaId: movimientoId,
            FolioSalida: folio.Valor,
            FechaMovimiento: request.FechaMovimiento,
            RqId: request.RequisicionId,
            EsPorVale: false,
            PersonaDestinatariaId: request.PersonaDestinatariaId,
            ValeBlobRef: null,
            Lineas: payloadLineas), cancellationToken);

        // 8. SaveChanges — todo en la misma TX. El trigger PG valida saldo
        //    suficiente y aborta la TX si insuficiente (SALDO_INSUFICIENTE).
        await _db.SaveChangesAsync(cancellationToken);

        return new RegistrarSalidaResponse(movimientoId, folio.Valor);
    }

    /// <summary>
    /// Fase E PR5: resuelve el CC-Máquina HEREDADO de la línea de RQ para una
    /// línea de salida-con-RQ. AUTORITATIVO — ignora lo que mande el caller:
    /// por <paramref name="lineaRqId"/> si viene; si no, fallback por artículo.
    /// Devuelve null si la RQ o la línea no resuelven → el consumo muestra "—".
    /// </summary>
    internal static Guid? ResolverCentroCostoHeredado(
        RequisicionLectura? rq, Guid? lineaRqId, Guid articuloId)
    {
        if (rq is null) return null;
        var lineaRq = lineaRqId is Guid lrq
            ? rq.Lineas.FirstOrDefault(l => l.LineaId == lrq)
            : rq.Lineas.FirstOrDefault(l => l.ArticuloId == articuloId);
        return lineaRq?.CentroCostoId;
    }
}
