using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;

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
/// </summary>
public sealed class ListarPendientesAutorizacionHandler
    : IRequestHandler<ListarPendientesAutorizacionQuery, PagedResponse<RequisicionListItemResponse>>
{
    private const int LimiteMaximo = 200;

    private readonly ComprasDbContext _db;
    private readonly IUsuarioReadPort _usuarios;
    private readonly IDepartamentoReadPort _departamentos;

    public ListarPendientesAutorizacionHandler(
        ComprasDbContext db,
        IUsuarioReadPort usuarios,
        IDepartamentoReadPort departamentos)
    {
        _db = db;
        _usuarios = usuarios;
        _departamentos = departamentos;
    }

    public async Task<PagedResponse<RequisicionListItemResponse>> Handle(
        ListarPendientesAutorizacionQuery query,
        CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, LimiteMaximo);

        IQueryable<Requisicion> q = _db.Requisiciones
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
            q = nivel == NivelAutorizacion.Nivel1
                ? q.Where(r =>
                    !r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1))
                : q.Where(r =>
                    r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1)
                    && !r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel2));
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

        return new PagedResponse<RequisicionListItemResponse>(enriquecidos, offset, limit, total);
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
