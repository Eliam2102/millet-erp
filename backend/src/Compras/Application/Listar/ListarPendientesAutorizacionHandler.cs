using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Disponibilidad;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Handler de <see cref="ListarPendientesAutorizacionQuery"/>. Filtra
/// por <c>Estado = EnAutorizacion</c> + departamento opcional. Orden
/// FIFO sobre <c>FechaSolicitud ASC</c>.
///
/// <para>
/// Tras la query, enriquece la página con los nombres de requisitante y
/// departamento (ADR-0042) vía <see cref="RequisicionNombres"/>.
/// </para>
///
/// <para>
/// S2: agrega por RQ <c>Disponibilidad</c> (mismo reparto que el preview de
/// cubrimiento, vía <see cref="IConsultarStockPort"/>) y <c>EsRecurrente</c>
/// (conteo SQL agrupado por requisitante y clasificación en la ventana de 60
/// días). La RQ no lleva monto: el monto se valida en la OC.
/// </para>
/// </summary>
public sealed class ListarPendientesAutorizacionHandler
    : IRequestHandler<ListarPendientesAutorizacionQuery, PagedResponse<PendienteAutorizacionItemResponse>>
{
    private const int LimiteMaximo = 200;

    private readonly ComprasDbContext _db;
    private readonly IUsuarioReadPort _usuarios;
    private readonly IDepartamentoReadPort _departamentos;
    private readonly IConsultarStockPort _stock;
    private readonly IConversionUnidadPort _conversion;
    private readonly IArticuloReadPort _articulos;
    private readonly IClock _clock;

    public ListarPendientesAutorizacionHandler(
        ComprasDbContext db,
        IUsuarioReadPort usuarios,
        IDepartamentoReadPort departamentos,
        IConsultarStockPort stock,
        IConversionUnidadPort conversion,
        IArticuloReadPort articulos,
        IClock clock)
    {
        _db = db;
        _usuarios = usuarios;
        _departamentos = departamentos;
        _stock = stock;
        _conversion = conversion;
        _articulos = articulos;
        _clock = clock;
    }

    public async Task<PagedResponse<PendienteAutorizacionItemResponse>> Handle(
        ListarPendientesAutorizacionQuery query,
        CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, LimiteMaximo);

        IQueryable<Requisicion> q = _db.Requisiciones
            .Where(x => query.SucursalesPermitidas == null || (query.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(x.SucursalId))
            .AsNoTracking()
            .Where(r => r.Estado == EstadoRequisicion.EnAutorizacion);

        if (query.DepartamentoId is Guid departamentoId)
        {
            q = q.Where(r => r.DepartamentoId == departamentoId);
        }

        // Filtro por nivel pendiente (PR-A): EXISTS/NOT EXISTS sobre
        // Autorizaciones. Va ANTES de CountAsync/Skip/Take para que tanto el
        // total como la página reflejen el filtro. Codifica la misma regla que
        // NivelPendienteDerivacion (dual-encoding WHERE/Derivar): falta N1 ⇒ no
        // hay firma N1; falta N2 ⇒ hay N1 y no hay N2.
        if (query.NivelPendiente is NivelAutorizacion nivel)
        {
            q = PendientesAutorizacionLectura.Requisiciones(q,
                nivel == NivelAutorizacion.Nivel1, nivel == NivelAutorizacion.Nivel2);
        }

        var total = await q.CountAsync(cancellationToken);

        // Proyección a un intermedio con 2 flags por RQ resueltos EN SQL: cada
        // Autorizaciones.Any(...) se traduce a un EXISTS escalar (no materializa
        // la colección). El nivel pendiente lo deriva en memoria la MISMA
        // función pura (fuente única, sin drift) — así NINGÚN constructor del DTO
        // queda dentro del árbol de expresión EF (CS0854 no aplica).
        var filas = await q
            .OrderBy(r => r.FechaSolicitud)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(limit)
            .Select(r => new FilaConFirmas(
                r.Id,
                r.Folio.Valor,
                r.FolioAnio,
                r.Estado,
                r.Clasificacion,
                r.Prioridad,
                r.SucursalId,
                r.DepartamentoId,
                r.RequisitanteId,
                r.Descripcion,
                r.FechaSolicitud,
                r.FechaEntregaDeseada,
                r.Origen,
                r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1),
                r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel2)))
            .ToListAsync(cancellationToken);

        // Mapeo en memoria: nombres en null (los llena Enriquecer, ADR-0042);
        // SituacionSurtido null (EnAutorizacion); NivelPendiente derivado por la
        // función pura a partir de los 2 flags.
        var items = filas
            .Select(f => new RequisicionListItemResponse(
                f.Id,
                f.Folio,
                f.FolioAnio,
                f.Estado,
                f.Clasificacion,
                f.Prioridad,
                f.SucursalId,
                f.DepartamentoId,
                f.RequisitanteId,
                f.Descripcion,
                f.FechaSolicitud,
                f.FechaEntregaDeseada,
                NivelPendiente: NivelPendienteDerivacion.Derivar(
                    f.Estado, f.TieneN1, f.TieneN2),
                Origen: f.Origen))
            .ToList();

        var enriquecidos = await RequisicionNombres.EnriquecerAsync(
            items, _usuarios, _departamentos, cancellationToken);

        // Líneas de la página (una query, sin N+1) y recurrencia agrupada (una query).
        var ids = enriquecidos.Select(i => i.Id).ToArray();
        var requisitantes = enriquecidos.Select(i => i.RequisitanteId).Distinct().ToArray();
        var cargadas = await _db.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(cancellationToken);
        var porId = cargadas.ToDictionary(r => r.Id);
        var articulos = await _articulos.ObtenerPorIdsAsync(
            cargadas.SelectMany(r => r.Lineas).Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);
        var recurrencias = await ContarRecientesAsync(requisitantes, cancellationToken);

        var resultado = new List<PendienteAutorizacionItemResponse>(enriquecidos.Count);
        foreach (var item in enriquecidos)
        {
            var lineas = porId.TryGetValue(item.Id, out var req)
                ? req.Lineas
                : Array.Empty<LineaRequisicion>();

            // ponytail: N+1 por línea (consulta de stock por línea dentro del loop) mientras limit<=200;
            // si hay lentitud, puerto batch de stock por sucursal (un solo viaje para la página).
            var repartos = await RepartoDisponibilidadService.RepartirAsync(
                _stock, _conversion, articulos, item.Clasificacion, item.SucursalId, lineas, cancellationToken);
            var disponibilidad = PendientesAutorizacionClasificacion.Disponibilidad(
                repartos.Select(r => (r.Linea.Cantidad, r.DeAlmacen)).ToList());
            var recurrente = PendientesAutorizacionClasificacion.EsRecurrente(
                recurrencias.GetValueOrDefault((item.RequisitanteId, item.Clasificacion)));

            resultado.Add(new PendienteAutorizacionItemResponse(
                item.Id,
                item.Folio,
                item.FolioAnio,
                item.Estado,
                item.Clasificacion,
                item.Prioridad,
                item.SucursalId,
                item.DepartamentoId,
                item.RequisitanteId,
                item.Descripcion,
                item.FechaSolicitud,
                item.FechaEntregaDeseada,
                item.RequisitanteNombre,
                item.DepartamentoNombre,
                item.DepartamentoClave,
                item.SituacionSurtido,
                item.NivelPendiente,
                item.Origen,
                disponibilidad,
                recurrente));
        }

        return new PagedResponse<PendienteAutorizacionItemResponse>(resultado, offset, limit, total);
    }

    /// <summary>
    /// Cuenta, por requisitante y clasificación, las RQ de la ventana que no
    /// están canceladas ni eliminadas. Una sola consulta agrupada en SQL.
    /// </summary>
    private async Task<IReadOnlyDictionary<(Guid RequisitanteId, Clasificacion Clasificacion), int>> ContarRecientesAsync(
        Guid[] requisitanteIds,
        CancellationToken cancellationToken)
    {
        var desde = _clock.UtcNow.AddDays(-PendientesAutorizacionClasificacion.VentanaRecurrenciaDias);

        var conteos = await _db.Requisiciones
            .AsNoTracking()
            .Where(r => requisitanteIds.Contains(r.RequisitanteId)
                && r.FechaSolicitud >= desde
                && r.Estado != EstadoRequisicion.Cancelada
                && r.Estado != EstadoRequisicion.Eliminada)
            .GroupBy(r => new { r.RequisitanteId, r.Clasificacion })
            .Select(g => new { g.Key.RequisitanteId, g.Key.Clasificacion, Total = g.Count() })
            .ToListAsync(cancellationToken);

        return conteos.ToDictionary(c => (c.RequisitanteId, c.Clasificacion), c => c.Total);
    }

}

/// <summary>
/// Intermedio de la proyección del list-query de pendientes: cabecera + los 2
/// flags de firma por RQ resueltos en SQL (EXISTS escalar sobre Autorizaciones),
/// antes de derivar el nivel pendiente en memoria con
/// <see cref="NivelPendienteDerivacion"/>.
/// </summary>
internal sealed record FilaConFirmas(
    Guid Id,
    string Folio,
    short FolioAnio,
    EstadoRequisicion Estado,
    Clasificacion Clasificacion,
    Prioridad Prioridad,
    Guid SucursalId,
    Guid DepartamentoId,
    Guid RequisitanteId,
    string? Descripcion,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    OrigenRequisicion Origen,
    bool TieneN1,
    bool TieneN2);
