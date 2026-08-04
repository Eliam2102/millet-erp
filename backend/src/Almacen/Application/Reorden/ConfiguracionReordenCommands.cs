using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Reorden;

// ============================================================================
// Commands de la configuración de reorden N1/N2 (ADR-0047 PR5.A, enmienda
// 2026-07-06). CRUD sobre almacen.configuraciones_reorden.
//
// Crear valida (en este orden): artículo activo, entidad (sucursal/almacén)
// existe+activa según nivel, la ASIGNACIÓN (PR3) existe para esa entidad, y la
// EXCLUSIÓN N1⊕N2 por sucursal (contra configs ACTIVAS). unique(articulo,nivel,
// entidad): activa → conflicto; inactiva → reactiva (re-valida exclusión).
//
// Editar solo toca min/máx/reorden/bandera/objetivo (llave inmutable) — NO
// re-corre exclusión ni asignación. Desactivar es idempotente.
// ============================================================================

public sealed record ConfiguracionReordenResponse(
    Guid Id,
    Guid ArticuloId,
    NivelReorden Nivel,
    Guid EntidadId,
    decimal Minimo,
    decimal Maximo,
    decimal PuntoReorden,
    bool AutoRequisicion,
    ObjetivoReposicion Objetivo,
    EstatusCatalogo Estatus);

internal static class ConfiguracionReordenMapper
{
    public static ConfiguracionReordenResponse ToResponse(ConfiguracionReorden c) =>
        new(c.Id, c.ArticuloId, c.Nivel, c.EntidadId, c.Minimo, c.Maximo,
            c.PuntoReorden, c.AutoRequisicion, c.Objetivo, c.Estatus);
}

// ─── Crear (o reactivar) config ───────────────────────────────────────────────

public sealed record CrearConfiguracionReordenCommand(
    Guid ArticuloId,
    NivelReorden Nivel,
    Guid EntidadId,
    decimal Minimo,
    decimal Maximo,
    decimal PuntoReorden,
    bool AutoRequisicion,
    ObjetivoReposicion Objetivo) : IRequest<ConfiguracionReordenResponse>;

public sealed class CrearConfiguracionReordenValidator
    : AbstractValidator<CrearConfiguracionReordenCommand>
{
    public CrearConfiguracionReordenValidator()
    {
        RuleFor(c => c.ArticuloId).NotEqual(Guid.Empty);
        RuleFor(c => c.EntidadId).NotEqual(Guid.Empty);
        RuleFor(c => c.Nivel).IsInEnum();
        RuleFor(c => c.Objetivo).IsInEnum();
        RuleFor(c => c.Minimo).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Maximo).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PuntoReorden).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Maximo).GreaterThanOrEqualTo(c => c.Minimo)
            .WithMessage("El máximo no puede ser menor que el mínimo.");
    }
}

public sealed class CrearConfiguracionReordenHandler
    : IRequestHandler<CrearConfiguracionReordenCommand, ConfiguracionReordenResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;
    private readonly ISucursalReadPort _sucursales;

    public CrearConfiguracionReordenHandler(
        AlmacenDbContext db, IArticuloReadPort articulos, ISucursalReadPort sucursales)
    {
        _db = db;
        _articulos = articulos;
        _sucursales = sucursales;
    }

    public async Task<ConfiguracionReordenResponse> Handle(
        CrearConfiguracionReordenCommand request, CancellationToken cancellationToken)
    {
        // 1. Artículo debe existir y estar activo (read-port cross-módulo).
        var articulo = await _articulos.ObtenerAsync(request.ArticuloId, cancellationToken);
        if (articulo is null)
            throw new EntityNotFoundException("ARTICULO_NO_ENCONTRADO",
                $"No existe artículo con id '{request.ArticuloId}'.");
        if (!articulo.EsActivo)
            throw new BusinessRuleException("ARTICULO_INACTIVO",
                $"El artículo '{articulo.Clave}' está inactivo; no se puede configurar reorden.");

        // 2. Entidad (según nivel) existe + activa. De aquí sale la sucursal en la
        //    que aplica la exclusión N1⊕N2.
        Guid sucursalScope = await ValidarEntidadYResolverSucursalAsync(
            request.Nivel, request.EntidadId, cancellationToken);

        // 3. Asignación-existe (PR3): el artículo debe estar asignado a alguna
        //    ubicación bajo la entidad (coherencia estructural; no juzga negocio).
        await ValidarAsignacionExisteAsync(
            request.ArticuloId, request.Nivel, request.EntidadId, cancellationToken);

        // 4. Exclusión N1⊕N2 por sucursal (contra configs ACTIVAS del otro nivel).
        await ValidarExclusionNivelAsync(
            request.ArticuloId, request.Nivel, sucursalScope, cancellationToken);

        // 5. unique(articulo, nivel, entidad): activa → conflicto; inactiva → reactiva.
        var existente = await _db.ConfiguracionesReorden.FirstOrDefaultAsync(
            c => c.ArticuloId == request.ArticuloId
              && c.Nivel == request.Nivel
              && c.EntidadId == request.EntidadId,
            cancellationToken);

        ConfiguracionReorden config;
        if (existente is not null)
        {
            if (existente.Estatus == EstatusCatalogo.Activo)
                throw new ConflictException("REORDEN_DUPLICADA",
                    "Ya existe una configuración de reorden activa para este artículo en esta entidad.");

            existente.EditarPolitica(
                request.Minimo, request.Maximo, request.PuntoReorden,
                request.AutoRequisicion, request.Objetivo);
            existente.CambiarEstatus(EstatusCatalogo.Activo);
            config = existente;
        }
        else
        {
            config = new ConfiguracionReorden(
                id: Guid.CreateVersion7(),
                articuloId: request.ArticuloId,
                nivel: request.Nivel,
                entidadId: request.EntidadId,
                minimo: request.Minimo,
                maximo: request.Maximo,
                puntoReorden: request.PuntoReorden,
                autoRequisicion: request.AutoRequisicion,
                objetivo: request.Objetivo);
            _db.ConfiguracionesReorden.Add(config);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ConfiguracionReordenMapper.ToResponse(config);
    }

    /// <summary>Valida existencia+actividad de la entidad y devuelve la sucursal en la que aplica la exclusión.</summary>
    private async Task<Guid> ValidarEntidadYResolverSucursalAsync(
        NivelReorden nivel, Guid entidadId, CancellationToken ct)
    {
        if (nivel == NivelReorden.Sucursal)
        {
            var sucursal = await _sucursales.ObtenerAsync(entidadId, ct);
            if (sucursal is null)
                throw new EntityNotFoundException("REORDEN_SUCURSAL_NO_ENCONTRADA",
                    $"No existe sucursal con id '{entidadId}'.");
            if (!sucursal.EsActiva)
                throw new BusinessRuleException("REORDEN_SUCURSAL_INACTIVA",
                    $"La sucursal '{sucursal.Clave}' está inactiva.");
            return entidadId;
        }

        var almacen = await _db.Almacenes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == entidadId, ct);
        if (almacen is null)
            throw new EntityNotFoundException("REORDEN_ALMACEN_NO_ENCONTRADO",
                $"No existe almacén con id '{entidadId}'.");
        if (almacen.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("REORDEN_ALMACEN_INACTIVO",
                $"El almacén '{almacen.Clave}' está inactivo.");
        return almacen.SucursalId;
    }

    private async Task ValidarAsignacionExisteAsync(
        Guid articuloId, NivelReorden nivel, Guid entidadId, CancellationToken ct)
    {
        var q =
            from a in _db.AsignacionesArticuloUbicacion.AsNoTracking()
            join u in _db.Ubicaciones.AsNoTracking() on a.UbicacionId equals u.Id
            join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            where a.ArticuloId == articuloId && a.Estatus == EstatusCatalogo.Activo
            select new { a.Id, sa.AlmacenId };

        bool asignado = nivel == NivelReorden.Almacen
            ? await q.AnyAsync(x => x.AlmacenId == entidadId, ct)
            : await (from x in q
                     join al in _db.Almacenes.AsNoTracking() on x.AlmacenId equals al.Id
                     where al.SucursalId == entidadId
                     select x.Id).AnyAsync(ct);

        if (!asignado)
            throw new BusinessRuleException("REORDEN_SIN_ASIGNACION",
                nivel == NivelReorden.Almacen
                    ? "El artículo no está asignado a ninguna ubicación de este almacén (PR3)."
                    : "El artículo no está asignado a ninguna ubicación de esta sucursal (PR3).");
    }

    private async Task ValidarExclusionNivelAsync(
        Guid articuloId, NivelReorden nivel, Guid sucursalScope, CancellationToken ct)
    {
        bool conflicto;
        if (nivel == NivelReorden.Sucursal)
        {
            // Crear N1 en sucursal S → conflicto si ya hay N2 activa del artículo en un almacén de S.
            conflicto = await (
                from c in _db.ConfiguracionesReorden.AsNoTracking()
                join al in _db.Almacenes.AsNoTracking() on c.EntidadId equals al.Id
                where c.ArticuloId == articuloId
                   && c.Nivel == NivelReorden.Almacen
                   && c.Estatus == EstatusCatalogo.Activo
                   && al.SucursalId == sucursalScope
                select c.Id).AnyAsync(ct);
        }
        else
        {
            // Crear N2 en almacén de sucursal S → conflicto si ya hay N1 activa del artículo en S.
            conflicto = await _db.ConfiguracionesReorden.AsNoTracking().AnyAsync(
                c => c.ArticuloId == articuloId
                  && c.Nivel == NivelReorden.Sucursal
                  && c.Estatus == EstatusCatalogo.Activo
                  && c.EntidadId == sucursalScope,
                ct);
        }

        if (conflicto)
            throw new ConflictException("REORDEN_NIVEL_EN_CONFLICTO",
                "El artículo ya tiene una configuración de reorden activa del otro nivel (N1/N2) en esta sucursal.");
    }
}

// ─── Editar política (llave inmutable) ────────────────────────────────────────

public sealed record EditarConfiguracionReordenCommand(
    Guid Id,
    decimal Minimo,
    decimal Maximo,
    decimal PuntoReorden,
    bool AutoRequisicion,
    ObjetivoReposicion Objetivo) : IRequest<ConfiguracionReordenResponse>;

public sealed class EditarConfiguracionReordenValidator
    : AbstractValidator<EditarConfiguracionReordenCommand>
{
    public EditarConfiguracionReordenValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Objetivo).IsInEnum();
        RuleFor(c => c.Minimo).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Maximo).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PuntoReorden).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Maximo).GreaterThanOrEqualTo(c => c.Minimo)
            .WithMessage("El máximo no puede ser menor que el mínimo.");
    }
}

public sealed class EditarConfiguracionReordenHandler
    : IRequestHandler<EditarConfiguracionReordenCommand, ConfiguracionReordenResponse>
{
    private readonly AlmacenDbContext _db;

    public EditarConfiguracionReordenHandler(AlmacenDbContext db) => _db = db;

    public async Task<ConfiguracionReordenResponse> Handle(
        EditarConfiguracionReordenCommand request, CancellationToken cancellationToken)
    {
        var config = await _db.ConfiguracionesReorden
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("REORDEN_CONFIG_NO_ENCONTRADA",
                $"No existe configuración de reorden con id '{request.Id}'.");

        config.EditarPolitica(
            request.Minimo, request.Maximo, request.PuntoReorden,
            request.AutoRequisicion, request.Objetivo);
        await _db.SaveChangesAsync(cancellationToken);

        return ConfiguracionReordenMapper.ToResponse(config);
    }
}

// ─── Desactivar (idempotente) ─────────────────────────────────────────────────

public sealed record DesactivarConfiguracionReordenCommand(Guid Id)
    : IRequest<ConfiguracionReordenResponse>;

public sealed class DesactivarConfiguracionReordenHandler
    : IRequestHandler<DesactivarConfiguracionReordenCommand, ConfiguracionReordenResponse>
{
    private readonly AlmacenDbContext _db;

    public DesactivarConfiguracionReordenHandler(AlmacenDbContext db) => _db = db;

    public async Task<ConfiguracionReordenResponse> Handle(
        DesactivarConfiguracionReordenCommand request, CancellationToken cancellationToken)
    {
        var config = await _db.ConfiguracionesReorden
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("REORDEN_CONFIG_NO_ENCONTRADA",
                $"No existe configuración de reorden con id '{request.Id}'.");

        if (config.Estatus != EstatusCatalogo.Inactivo)
        {
            config.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ConfiguracionReordenMapper.ToResponse(config);
    }
}
