using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Application.Listar;

/// <summary>Criterios compartidos por las bandejas y los resúmenes del Inicio.</summary>
public static class PendientesAutorizacionLectura
{
    public static IQueryable<Requisicion> Requisiciones(IQueryable<Requisicion> query, bool nivel1, bool nivel2) =>
        query.Where(r => r.Estado == EstadoRequisicion.EnAutorizacion &&
            ((nivel1 && !r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1)) ||
             (nivel2 && r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1) &&
                !r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel2))));

    public static IQueryable<OrdenCompra> Ordenes(IQueryable<OrdenCompra> query, bool nivel1, bool nivel2) =>
        query.Where(o => (nivel1 && o.Estado == EstadoOrdenCompra.EnAutorizacionJefeCompras) ||
            (nivel2 && (o.Estado == EstadoOrdenCompra.EnAutorizacionDireccion || o.Estado == EstadoOrdenCompra.CancelacionSolicitada)));
}
