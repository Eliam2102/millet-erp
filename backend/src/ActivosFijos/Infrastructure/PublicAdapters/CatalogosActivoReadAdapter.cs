using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
public sealed class CatalogosActivoReadAdapter(CompartidoDbContext db, IdentidadDbContext identidad, IDim3ElegibilidadPort centros, Millet.Contabilidad.Application.Ports.ICentroCostoContabilidadPort dimensiones, Millet.SharedKernel.Application.ICurrentEmpresaContext empresa) : ICatalogosActivoReadPort
{
    public async Task<CustodiaDescripcion> DescribirAsync(CustodiaActivo custodia, CancellationToken ct)
    {
        var responsable = await identidad.Usuarios.Where(x => x.Id == custodia.ResponsableId).Select(x => x.Nombre).SingleOrDefaultAsync(ct)
            ?? await db.Empleados.Where(x => x.Id == custodia.ResponsableId && x.EmpresaId == empresa.Current).Select(x => x.Nombre).SingleOrDefaultAsync(ct);
        var sucursal = await db.Sucursales.Where(x => x.Id == custodia.SucursalId).Select(x => x.Nombre).SingleOrDefaultAsync(ct);
        var nodos = await dimensiones.ObtenerAsync(new[] { custodia.CentroCostoId, custodia.EquipoId ?? Guid.Empty }.Where(x => x != Guid.Empty).ToArray(), ct);
        string? Etiqueta(Guid? id) => id is Guid valor && nodos.TryGetValue(valor, out var nodo) ? $"{nodo.Clave} · {nodo.Nombre}" : null;
        return new(responsable, sucursal, Etiqueta(custodia.CentroCostoId), Etiqueta(custodia.EquipoId));
    }
    public async Task<IReadOnlyList<Guid>> ResponsablesAsync(Guid usuario, CancellationToken ct)
    {
        var ids = await db.Empleados.Where(x => x.UsuarioId == usuario && x.Estatus == EstatusCatalogo.Activo && x.EmpresaId == empresa.Current).Select(x => x.Id).ToListAsync(ct);
        ids.Add(usuario); return ids;
    }
    public async Task ValidarCustodiaAsync(CustodiaActivo custodia, bool completa, CancellationToken ct)
    {
        if (!await db.Sucursales.AnyAsync(x => x.Id == custodia.SucursalId && x.Estatus == EstatusCatalogo.Activo, ct))
            throw new BusinessRuleException("AF_SUCURSAL_INVALIDA", "Seleccione una sucursal activa.");
        if ((completa || custodia.ResponsableId != Guid.Empty) && !await identidad.Usuarios.AnyAsync(x => x.Id == custodia.ResponsableId && x.Activo && identidad.UsuarioEmpresaRoles.Any(r => r.UsuarioId == x.Id), ct)
            && !await db.Empleados.AnyAsync(x => x.Id == custodia.ResponsableId && x.Estatus == EstatusCatalogo.Activo && x.EmpresaId == empresa.Current, ct))
            throw new BusinessRuleException("AF_RESPONSABLE_INVALIDO", "Seleccione un usuario o empleado activo de la empresa.");
        foreach (var id in new[] { custodia.CentroCostoId == Guid.Empty ? (Guid?)null : custodia.CentroCostoId, custodia.EquipoId }.OfType<Guid>())
            if (await centros.EvaluarAsync(id, false, ct) != Dim3Elegibilidad.Valida)
                throw new BusinessRuleException("AF_CENTRO_INVALIDO", "Seleccione un centro de costo o equipo activo.");
        if (custodia.EquipoId is Guid equipoId && custodia.CentroCostoId != Guid.Empty)
        {
            var nodos = await dimensiones.ObtenerAsync([equipoId, custodia.CentroCostoId], ct);
            if (!nodos.TryGetValue(equipoId, out var equipo) || equipo.Nivel != Millet.Contabilidad.Domain.DimensionContable.Dim3
                || (custodia.CentroCostoId != equipo.Id && custodia.CentroCostoId != equipo.Dim1Id && custodia.CentroCostoId != equipo.Dim2Id))
                throw new BusinessRuleException("AF_EQUIPO_CENTRO_DISTINTO", "El equipo debe pertenecer al centro de costo seleccionado.");
        }
        if (completa && custodia.CentroCostoId == Guid.Empty)
            throw new BusinessRuleException("AF_CENTRO_REQUERIDO", "Complete el centro de costo.");
    }
}
