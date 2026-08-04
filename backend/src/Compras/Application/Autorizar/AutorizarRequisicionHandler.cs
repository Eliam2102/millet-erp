using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Serilog.Context;

namespace Millet.Compras.Application.Autorizar;

/// <summary>
/// Handler de <see cref="AutorizarRequisicionCommand"/>. Implementa el
/// flujo completo de bifurcación stock-aware:
/// <list type="number">
///   <item>Carga la requisición con líneas y autorizaciones existentes.</item>
///   <item>Invoca el evaluator de matriz v0 (<see cref="IRequiereNivelEvaluator"/>)
///         para saber si requiere solo N1 o N1+N2.</item>
///   <item>Llama <c>RegistrarAutorizacion</c> en el agregado, que valida
///         estado, secuencia y unicidad. Si la matriz queda satisfecha,
///         transiciona a <c>Autorizada</c> y devuelve
///         <see cref="Domain.Events.MatrizAprobacionSatisfechaEvent"/>.</item>
///   <item><b>F4-PR1</b>: si quedó <c>Autorizada</c>, consulta
///         <see cref="IConsultarStockPort"/> por línea y aplica
///         <c>RegistrarCubrimiento</c> (transiciona a <c>Cerrada</c> /
///         <c>EnSurtido</c>).</item>
///   <item><b>F4-PR2 / PR4</b>: en la <b>misma transacción EF</b>, invoca
///         <see cref="IGenerarSolicitudCompraPort"/> con el saldo si hay
///         líneas con <c>CantidadDeCompra &gt; 0</c> (ADR-0047: las RQ ya
///         NO reservan stock; la porción de almacén queda como stock libre).
///         Si el puerto lanza, la TX se descarta y el agregado regresa a
///         <c>EnAutorizacion</c>. La excepción se propaga como
///         <see cref="BusinessRuleException"/> con código
///         <c>BIFURCACION_FALLO</c> → 422.</item>
/// </list>
///
/// El user actual viene del JWT (<see cref="ICurrentUserContext"/>), no
/// del request — evita suplantación.
/// </summary>
public sealed class AutorizarRequisicionHandler : IRequestHandler<AutorizarRequisicionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IRequiereNivelEvaluator _evaluator;
    private readonly IConsultarStockPort _stock;
    private readonly IGenerarSolicitudCompraPort _ocPort;
    private readonly IClock _clock;

    public AutorizarRequisicionHandler(
        ComprasDbContext db,
        IMediator mediator,
        ICurrentUserContext currentUser,
        IRequiereNivelEvaluator evaluator,
        IConsultarStockPort stock,
        IGenerarSolicitudCompraPort ocPort,
        IClock clock)
    {
        _db = db;
        _mediator = mediator;
        _currentUser = currentUser;
        _evaluator = evaluator;
        _stock = stock;
        _ocPort = ocPort;
        _clock = clock;
    }

    public async Task<Unit> Handle(AutorizarRequisicionCommand command, CancellationToken cancellationToken)
    {
        // F8-PR2: RequisicionId visible en todo log dentro del handler;
        // el span de OpenTelemetry replica el id como tag para que
        // Application Insights pueda correlacionarlo con traces.
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.Autorizar");
        activity?.SetTag("compras.requisicion.id", command.RequisicionId);
        activity?.SetTag("compras.autorizacion.nivel", command.Nivel.ToString());

        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .Include(r => r.Autorizaciones)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        activity?.SetTag("compras.estado.antes", requisicion.Estado.ToString());

        var requiereNivel = await _evaluator.EvaluarAsync(requisicion, cancellationToken);

        var resultado = requisicion.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: command.Nivel,
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            requiereNivel: requiereNivel,
            notas: command.Notas);

        // Si la matriz NO está satisfecha (falta N2): solo persiste la
        // autorización + posible cambio de estado (sigue en EnAutorizacion).
        // Sin TX explícita, sin puertos cross-module, sin eventos.
        if (resultado.MatrizSatisfecha is null)
        {
            await _db.SaveChangesAsync(cancellationToken);
            activity?.SetTag("compras.estado.despues", requisicion.Estado.ToString());
            return Unit.Value;
        }

        // Matriz cumplida: bifurcación stock-aware en una sola TX EF.
        // Si cualquier puerto lanza, el using-dispose de la TX rollbackea
        // la autorización + cubrimiento. El agregado en memoria queda
        // mutado, pero EF no persistió y el siguiente request recarga
        // desde BD el estado original (EnAutorizacion).
        try
        {
            await EjecutarBifurcacionAsync(requisicion, resultado.MatrizSatisfecha, cancellationToken);
        }
        catch (Exception ex) when (ex is not BusinessRuleException && ex is not OperationCanceledException)
        {
            // Excepción runtime de un puerto (ej. Service Bus down,
            // Almacén caído). Traduce a 422 con código estable y
            // propaga el detalle en el mensaje para diagnostics.
            throw new BusinessRuleException(
                "BIFURCACION_FALLO",
                $"La bifurcación stock-aware falló: {ex.Message}",
                ex);
        }

        activity?.SetTag("compras.estado.despues", requisicion.Estado.ToString());
        return Unit.Value;
    }

    /// <summary>
    /// Ejecuta cubrimiento + reservas + movimiento + OC borrador en una
    /// sola transacción EF. F6-PR3: publica los domain events
    /// (MatrizSatisfecha, CubrimientoRegistrado, posible Cerrada) ANTES
    /// del SaveChanges final, para que los integration mappers
    /// agreguen filas a outbox dentro de la misma TX (atomicidad).
    /// </summary>
    private async Task EjecutarBifurcacionAsync(
        Requisicion requisicion,
        Domain.Events.MatrizAprobacionSatisfechaEvent matrizEvento,
        CancellationToken cancellationToken)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Calcular cubrimiento por línea consultando stock.
        var cubrimientos = await CalcularCubrimientosAsync(requisicion, cancellationToken);
        var cubrimientoResultado = requisicion.RegistrarCubrimiento(cubrimientos, _clock.UtcNow);

        // 2. (PR4 / ADR-0047) Las requisiciones YA NO reservan inventario. La
        // porción cubierta por almacén (CantidadDeAlmacen) queda como stock
        // libre; el surtido decrementa el físico al entregar. El faltante
        // (CantidadDeCompra) va a OC (paso 3).

        // 3. OC borrador con líneas de saldo (si hay y el setting está ON).
        //
        // Setting `Compras.Settings.AutoGenerarOcAlAutorizar`:
        // - true  → genera OC borrador síncrono (Narrativa A del diseño
        //           RQ §A3 original, hoy stubbed por
        //           InMemoryGenerarSolicitudCompraPort hasta el
        //           PLATFORM-TODO(<StubsTeardown>)).
        // - false → no genera nada. La RQ queda en EnSurtido con
        //           ComprometidaEnOcId=null. El comprador convierte
        //           manualmente vía POST /api/v1/compras/ordenes/desde-requisicion
        //           (1:1) o vía Sheet "Nueva OC" consolidación N:1.
        //           Es la narrativa B del diseño OC §3.bis.1.
        //
        // Default false; el owner cambia el flag por empresa vía
        // PATCH /api/v1/compras/configuracion.
        var saldo = requisicion.Lineas
            .Where(l => l.CantidadDeCompra > 0)
            .Select(l => new LineaSaldo(
                LineaRequisicionId: l.Id,
                ArticuloId: l.ArticuloId,
                CantidadSaldo: l.CantidadDeCompra,
                UnidadMedida: l.UnidadMedida,
                PrecioEstimado: l.PrecioEstimado,
                CuentaContableId: l.CuentaContableId,
                CentroCostoId: l.CentroCostoId,
                Proyecto: l.Proyecto))
            .ToList();

        if (saldo.Count > 0)
        {
            var autoGenerarOc = await _db.ComprasSettings
                .AsNoTracking()
                .Where(s => s.EmpresaId == requisicion.EmpresaId)
                .Select(s => (bool?)s.AutoGenerarOcAlAutorizar)
                .FirstOrDefaultAsync(cancellationToken) ?? false;

            if (autoGenerarOc)
            {
                _ = await _ocPort.GenerarBorradorAsync(requisicion.Id, saldo, cancellationToken);
            }
        }

        // 4. F6-PR3: publicar domain events ANTES del SaveChanges final
        // para que los integration mappers encolen al outbox buffer y
        // el OutboxSaveChangesInterceptor inserte las filas dentro de
        // la misma TX (atomicidad).
        await _mediator.Publish(matrizEvento, cancellationToken);
        await _mediator.Publish(cubrimientoResultado.CubrimientoEvento, cancellationToken);
        if (cubrimientoResultado.CerradaEvento is not null)
        {
            await _mediator.Publish(cubrimientoResultado.CerradaEvento, cancellationToken);
        }

        // 5. SaveChanges persiste autorización + cubrimiento + reservaIds
        // + drena el outbox buffer creando las filas integration_events_outbox.
        // Si el stub de OC ya hizo un SaveChanges interno, este SaveChanges
        // flushea solo lo nuevo (estado, mutaciones de líneas, outbox rows).
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CubrimientoLinea>> CalcularCubrimientosAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken)
    {
        var resultado = new List<CubrimientoLinea>(requisicion.Lineas.Count);
        foreach (var linea in requisicion.Lineas)
        {
            var disponibilidad = await _stock.ConsultarPorSucursalAsync(
                requisicion.SucursalId,
                linea.ArticuloId,
                cancellationToken);

            // Reparto conservador (todo el disponible hasta el tope; el resto
            // es saldo para OC) vía la función pura del dominio — MISMA pieza
            // que usa el preview read-only (PR-C), sin drift.
            var (cantidadDeAlmacen, cantidadDeCompra) =
                Cubrimiento.Repartir(disponibilidad.Disponible, linea.Cantidad);

            resultado.Add(new CubrimientoLinea(linea.Id, cantidadDeAlmacen, cantidadDeCompra));
        }
        return resultado;
    }
}
