using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Propietario de adjuntos para <see cref="Proveedor"/> (expediente documental, F1-ADM-11 G1.2).
/// Proveedor es cross-empresa y sin sucursal: la autorización se reduce al permiso de rol.
/// Un proveedor <see cref="EstatusCatalogo.Inactivo"/> no admite subidas nuevas (sí lectura y baja).
/// </summary>
public sealed class ProveedorAdjuntoPropietario : IAdjuntoPropietario
{
    public const string Tipo = "proveedor";

    private readonly CompartidoDbContext _db;

    public ProveedorAdjuntoPropietario(CompartidoDbContext db) => _db = db;

    public string TipoEntidad => Tipo;

    public string CodigoNoEncontrado => "PROVEEDOR_NO_ENCONTRADO";

    // Literales (no PermisosCanonicos): Compartido no referencia Identidad; los tests de integración los cruzan.
    public string PermisoVer => "datos_maestros.proveedores.adjuntos-ver";

    public string PermisoSubir => "datos_maestros.proveedores.adjuntos-subir";

    public string PermisoBaja => "datos_maestros.proveedores.adjuntos-baja";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
    {
        var p = await _db.Proveedores
            .AsNoTracking()
            .Where(x => x.Id == entidadId)
            .Select(x => new { x.Id, x.Clave, x.RazonSocial, x.Estatus, x.TipoPersona })
            .FirstOrDefaultAsync(cancellationToken);

        return p is null
            ? null
            : new AdjuntoPropietarioInfo(
                p.Id,
                EmpresaId: null,
                SucursalId: null,
                PuedeSubir: p.Estatus != EstatusCatalogo.Inactivo,
                EsPersonaMoral: p.TipoPersona == TipoPersonaProveedor.Moral,
                Etiqueta: $"{p.Clave} · {p.RazonSocial}");
    }

    // Proveedor no tiene sucursal: no hay alcance territorial que verificar.
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
