using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Handler de <see cref="ListarRequisicionesQuery"/>. Compone la query
/// con filtros opcionales y devuelve una página + total. La proyección
/// LINQ a <see cref="RequisicionListItemResponse"/> evita materializar
/// la entidad completa.
///
/// <para>
/// Tras la query, enriquece la página con los nombres de requisitante y
/// departamento (ADR-0042) vía <see cref="RequisicionNombres"/>.
/// </para>
/// </summary>
public sealed class ListarRequisicionesHandler
    : IRequestHandler<ListarRequisicionesQuery, PagedResponse<RequisicionListItemResponse>>
{
    private const int LimiteMaximo = 200;

    private readonly ComprasDbContext _db;
    private readonly IUsuarioReadPort _usuarios;
    private readonly IDepartamentoReadPort _departamentos;

    public ListarRequisicionesHandler(
        ComprasDbContext db,
        IUsuarioReadPort usuarios,
        IDepartamentoReadPort departamentos)
    {
        _db = db;
        _usuarios = usuarios;
        _departamentos = departamentos;
    }

    public async Task<PagedResponse<RequisicionListItemResponse>> Handle(
        ListarRequisicionesQuery query,
        CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit <= 0 ? 50 : Math.Min(query.Limit, LimiteMaximo);

        IQueryable<Requisicion> q = _db.Requisiciones.AsNoTracking();

        if (query.Estado is EstadoRequisicion estado)
        {
            q = q.Where(r => r.Estado == estado);
        }
        if (query.DepartamentoId is Guid departamentoId)
        {
            q = q.Where(r => r.DepartamentoId == departamentoId);
        }
        if (query.RequisitanteId is Guid requisitanteId)
        {
            q = q.Where(r => r.RequisitanteId == requisitanteId);
        }

        var total = await q.CountAsync(cancellationToken);

        // Proyección a un intermedio con las sumas por RQ calculadas EN SQL
        // (ADR-0043). El clamp por línea se expresa con condicional → EF lo
        // traduce a SUM(CASE WHEN ... THEN ... ELSE 0 END) (no Math.Max → sin
        // client-eval). Se acota a EnSurtido: en otros estados no se agregan
        // líneas (CASE WHEN estado=EnSurtido) y la función devuelve null.
        var filas = await q
            .OrderByDescending(r => r.FechaSolicitud)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(limit)
            .Select(r => new FilaConSumas(
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
                r.Estado == EstadoRequisicion.EnSurtido
                    ? r.Lineas.Sum(l => l.CantidadEntregada)
                    : 0m,
                r.Estado == EstadoRequisicion.EnSurtido
                    ? r.Lineas.Sum(l =>
                        l.CantidadDeAlmacen + l.CantidadRecibida - l.CantidadEntregada > 0m
                            ? l.CantidadDeAlmacen + l.CantidadRecibida - l.CantidadEntregada
                            : 0m)
                    : 0m,
                r.Estado == EstadoRequisicion.EnSurtido
                    ? r.Lineas.Sum(l => l.Cantidad)
                    : 0m))
            .ToListAsync(cancellationToken);

        // Mapeo en memoria a la respuesta: la situación la deriva la MISMA
        // función pura que el detalle (fuente única, sin drift). Nombres en null
        // aquí; los llena Enriquecer (ADR-0042).
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
                SituacionSurtido: SituacionSurtidoDerivacion.Derivar(
                    f.Estado, f.SumEntregado, f.SumPendienteEntregar, f.SumTotal),
                Origen: f.Origen))
            .ToList();

        var enriquecidos = await RequisicionNombres.EnriquecerAsync(
            items, _usuarios, _departamentos, cancellationToken);

        return new PagedResponse<RequisicionListItemResponse>(enriquecidos, offset, limit, total);
    }
}

/// <summary>
/// Intermedio de la proyección del list-query: cabecera + las 3 sumas por RQ
/// calculadas en SQL (ADR-0043), antes de derivar la situación en memoria con
/// <see cref="SituacionSurtidoDerivacion"/>.
/// </summary>
internal sealed record FilaConSumas(
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
    decimal SumEntregado,
    decimal SumPendienteEntregar,
    decimal SumTotal);
