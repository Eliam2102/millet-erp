using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Motor;

// ─── Roles de cuenta (lo único que edita Laura; plano C1 §2) ─────────────────

public sealed record RolCuentaResponse(Guid Id, string Codigo, string Descripcion, Guid? CuentaId, string? CuentaCodigo, string? CuentaNombre,
    Guid? SucursalId, string? Moneda, DateOnly VigenteDesde, DateOnly? VigenteHasta, int Version);

public sealed record RolCuentaCambioResponse(Guid Id, string? CuentaAnterior, string CuentaNueva, string Usuario, DateTimeOffset CambiadoEn, string? Motivo);

public sealed record ListarRolesCuentaQuery : IRequest<IReadOnlyList<RolCuentaResponse>>;

public sealed record CrearRolCuentaCommand(string Codigo, string Descripcion, Guid? CuentaId, Guid? SucursalId, string? Moneda, DateOnly? VigenteDesde)
    : IRequest<RolCuentaResponse>;

/// <summary>C1.4-c: cambia la cuenta del rol; solo afecta pólizas nuevas y deja bitácora.</summary>
public sealed record CambiarCuentaRolCommand(Guid RolId, Guid CuentaId, string? Motivo, int VersionEsperada) : IRequest<RolCuentaResponse>;

public sealed record BitacoraRolCuentaQuery(Guid RolId) : IRequest<IReadOnlyList<RolCuentaCambioResponse>>;

public sealed class RolesCuentaHandlers(ContabilidadDbContext db, ICuentaContableReadPort cuentas, ICurrentUserContext usuario, IClock clock) :
    IRequestHandler<ListarRolesCuentaQuery, IReadOnlyList<RolCuentaResponse>>,
    IRequestHandler<CrearRolCuentaCommand, RolCuentaResponse>,
    IRequestHandler<CambiarCuentaRolCommand, RolCuentaResponse>,
    IRequestHandler<BitacoraRolCuentaQuery, IReadOnlyList<RolCuentaCambioResponse>>
{
    public async Task<IReadOnlyList<RolCuentaResponse>> Handle(ListarRolesCuentaQuery request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var roles = await db.RolesCuenta.AsNoTracking().OrderBy(r => r.Codigo).ThenBy(r => r.VigenteDesde).ToListAsync(ct);
        var cuentasPorId = await NombresAsync(roles.Select(r => r.CuentaId).OfType<Guid>(), ct);
        return [.. roles.Select(r => Response(r, cuentasPorId))];
    }

    public async Task<RolCuentaResponse> Handle(CrearRolCuentaCommand r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (r.CuentaId is { } cuenta) await ValidarCuentaAsync(cuenta, ct);
        var rol = RolCuenta.Crear(Guid.CreateVersion7(), r.Codigo, r.Descripcion, r.CuentaId, r.SucursalId, r.Moneda,
            r.VigenteDesde ?? FechaContable.HoyLocal(clock.UtcNow));
        if (await db.RolesCuenta.AnyAsync(x => x.Codigo == rol.Codigo && x.SucursalId == rol.SucursalId && x.Moneda == rol.Moneda
                && x.VigenteDesde == rol.VigenteDesde, ct))
            throw new ConflictException("CONTAB_ROL_DUPLICADO", $"Ya existe el rol {rol.Codigo} con ese filtro y vigencia.");
        db.RolesCuenta.Add(rol);
        await db.SaveChangesAsync(ct);
        return Response(rol, await NombresAsync(rol.CuentaId is { } id ? [id] : [], ct));
    }

    public async Task<RolCuentaResponse> Handle(CambiarCuentaRolCommand r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rol = await db.RolesCuenta.FirstOrDefaultAsync(x => x.Id == r.RolId, ct)
            ?? throw new EntityNotFoundException("CONTAB_ROL_NO_ENCONTRADO", $"No existe el rol '{r.RolId}'.");
        if (rol.Version != r.VersionEsperada) throw new ConcurrencyException(nameof(RolCuenta), rol.Id);
        await ValidarCuentaAsync(r.CuentaId, ct);
        db.RolesCuentaCambios.Add(rol.CambiarCuenta(r.CuentaId, usuario.UserName ?? usuario.Email ?? "system", clock.UtcNow, r.Motivo));
        await db.SaveChangesAsync(ct);
        return Response(rol, await NombresAsync([r.CuentaId], ct));
    }

    public async Task<IReadOnlyList<RolCuentaCambioResponse>> Handle(BitacoraRolCuentaQuery r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cambios = await db.RolesCuentaCambios.AsNoTracking().Where(x => x.RolCuentaId == r.RolId).OrderByDescending(x => x.CambiadoEn).ToListAsync(ct);
        var nombres = await NombresAsync(cambios.SelectMany(c => new[] { c.CuentaAnteriorId, c.CuentaNuevaId }).OfType<Guid>(), ct);
        return [.. cambios.Select(c => new RolCuentaCambioResponse(c.Id,
            c.CuentaAnteriorId is { } a ? Etiqueta(nombres, a) : null, Etiqueta(nombres, c.CuentaNuevaId), c.Usuario, c.CambiadoEn, c.Motivo))];
    }

    /// <summary>
    /// La cuenta debe aceptar movimientos del motor por alguna vía: <c>Automatica</c> o el auxiliar que corresponda (las cuentas
    /// colectivas solo admiten su auxiliar; el rol no sabe cuál usará cada plantilla).
    /// </summary>
    private async Task ValidarCuentaAsync(Guid cuentaId, CancellationToken ct)
    {
        CuentaContableValidacion? ultima = null;
        foreach (var origen in new[] { OrigenMovimiento.Automatica, OrigenMovimiento.AuxiliarCxP, OrigenMovimiento.AuxiliarCxC })
        {
            ultima = await cuentas.ValidarParaMovimientoAsync(cuentaId, origen, ct);
            if (ultima.Valida) return;
        }
        throw new BusinessRuleException("CONTAB_ROL_CUENTA_NO_VALIDA",
            $"La cuenta no admite movimientos del motor ({ultima?.Motivo}): elija una cuenta afectable y activa.");
    }

    private async Task<Dictionary<Guid, (string Codigo, string Nombre)>> NombresAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await db.Cuentas.AsNoTracking().Where(c => lista.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Codigo, c.Nombre), ct);
    }

    private static string Etiqueta(Dictionary<Guid, (string Codigo, string Nombre)> n, Guid id) =>
        n.TryGetValue(id, out var c) ? $"{c.Codigo} {c.Nombre}" : id.ToString();

    private static RolCuentaResponse Response(RolCuenta r, Dictionary<Guid, (string Codigo, string Nombre)> n)
    {
        (string Codigo, string Nombre)? c = r.CuentaId is { } id && n.TryGetValue(id, out var v) ? v : null;
        return new(r.Id, r.Codigo, r.Descripcion, r.CuentaId, c?.Codigo, c?.Nombre, r.SucursalId, r.Moneda, r.VigenteDesde, r.VigenteHasta, r.Version);
    }
}

// ─── Plantillas (consulta) ───────────────────────────────────────────────────

public sealed record PlantillaContableResponse(Guid Id, string TipoEvento, int Version, DateOnly VigenteDesde, string TipoOperacionClave,
    string Concepto, string Descripcion, IReadOnlyList<LineaPlantillaContable> Lineas);

public sealed record ListarPlantillasContablesQuery : IRequest<IReadOnlyList<PlantillaContableResponse>>;

public sealed class ListarPlantillasContablesHandler(ContabilidadDbContext db) : IRequestHandler<ListarPlantillasContablesQuery, IReadOnlyList<PlantillaContableResponse>>
{
    public async Task<IReadOnlyList<PlantillaContableResponse>> Handle(ListarPlantillasContablesQuery request, CancellationToken cancellationToken) =>
        [.. (await db.PlantillasContables.AsNoTracking().OrderBy(p => p.TipoEvento).ThenByDescending(p => p.NumeroVersion).ToListAsync(cancellationToken))
            .Select(p => new PlantillaContableResponse(p.Id, p.TipoEvento, p.NumeroVersion, p.VigenteDesde, p.TipoOperacionClave, p.Concepto,
                p.Descripcion, p.Lineas))];
}

// ─── Bandeja de rechazos ─────────────────────────────────────────────────────

public sealed record RechazoContableResponse(Guid Id, string TipoEvento, Guid EventoId, CausaRechazoContable Causa, string? Detalle, string Mensaje,
    int Intentos, EstadoRechazoContable Estado, DateTimeOffset CreadoEn, DateTimeOffset UltimoIntentoEn, Guid? PolizaId, string? ResueltoPor,
    DateTimeOffset? ResueltoEn, string? MotivoDescarte, string Payload, int Version);

public sealed record ListarRechazosContablesQuery(EstadoRechazoContable? Estado) : IRequest<IReadOnlyList<RechazoContableResponse>>;

/// <summary>Corre la plantilla vigente con el mismo payload y la misma llave (C1.4-b).</summary>
public sealed record ReprocesarRechazoCommand(Guid Id) : IRequest<ResultadoMotor>;

/// <summary>C1.4-h: exige motivo (y el permiso, en el endpoint).</summary>
public sealed record DescartarRechazoCommand(Guid Id, string Motivo) : IRequest<RechazoContableResponse>;

public sealed class RechazosHandlers(ContabilidadDbContext db, IMediator mediator, ICurrentUserContext usuario, IClock clock) :
    IRequestHandler<ListarRechazosContablesQuery, IReadOnlyList<RechazoContableResponse>>,
    IRequestHandler<ReprocesarRechazoCommand, ResultadoMotor>,
    IRequestHandler<DescartarRechazoCommand, RechazoContableResponse>
{
    public async Task<IReadOnlyList<RechazoContableResponse>> Handle(ListarRechazosContablesQuery r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var q = db.RechazosContables.AsNoTracking();
        if (r.Estado is { } estado) q = q.Where(x => x.Estado == estado);
        return [.. (await q.OrderByDescending(x => x.UltimoIntentoEn).Take(500).ToListAsync(ct)).Select(Response)];
    }

    public async Task<ResultadoMotor> Handle(ReprocesarRechazoCommand r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rechazo = await db.RechazosContables.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.Id, ct)
            ?? throw new EntityNotFoundException("CONTAB_RECHAZO_NO_ENCONTRADO", $"No existe el rechazo '{r.Id}'.");
        if (rechazo.Estado != EstadoRechazoContable.Pendiente)
            throw new ConflictException("CONTAB_RECHAZO_RESUELTO", "El rechazo ya se resolvió (reprocesado o descartado).");
        return await mediator.Send(new ProcesarEventoContableCommand(rechazo.TipoEvento, rechazo.EventoId, rechazo.Payload,
            usuario.UserName ?? usuario.Email ?? "system"), ct);
    }

    public async Task<RechazoContableResponse> Handle(DescartarRechazoCommand r, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var rechazo = await db.RechazosContables.FirstOrDefaultAsync(x => x.Id == r.Id, ct)
            ?? throw new EntityNotFoundException("CONTAB_RECHAZO_NO_ENCONTRADO", $"No existe el rechazo '{r.Id}'.");
        rechazo.Descartar(r.Motivo, usuario.UserName ?? usuario.Email ?? "system", clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Response(rechazo);
    }

    private static RechazoContableResponse Response(RechazoContable x) => new(x.Id, x.TipoEvento, x.EventoId, x.Causa, x.Detalle, x.Mensaje,
        x.Intentos, x.Estado, x.CreadoEn, x.UltimoIntentoEn, x.PolizaId, x.ResueltoPor, x.ResueltoEn, x.MotivoDescarte, x.Payload, x.Version);
}
