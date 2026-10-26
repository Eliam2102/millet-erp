using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.Compras.Domain.Oc;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.Application;

public sealed class OrdenesServicioServicio(ComercioExteriorDbContext db, ExpedienteServicio servicio, IOrdenesServicioReadPort compras)
{
    public async Task<IReadOnlyList<OrdenServicioLectura>> BuscarAsync(Guid expedienteId, Guid? proveedorId, string? q, CancellationToken ct)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorGastosRegistrar, ct);
        var e = await servicio.CargarAsync(expedienteId, true, ct);
        return await compras.BuscarAsync(e.EmpresaId, e.SucursalId, proveedorId, q, ct);
    }
    public async Task<OrdenServicioCopia?> ValidarAsync(Expediente e, DatosGasto gasto, ConceptoGasto concepto, decimal tc, CancellationToken ct)
    {
        if (gasto.OrdenServicioId is not Guid id)
        {
            if (concepto.RequiereOrdenServicio)
                throw new BusinessRuleException("CE_ORDEN_SERVICIO_REQUERIDA", "Seleccione una orden de servicio para este concepto.");
            if (gasto.ImporteAsignado is not null)
                throw new BusinessRuleException("CE_ORDEN_SERVICIO_INVALIDA", "El reparto requiere una orden de servicio.");
            return null;
        }
        var oc = await compras.ObtenerAsync(e.EmpresaId, id, ct) ?? throw new BusinessRuleException("CE_ORDEN_SERVICIO_INVALIDA", "No existe la orden de servicio en esta empresa.");
        await servicio.SucursalAsync(oc.SucursalId, true, ct);
        ValidarOrden(oc, e.EmpresaId, e.SucursalId, gasto.ProveedorId);
        var otros = await db.Set<GastoExpediente>().Where(g => g.OrdenServicioId == id && g.ExpedienteId != e.Id)
            .GroupBy(g => g.ExpedienteId).Select(g => g.Max(x => x.OrdenServicioAsignado) ?? 0).ToListAsync(ct);
        var actual = e.Gastos.FirstOrDefault(g => g.OrdenServicioId == id)?.OrdenServicioAsignado;
        var asignado = ValidarAsignacion(oc.Total, gasto.ImporteAsignado, actual, otros);
        return new(id, oc.Folio, oc.Total, oc.Moneda, asignado, oc.Moneda == "MXN" ? 1 : tc);
    }
    public static void ValidarOrden(OrdenServicioLectura oc, Guid empresa, Guid sucursal, Guid proveedor)
    {
        if (oc.EmpresaId != empresa || oc.SucursalId != sucursal || oc.ProveedorId != proveedor ||
            oc.Estado == EstadoOrdenCompra.Cancelada || oc.Lineas.Count == 0 || oc.Moneda is not ("MXN" or "USD"))
            throw new BusinessRuleException("CE_ORDEN_SERVICIO_INVALIDA", "La orden debe corresponder al proveedor y sucursal, contener servicios y no estar cancelada.");
    }
    public static decimal ValidarAsignacion(decimal total, decimal? solicitado, decimal? actual, IReadOnlyList<decimal> otros)
    {
        if (otros.Count > 0 && solicitado is null && actual is null)
            throw new BusinessRuleException("CE_ORDEN_SERVICIO_REPARTO_REQUERIDO", "La orden ya tiene gastos en otro expediente. Capture el importe asignado a este expediente.");
        var asignado = solicitado ?? actual ?? total;
        if (asignado <= 0 || decimal.Round(asignado, 2) != asignado || asignado + otros.Sum() > total)
            throw new BusinessRuleException("CE_ORDEN_SERVICIO_EXCEDE_TOTAL", "La suma asignada entre expedientes no puede superar el total de la orden.");
        return asignado;
    }
}
