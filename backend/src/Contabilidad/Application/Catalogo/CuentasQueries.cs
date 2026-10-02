using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Catalogo;

public sealed record ListarCuentasQuery(
    EstatusCatalogo? Estatus, TipoCuenta? Tipo, string? Q, Guid? PadreId, bool? Pendientes, int Offset, int Limit)
    : IRequest<PagedResponse<CuentaResponse>>;

public sealed class ListarCuentasHandler(ContabilidadDbContext db) : IRequestHandler<ListarCuentasQuery, PagedResponse<CuentaResponse>>
{
    public async Task<PagedResponse<CuentaResponse>> Handle(ListarCuentasQuery request, CancellationToken cancellationToken)
    {
        var q = db.Cuentas.AsNoTracking().AsQueryable();
        if (request.Estatus is { } e) q = q.Where(c => c.Estatus == e);
        if (request.Tipo is { } t) q = q.Where(c => c.Tipo == t);
        if (request.PadreId is { } p) q = q.Where(c => c.PadreId == p);
        if (request.Pendientes is { } pend) q = q.Where(c => (c.Naturaleza == null || c.Tipo == null) == pend);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var aguja = request.Q.Trim();
            var codigo = $"%{aguja}%";
#pragma warning disable CA1304, CA1311, CA1862
            q = q.Where(c => EF.Functions.ILike(c.Codigo, codigo)
                || PostgresFunctions.Translate(c.Nombre.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
#pragma warning restore CA1304, CA1311, CA1862
        }
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderBy(c => c.Codigo).Skip(request.Offset).Take(request.Limit).ToListAsync(cancellationToken);
        return new PagedResponse<CuentaResponse>([.. items.Select(c => CuentaResponse.De(c))], total, request.Offset, request.Limit);
    }
}

/// <summary>Carga perezosa: hijas directas de <c>RaizId</c> (null = raíces).</summary>
public sealed record ArbolCuentasQuery(EstatusCatalogo? Estatus, Guid? RaizId) : IRequest<IReadOnlyList<CuentaArbolNodo>>;

public sealed record CuentaArbolNodo(
    Guid Id, string Codigo, string Nombre, int Nivel, TipoCuenta? Tipo, bool Activa, bool PendienteValidacion, bool TieneHijos);

public sealed class ArbolCuentasHandler(ContabilidadDbContext db) : IRequestHandler<ArbolCuentasQuery, IReadOnlyList<CuentaArbolNodo>>
{
    public async Task<IReadOnlyList<CuentaArbolNodo>> Handle(ArbolCuentasQuery request, CancellationToken cancellationToken)
    {
        var q = db.Cuentas.AsNoTracking().Where(c => c.PadreId == request.RaizId);
        if (request.Estatus is { } e) q = q.Where(c => c.Estatus == e);
        var hijas = await q.OrderBy(c => c.Codigo).ToListAsync(cancellationToken);
        var ids = hijas.Select(h => h.Id).ToList();
        var conHijos = (await db.Cuentas.AsNoTracking().Where(c => c.PadreId != null && ids.Contains(c.PadreId.Value))
            .Select(c => c.PadreId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return [.. hijas.Select(c => new CuentaArbolNodo(c.Id, c.Codigo, c.Nombre, c.Nivel, c.Tipo, c.Activa, c.PendienteValidacion, conHijos.Contains(c.Id)))];
    }
}

public sealed record ObtenerCuentaQuery(Guid Id) : IRequest<CuentaResponse>;

public sealed class ObtenerCuentaHandler(ContabilidadDbContext db) : IRequestHandler<ObtenerCuentaQuery, CuentaResponse>
{
    public async Task<CuentaResponse> Handle(ObtenerCuentaQuery request, CancellationToken cancellationToken)
    {
        var c = await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.Id}'.");
        var origenes = await db.Origenes.AsNoTracking().Where(o => o.CuentaId == c.Id)
            .OrderBy(o => o.Fuente).ThenBy(o => o.CodigoOrigen).Select(o => new OrigenCuentaDto(o.Fuente, o.CodigoOrigen)).ToListAsync(cancellationToken);
        return CuentaResponse.De(c, await db.Usos.AnyAsync(u => u.CuentaId == c.Id, cancellationToken), origenes);
    }
}

/// <summary>Misma validación que el puerto de lectura (útil para la UI de los consumidores).</summary>
public sealed record ValidarMovimientoQuery(string? Codigo, Guid? CuentaId, OrigenMovimiento Origen) : IRequest<CuentaContableValidacion>;

public sealed class ValidarMovimientoHandler(ICuentaContableReadPort puerto) : IRequestHandler<ValidarMovimientoQuery, CuentaContableValidacion>
{
    public Task<CuentaContableValidacion> Handle(ValidarMovimientoQuery request, CancellationToken cancellationToken) =>
        request.CuentaId is { } id ? puerto.ValidarParaMovimientoAsync(id, request.Origen, cancellationToken)
            : puerto.ValidarParaMovimientoAsync(request.Codigo ?? string.Empty, request.Origen, cancellationToken);
}

/// <summary>Configuración de formato activa (§20.1); la consume la UI y el perfilado. No son secretos.</summary>
public sealed record ConfiguracionFormatoQuery : IRequest<CatalogoOpciones>;

public sealed class ConfiguracionFormatoHandler(FormatoCatalogo formato) : IRequestHandler<ConfiguracionFormatoQuery, CatalogoOpciones>
{
    public Task<CatalogoOpciones> Handle(ConfiguracionFormatoQuery request, CancellationToken cancellationToken) => Task.FromResult(formato.Opciones);
}

public sealed record ImportacionLoteDto(
    Guid Id, string Fuente, string? ArchivoNombre, string Huella, int TotalFilas, int Creadas, int Actualizadas, int SinCambios,
    DateTimeOffset AplicadoEn, string? AplicadoPor);

public sealed record ListarLotesQuery(int Offset, int Limit) : IRequest<PagedResponse<ImportacionLoteDto>>;

public sealed class ListarLotesHandler(ContabilidadDbContext db) : IRequestHandler<ListarLotesQuery, PagedResponse<ImportacionLoteDto>>
{
    public async Task<PagedResponse<ImportacionLoteDto>> Handle(ListarLotesQuery request, CancellationToken cancellationToken)
    {
        var q = db.Importaciones.AsNoTracking();
        var items = await q.OrderByDescending(l => l.AplicadoEn).Skip(request.Offset).Take(request.Limit)
            .Select(l => new ImportacionLoteDto(l.Id, l.Fuente, l.ArchivoNombre, l.HuellaSha256, l.TotalFilas, l.Creadas,
                l.Actualizadas, l.SinCambios, l.AplicadoEn, l.AplicadoPor)).ToListAsync(cancellationToken);
        return new PagedResponse<ImportacionLoteDto>(items, await q.CountAsync(cancellationToken), request.Offset, request.Limit);
    }
}
