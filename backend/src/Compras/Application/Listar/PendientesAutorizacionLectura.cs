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
            (nivel2 && (o.Estado == EstadoOrdenCompra.EnAutorizacionDireccion || (o.Estado == EstadoOrdenCompra.CancelacionSolicitada && !o.SolicitudesCancelacion.Any(s => s.EsperaAlmacenes)))));
    /// <summary>Misma elegibilidad individual usada por los avisos y la bandeja de OC.</summary>
    public static IQueryable<OrdenCompra> OrdenesAccionables(IQueryable<OrdenCompra> query,
        Guid usuarioId, bool nivel1, bool nivel2) =>
        query.Where(o => o.CompradorTitularId != usuarioId &&
            ((nivel1 && o.Estado == EstadoOrdenCompra.EnAutorizacionJefeCompras) ||
             (nivel2 && o.Estado == EstadoOrdenCompra.EnAutorizacionDireccion &&
                !o.Autorizaciones.Any(a => a.Ciclo == o.CicloAutorizacion && a.Nivel == NivelAutorizacion.Nivel1 && a.UsuarioId == usuarioId))));

    public static IQueryable<OrdenCompra> CancelacionesAccionables(IQueryable<OrdenCompra> query,
        Guid usuarioId) => query.Where(o => o.Estado == EstadoOrdenCompra.CancelacionSolicitada &&
            !o.SolicitudesCancelacion.Any(s => s.EsperaAlmacenes) &&
            o.SolicitudesCancelacion.Any(s => s.FechaResolucion == null && s.SolicitanteId != usuarioId));
    public static IQueryable<Requisicion> RequisicionesAccionables(IQueryable<Requisicion> query,
        Guid usuarioId, bool nivel1, bool nivel2) => Requisiciones(query, nivel1, nivel2)
        .Where(r => r.CreadorId != usuarioId &&
            !r.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1 && a.UsuarioId == usuarioId));
}
