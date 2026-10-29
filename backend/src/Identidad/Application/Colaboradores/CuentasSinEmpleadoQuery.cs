using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Cuentas Microsoft que todavía no son empleados (86e3x6cpw, B1;
/// contrato 1): las del directorio de Entra que no están ligadas a un
/// empleado (<c>Usuario.EntraOid</c> → <c>Empleado.UsuarioId</c>), sin
/// cuentas técnicas, más los usuarios del ERP activos que entraron sin
/// empleado aunque el directorio no los devuelva.
/// </summary>
public sealed record ListarCuentasSinEmpleadoQuery(string? Buscar, int Limit = 50)
    : IRequest<CuentasSinEmpleadoResponse>;

public sealed record CuentaSinEmpleadoItem(
    string ObjectId,
    string Nombre,
    string Correo,
    bool Habilitada,
    Guid? UsuarioId,
    bool YaEntroAlErp);

public sealed record CuentasSinEmpleadoResponse(IReadOnlyList<CuentaSinEmpleadoItem> Items, int Total);

/// <summary>
/// Empleado ligado a una cuenta del ERP (contrato 2): redirige la ficha
/// vieja de la cuenta a la del empleado. 404 <c>EMPLEADO_NO_ENCONTRADO</c>.
/// </summary>
public sealed record ObtenerEmpleadoPorUsuarioQuery(Guid UsuarioId) : IRequest<EmpleadoPorUsuarioResponse>;

public sealed record EmpleadoPorUsuarioResponse(Guid EmpleadoId);

public sealed class CuentasSinEmpleadoHandlers :
    IRequestHandler<ListarCuentasSinEmpleadoQuery, CuentasSinEmpleadoResponse>,
    IRequestHandler<ObtenerEmpleadoPorUsuarioQuery, EmpleadoPorUsuarioResponse>
{
    /// <summary>Máximo que pide Graph en una página de <c>/users</c>.</summary>
    private const int MaximoDirectorio = 999;

    private readonly IdentidadDbContext _identidad;
    private readonly CompartidoDbContext _compartido;
    private readonly IEntraDirectorioPort _directorio;

    public CuentasSinEmpleadoHandlers(
        IdentidadDbContext identidad, CompartidoDbContext compartido, IEntraDirectorioPort directorio)
    {
        _identidad = identidad;
        _compartido = compartido;
        _directorio = directorio;
    }

    public async Task<CuentasSinEmpleadoResponse> Handle(
        ListarCuentasSinEmpleadoQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var limite = Math.Clamp(query.Limit, 1, 200);
        var buscar = string.IsNullOrWhiteSpace(query.Buscar) ? null : query.Buscar.Trim();

        // Se pide más de lo que se muestra: parte del directorio ya son empleados.
        var cuentas = await _directorio.ListarCuentasAsync(buscar, MaximoDirectorio, ct);

        var usuarios = await _identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Select(u => new { u.Id, u.EntraOid, u.Email, u.Nombre, u.Activo, u.EsCuentaTecnica, u.PrimerAccesoEn })
            .ToListAsync(ct);
        var ligados = (await _compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.UsuarioId != null)
                .Select(e => e.UsuarioId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var porOid = usuarios.GroupBy(u => u.EntraOid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var porCorreo = usuarios.GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var items = new List<CuentaSinEmpleadoItem>();
        var usuariosListados = new HashSet<Guid>();
        foreach (var cuenta in cuentas)
        {
            var usuario = porOid.GetValueOrDefault(cuenta.ObjectId) ?? porCorreo.GetValueOrDefault(cuenta.Upn);
            if (usuario is not null)
            {
                if (usuario.EsCuentaTecnica || ligados.Contains(usuario.Id)) continue;
                usuariosListados.Add(usuario.Id);
            }
            items.Add(new CuentaSinEmpleadoItem(
                cuenta.ObjectId, cuenta.NombreMostrado, cuenta.Upn, cuenta.Habilitada,
                usuario?.Id, usuario?.PrimerAccesoEn is not null));
        }

        // Usuarios que entraron al ERP sin empleado (JIT o cuenta independiente).
        foreach (var usuario in usuarios.Where(u => u.Activo && !u.EsCuentaTecnica
                     && !ligados.Contains(u.Id) && !usuariosListados.Contains(u.Id)))
        {
            if (buscar is not null
                && !usuario.Nombre.Contains(buscar, StringComparison.OrdinalIgnoreCase)
                && !usuario.Email.Contains(buscar, StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new CuentaSinEmpleadoItem(
                usuario.EntraOid, usuario.Nombre, usuario.Email, Habilitada: true, usuario.Id, YaEntroAlErp: true));
        }

        var ordenados = items
            .OrderBy(i => i.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Correo, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new CuentasSinEmpleadoResponse(ordenados.Take(limite).ToList(), ordenados.Count);
    }

    public async Task<EmpleadoPorUsuarioResponse> Handle(
        ObtenerEmpleadoPorUsuarioQuery query, CancellationToken cancellationToken)
    {
        var empleadoId = await _compartido.Empleados.AsNoTracking()
            .Where(e => e.UsuarioId == query.UsuarioId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return empleadoId is Guid id
            ? new EmpleadoPorUsuarioResponse(id)
            : throw new EntityNotFoundException(
                "EMPLEADO_NO_ENCONTRADO", "La cuenta no está ligada a un empleado.");
    }
}
