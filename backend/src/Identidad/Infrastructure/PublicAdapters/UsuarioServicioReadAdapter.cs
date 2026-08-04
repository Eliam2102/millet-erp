using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IUsuarioServicioReadPort"/> declarado en
/// <c>Almacen.Domain.Ports</c> (ADR-0047 PR5.C). Resuelve el usuario de servicio del
/// motor de reorden leyendo <c>identidad.usuario_servicio</c> por su
/// <c>EntraAppId</c> sintético. Vive en Identidad (dueño del dato; Identidad ya
/// referencia Almacen.Domain, Almacén no referencia Identidad). Cableado en
/// <c>Program.cs</c>. <c>Bypass()</c> de tenancy: el SP es cross-empresa a nivel
/// resolución (mismo patrón que <see cref="UsuarioReadAdapter"/>).
/// </summary>
public sealed class UsuarioServicioReadAdapter : IUsuarioServicioReadPort
{
    /// <summary>
    /// AppId sintético del SP "Reabastecimiento Automático" (ADR-0047 PR5.A). Debe
    /// coincidir con la entrada de <c>Auth:ServicePrincipalsJson</c>
    /// (appsettings/KV) que el bootstrap siembra; es sintético y fijo (no autentica
    /// por Entra), así que es estable entre ambientes.
    /// </summary>
    private static readonly Guid ReordenEntraAppId =
        Guid.Parse("a0000000-0000-4000-8000-0000000000a1");

    private readonly IdentidadDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public UsuarioServicioReadAdapter(IdentidadDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.UsuariosServicio
            .AsNoTracking()
            .Where(s => s.EntraAppId == ReordenEntraAppId)
            .Select(s => new UsuarioServicioLectura(s.Id, s.EmpresaId, s.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
