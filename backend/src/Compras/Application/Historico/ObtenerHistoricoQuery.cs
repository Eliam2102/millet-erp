using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compras.Application.Historico;

/// <summary>
/// Histórico de auditoría de una requisición específica (B.2). Lee
/// <c>core.audit_log</c> filtrando por <c>aggregate_root_id =
/// requisicionId</c>: trae cambios del root + de líneas + de
/// autorizaciones en una sola query (gracias a la enrichment del
/// interceptor introducida en B.2).
///
/// <para>
/// Permiso: lo decide el endpoint
/// (<c>compras.requisiciones.leer</c>). Sin paginación —
/// las RQs típicas tienen &lt; 50 transiciones; el cap defensivo es
/// 500 para que un caso patológico no traiga miles de filas.
/// </para>
/// </summary>
public sealed record ObtenerHistoricoQuery(Guid RequisicionId)
    : IRequest<IReadOnlyList<HistoricoEntryResponse>>;

public sealed record HistoricoEntryResponse(
    HistoricoTipo Tipo,
    string Operacion,
    string Entidad,
    Guid? EntidadId,
    Guid? ActorId,
    // Nombre del actor resuelto en backend (ADR-0042). null si la entrada
    // no tiene actor (sistema/automática → el FE muestra "Sistema") o si el
    // actor no se resolvió (service principal / usuario borrado → el FE cae
    // al id).
    string? ActorNombre,
    DateTimeOffset Timestamp,
    string Cambios,
    Guid CorrelationId);

public sealed class ObtenerHistoricoHandler
    : IRequestHandler<ObtenerHistoricoQuery, IReadOnlyList<HistoricoEntryResponse>>
{
    private const int MaxFilas = 500;

    private readonly CoreDbContext _coreDb;
    private readonly Infrastructure.ComprasDbContext _comprasDb;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IUsuarioReadPort _usuarios;

    public ObtenerHistoricoHandler(
        CoreDbContext coreDb,
        Infrastructure.ComprasDbContext comprasDb,
        ICurrentEmpresaContext currentEmpresa,
        IUsuarioReadPort usuarios)
    {
        _coreDb = coreDb;
        _comprasDb = comprasDb;
        _currentEmpresa = currentEmpresa;
        _usuarios = usuarios;
    }

    public async Task<IReadOnlyList<HistoricoEntryResponse>> Handle(
        ObtenerHistoricoQuery request, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // Verificación de pertenencia: la RQ debe existir en la empresa
        // actual. El global query filter de Compras protege contra
        // cross-tenant. Si no existe → 404 antes de tocar audit_log.
        var existe = await _comprasDb.Requisiciones.AsNoTracking()
            .AnyAsync(r => r.Id == request.RequisicionId, cancellationToken);
        if (!existe)
        {
            throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{request.RequisicionId}' en la empresa actual.");
        }

        // Histórico: filtramos por (Modulo='Compras', AggregateRootId=id).
        // La empresa_id la fija el interceptor al insertar; verificamos
        // explícitamente para evitar fugas cross-tenant en caso de
        // bypass mal usado en migrations o seeds.
        var entries = await _coreDb.AuditLog.AsNoTracking()
            .Where(a =>
                a.Modulo == "Compras"
                && a.AggregateRootId == request.RequisicionId
                && (a.EmpresaId == null || a.EmpresaId == empresaId))
            .OrderBy(a => a.Timestamp)
            .Take(MaxFilas)
            .ToListAsync(cancellationToken);

        // Enrichment cross-módulo (ADR-0042): resuelve actorId → nombre en
        // una sola consulta batch (el resto del Select es in-memory tras el
        // ToListAsync). Entradas sin actor (UsuarioId null) o no resueltas
        // quedan con ActorNombre null; el FE muestra "Sistema" / id.
        var actorIds = entries
            .Where(e => e.UsuarioId is Guid)
            .Select(e => e.UsuarioId!.Value)
            .Distinct()
            .ToArray();
        var nombres = await _usuarios.ObtenerNombresAsync(actorIds, cancellationToken);

        return entries
            .Select(e => new HistoricoEntryResponse(
                Tipo: HistoricoTipoMapper.InferirTipo(e),
                Operacion: e.Operacion,
                Entidad: e.Entidad,
                EntidadId: e.EntidadId,
                ActorId: e.UsuarioId,
                ActorNombre: e.UsuarioId is Guid uid ? nombres.GetValueOrDefault(uid) : null,
                Timestamp: e.Timestamp,
                Cambios: e.Cambios,
                CorrelationId: e.CorrelationId))
            .ToList();
    }
}
