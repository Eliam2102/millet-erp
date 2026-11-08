using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Implementación de <see cref="IUsuarioCorreoAvisosPort"/> que consulta <c>identidad.usuarios</c>
/// resolviendo prioritariamente <c>correo_para_avisos</c> con fallback al correo de Entra ID (86e3zexqm).
/// </summary>
public sealed class UsuarioCorreoAvisosAdapter : IUsuarioCorreoAvisosPort
{
    private readonly IdentidadDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public UsuarioCorreoAvisosAdapter(IdentidadDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<string?> ObtenerCorreoAvisosAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();
        var u = await _db.Usuarios.AsNoTracking()
            .Where(x => x.Id == usuarioId)
            .Select(x => new { x.CorreoParaAvisos, x.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (u is null) return null;
        return UsuarioAvisosResolver.ResolverCorreoDestino(u.CorreoParaAvisos, u.Email);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerCorreosAvisosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken)
    {
        if (usuarioIds.Count == 0) return new Dictionary<Guid, string>();

        var distinct = usuarioIds.Distinct().ToArray();
        using var bypass = _empresaContext.Bypass();

        var lista = await _db.Usuarios.AsNoTracking()
            .Where(x => distinct.Contains(x.Id))
            .Select(x => new { x.Id, x.CorreoParaAvisos, x.Email })
            .ToListAsync(cancellationToken);

        var resultado = new Dictionary<Guid, string>();
        foreach (var item in lista)
        {
            var destino = UsuarioAvisosResolver.ResolverCorreoDestino(item.CorreoParaAvisos, item.Email);
            if (!string.IsNullOrWhiteSpace(destino))
                resultado[item.Id] = destino;
        }

        return resultado;
    }
}
