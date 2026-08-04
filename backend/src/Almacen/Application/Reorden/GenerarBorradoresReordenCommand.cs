using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Reorden;

// ============================================================================
// Orquestación del motor de reorden (ADR-0047 PR5.D). Barre configs activas con
// faltante, mapea cada una a (sucursal, almacén), agrupa por almacén y crea UNA
// RQ de sistema por almacén (con N líneas). El worker (ReordenWorker) invoca este
// command por IMediator (testeable sin timer). Aislamiento POR CONFIG: un fallo
// de mapeo o creación reporta esa config/almacén y el barrido sigue.
//
// Dedup por faltante-neto: EvaluarReorden (5.B) ya resta lo vivo de sistema, y el
// Borrador creado cuenta como vivo el ciclo siguiente → no re-crea. No hay chequeo
// booleano de existencia (se acepta un duplicado raro que el humano cancela).
// ============================================================================

public sealed record GenerarBorradoresReordenCommand : IRequest<GenerarBorradoresReordenResultado>;

public sealed record GenerarBorradoresReordenResultado(
    int ConfigsConFaltante,
    int RqsCreadas,
    int LineasCreadas,
    IReadOnlyList<ReordenConfigError> Errores);

public sealed record ReordenConfigError(
    Guid ConfiguracionId, Guid ArticuloId, string Codigo, string Mensaje);

public sealed class GenerarBorradoresReordenHandler
    : IRequestHandler<GenerarBorradoresReordenCommand, GenerarBorradoresReordenResultado>
{
    private readonly IMediator _mediator;
    private readonly AlmacenDbContext _db;
    private readonly IComprasCrearRqSistemaPort _crearRq;
    private readonly ILogger<GenerarBorradoresReordenHandler> _logger;

    public GenerarBorradoresReordenHandler(
        IMediator mediator,
        AlmacenDbContext db,
        IComprasCrearRqSistemaPort crearRq,
        ILogger<GenerarBorradoresReordenHandler> logger)
    {
        _mediator = mediator;
        _db = db;
        _crearRq = crearRq;
        _logger = logger;
    }

    private readonly record struct MapeoLinea(
        Guid SucursalId, Guid AlmacenId, Guid ArticuloId, decimal Cantidad, Guid ConfiguracionId);

    public async Task<GenerarBorradoresReordenResultado> Handle(
        GenerarBorradoresReordenCommand request, CancellationToken cancellationToken)
    {
        // 1. Faltante por config (5.B); filtra las que disparan y tienen hueco.
        var faltantes = await _mediator.Send(new EvaluarReordenQuery(), cancellationToken);
        var candidatos = faltantes.Where(f => f.AutoRequisicion && f.Faltante > 0m).ToList();

        var errores = new List<ReordenConfigError>();
        if (candidatos.Count == 0)
            return new GenerarBorradoresReordenResultado(0, 0, 0, errores);

        // Almacenes activos (dato local de Almacén): por id (N2) y por sucursal (N1).
        var almacenes = await _db.Almacenes.AsNoTracking()
            .Where(a => a.Estatus == EstatusCatalogo.Activo)
            .Select(a => new { a.Id, a.SucursalId })
            .ToListAsync(cancellationToken);
        var sucursalPorAlmacen = almacenes.ToDictionary(a => a.Id, a => a.SucursalId);
        var almacenesPorSucursal = almacenes
            .GroupBy(a => a.SucursalId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        // 2. Mapeo config→(sucursal, almacén) con aislamiento por config.
        var mapeados = new List<MapeoLinea>();
        foreach (var f in candidatos)
        {
            try
            {
                var (sucursalId, almacenId) = ResolverDestino(f, sucursalPorAlmacen, almacenesPorSucursal);
                mapeados.Add(new MapeoLinea(sucursalId, almacenId, f.ArticuloId, f.Faltante, f.ConfiguracionId));
            }
            catch (BusinessRuleException ex)
            {
                _logger.LogWarning(
                    "Reorden: config {ConfigId} (articulo {Articulo}) omitida — {Codigo}: {Mensaje}",
                    f.ConfiguracionId, f.ArticuloId, ex.Code, ex.Message);
                errores.Add(new ReordenConfigError(f.ConfiguracionId, f.ArticuloId, ex.Code, ex.Message));
            }
        }

        // 3. Agrupa por almacén → 1 RQ con N líneas. Aislamiento por grupo.
        var rqsCreadas = 0;
        var lineasCreadas = 0;
        foreach (var grupo in mapeados.GroupBy(m => (m.SucursalId, m.AlmacenId)))
        {
            var lineasGrupo = grupo.ToList();
            var lineas = lineasGrupo
                .Select(m => new LineaRqSistema(m.ArticuloId, m.Cantidad))
                .ToList();
            try
            {
                await _crearRq.CrearBorradorSistemaAsync(
                    new CrearRqSistemaSolicitud(grupo.Key.SucursalId, grupo.Key.AlmacenId, lineas),
                    cancellationToken);
                rqsCreadas++;
                lineasCreadas += lineas.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Reorden: falló crear RQ para almacén {Almacen} ({Lineas} líneas)",
                    grupo.Key.AlmacenId, lineas.Count);
                foreach (var m in lineasGrupo)
                    errores.Add(new ReordenConfigError(
                        m.ConfiguracionId, m.ArticuloId, "REORDEN_CREACION_FALLIDA", ex.Message));
            }
        }

        _logger.LogInformation(
            "Reorden: {Candidatos} configs con faltante → {Rqs} RQs ({Lineas} líneas), {Errores} errores.",
            candidatos.Count, rqsCreadas, lineasCreadas, errores.Count);

        return new GenerarBorradoresReordenResultado(candidatos.Count, rqsCreadas, lineasCreadas, errores);
    }

    /// <summary>
    /// Mapea (Nivel, EntidadId) al almacén concreto de la RQ. N2: el almacén es la
    /// entidad; la sucursal se resuelve por Almacen.SucursalId. N1: el ÚNICO almacén
    /// de la sucursal (falla ruidoso si 0 o 2+).
    /// </summary>
    private static (Guid SucursalId, Guid AlmacenId) ResolverDestino(
        FaltanteReorden f,
        Dictionary<Guid, Guid> sucursalPorAlmacen,
        Dictionary<Guid, List<Guid>> almacenesPorSucursal)
    {
        if (f.Nivel == NivelReorden.Almacen)
        {
            if (!sucursalPorAlmacen.TryGetValue(f.EntidadId, out var sucursalId))
                throw new BusinessRuleException("REORDEN_ALMACEN_NO_ENCONTRADO",
                    $"El almacén '{f.EntidadId}' de la config N2 no existe o está inactivo.");
            return (sucursalId, f.EntidadId);
        }

        // N1 (Sucursal): el único almacén de la sucursal.
        var alms = almacenesPorSucursal.GetValueOrDefault(f.EntidadId) ?? [];
        if (alms.Count == 0)
            throw new BusinessRuleException("REORDEN_SUCURSAL_SIN_ALMACEN",
                $"La sucursal '{f.EntidadId}' de la config N1 no tiene almacén activo.");
        if (alms.Count > 1)
            throw new BusinessRuleException("REORDEN_SUCURSAL_MULTI_ALMACEN",
                $"La sucursal '{f.EntidadId}' tiene {alms.Count} almacenes activos; " +
                "una config N1 exige uno solo (error de configuración).");
        return (f.EntidadId, alms[0]);
    }
}
