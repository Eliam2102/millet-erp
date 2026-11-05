using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Cuentas técnicas del ERP (86e3yxe9m): las que no corresponden a un
/// empleado (administrador inicial, soporte, QA). La vista «Cuentas sin
/// empleado» las excluye, así que esta las hace visibles con su rol en la
/// empresa actual, su primer acceso y su estado.
/// </summary>
public sealed record ListarCuentasTecnicasQuery(string? Buscar, int Limit = 50)
    : IRequest<CuentasTecnicasResponse>;

public sealed record CuentaTecnicaItem(
    Guid UsuarioId,
    string Nombre,
    string Correo,
    IReadOnlyList<string> Roles,
    DateTimeOffset? PrimerAccesoEn,
    bool Activo,
    EstadoAcceso EstadoAcceso);

public sealed record CuentasTecnicasResponse(IReadOnlyList<CuentaTecnicaItem> Items, int Total);

public sealed class CuentasTecnicasHandler : IRequestHandler<ListarCuentasTecnicasQuery, CuentasTecnicasResponse>
{
    private readonly IdentidadDbContext _identidad;

    public CuentasTecnicasHandler(IdentidadDbContext identidad) => _identidad = identidad;

    public async Task<CuentasTecnicasResponse> Handle(
        ListarCuentasTecnicasQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limite = Math.Clamp(query.Limit, 1, 200);
        var buscar = string.IsNullOrWhiteSpace(query.Buscar) ? null : query.Buscar.Trim();

        var usuarios = await _identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.EsCuentaTecnica)
            .Select(u => new { u.Id, u.Nombre, u.Email, u.PrimerAccesoEn, u.Activo, u.EstadoAcceso })
            .ToListAsync(ct);
        if (buscar is not null)
            usuarios = usuarios
                .Where(u => u.Nombre.Contains(buscar, StringComparison.OrdinalIgnoreCase)
                            || u.Email.Contains(buscar, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var ids = usuarios.Select(u => u.Id).ToList();
        // Roles en la empresa actual (filtro global de UsuarioEmpresaRol).
        var roles = (await (
                from uer in _identidad.UsuarioEmpresaRoles.AsNoTracking()
                join r in _identidad.Roles.AsNoTracking() on uer.RolId equals r.Id
                where ids.Contains(uer.UsuarioId)
                select new { uer.UsuarioId, r.Nombre })
            .ToListAsync(ct))
            .GroupBy(x => x.UsuarioId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Nombre).Distinct().Order().ToList());

        var items = usuarios
            .OrderBy(u => u.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .Select(u => new CuentaTecnicaItem(
                u.Id, u.Nombre, u.Email, roles.GetValueOrDefault(u.Id) ?? [], u.PrimerAccesoEn, u.Activo, u.EstadoAcceso))
            .ToList();
        return new CuentasTecnicasResponse(items.Take(limite).ToList(), items.Count);
    }
}
