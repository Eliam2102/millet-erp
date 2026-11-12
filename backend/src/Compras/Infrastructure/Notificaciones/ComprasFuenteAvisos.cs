using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Matriz;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Notificaciones;

namespace Millet.Compras.Infrastructure.Notificaciones;

/// <summary>Lee acciones vigentes; no autoriza ni cambia el estado de los documentos.</summary>
public sealed class ComprasFuenteAvisos(ComprasDbContext db, ICurrentUserPermissions permisos,
    IUsuarioSucursalReadPort sucursales, IResolverAutorizadorPort autorizadores,
    IRequiereNivelEvaluator niveles) : IFuenteAvisosUsuario
{
    public string Nombre => "compras";

    public async Task<IReadOnlyList<AvisoUsuario>> ConsultarAsync(ContextoAvisos contexto, CancellationToken ct)
    {
        var avisos = new List<AvisoUsuario>();
        var asociadas = await sucursales.ListarIdsAsync(contexto.UsuarioId, ct);
        if (await permisos.TieneAsync(PermisosCanonicos.ComprasOrdenesLeer, ct))
        {
            var gestionar = await permisos.TieneAsync("compras.ordenes.gestionar-todas-sucursales", ct);
            var leer = await permisos.TieneAsync(SucursalScopeGuardPermisos.OrdenesCompraLeerTodas, ct);
            var query = db.OrdenesCompra.AsNoTracking().Where(o => o.EmpresaId == contexto.EmpresaId &&
                (contexto.SucursalId == null || o.SucursalDestinoId == contexto.SucursalId) &&
                (gestionar || asociadas.Contains(o.SucursalDestinoId)) &&
                (leer || asociadas.Contains(o.SucursalDestinoId)));
            var n1 = await permisos.TieneAsync(PermisosCanonicos.ComprasOrdenesAutorizarNivel1, ct);
            var n2 = await permisos.TieneAsync(PermisosCanonicos.ComprasOrdenesAutorizarNivel2, ct);
            var firmar = PendientesAutorizacionLectura.OrdenesAccionables(query, contexto.UsuarioId, n1, n2);
            foreach (var oc in await firmar.ToListAsync(ct))
            {
                var paso = oc.Estado == EstadoOrdenCompra.EnAutorizacionJefeCompras ? "autorizar-n1" : "autorizar-n2";
                avisos.Add(Oc(oc, paso, oc.CicloAutorizacion.ToString(CultureInfo.InvariantCulture),
                    "Orden de compra por autorizar", paso == "autorizar-n1" ? "Requiere firma de Compras." : "Requiere firma de Dirección.", oc.CreatedAt));
            }
            if (n2)
                foreach (var oc in await PendientesAutorizacionLectura.CancelacionesAccionables(query, contexto.UsuarioId)
                    .Include(o => o.SolicitudesCancelacion).ToListAsync(ct))
                {
                    var solicitud = oc.SolicitudesCancelacion.Single(s => s.FechaResolucion == null);
                    avisos.Add(Oc(oc, "resolver-cancelacion", solicitud.Id.ToString("D"),
                        "Cancelación de orden por resolver", solicitud.MotivoSolicitud, solicitud.FechaSolicitud));
                }
            if (await permisos.TieneAsync(PermisosCanonicos.ComprasOrdenesCrear, ct))
                foreach (var oc in await query.Where(o => o.Estado == EstadoOrdenCompra.Rechazada &&
                    o.CompradorTitularId == contexto.UsuarioId).Include(o => o.Autorizaciones).ToListAsync(ct))
                    avisos.Add(Oc(oc, "corregir", oc.CicloAutorizacion.ToString(CultureInfo.InvariantCulture),
                        "Orden de compra devuelta a corrección", oc.MotivoRechazoTexto ?? "Revisa el motivo de rechazo y vuelve a transmitir.",
                        oc.Autorizaciones.Where(a => a.Ciclo == oc.CicloAutorizacion && a.Resultado == ResultadoAutorizacionOc.Rechazado)
                            .Select(a => a.FechaHora).DefaultIfEmpty(oc.CreatedAt).Max()));
        }
        if (await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesLeer, ct))
        {
            var gestionar = await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesGestionarTodasSucursales, ct);
            var leer = await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales, ct);
            var query = db.Requisiciones.AsNoTracking().Where(r => r.EmpresaId == contexto.EmpresaId &&
                (contexto.SucursalId == null || r.SucursalId == contexto.SucursalId) &&
                (gestionar || asociadas.Contains(r.SucursalId)) && (leer || asociadas.Contains(r.SucursalId)));
            var n1 = await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, ct);
            var n2 = await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesAutorizarNivel2, ct);
            var candidatas = PendientesAutorizacionLectura.RequisicionesAccionables(query, contexto.UsuarioId, n1, n2);
            foreach (var rq in await candidatas.Include(r => r.Autorizaciones).Include(r => r.Lineas).ToListAsync(ct))
            {
                var nivel = rq.Autorizaciones.Any(a => a.Nivel == NivelAutorizacion.Nivel1) ? NivelAutorizacion.Nivel2 : NivelAutorizacion.Nivel1;
                var facultado = nivel == NivelAutorizacion.Nivel1
                    ? await autorizadores.ResolverN1Async(rq, ct) : await autorizadores.ResolverN2Async(rq, ct);
                if (facultado is Guid id && id != contexto.UsuarioId) continue;
                if (nivel == NivelAutorizacion.Nivel2 && await niveles.EvaluarAsync(rq, ct) == RequiereNivel.SinPolitica) continue;
                var paso = nivel == NivelAutorizacion.Nivel1 ? "autorizar-n1" : "autorizar-n2";
                avisos.Add(Rq(rq, paso, rq.CicloAutorizacion.ToString(CultureInfo.InvariantCulture), "Requisición por autorizar",
                    nivel == NivelAutorizacion.Nivel1 ? "Requiere firma del jefe del departamento." : "Requiere firma del jefe de Almacén.",
                    nivel == NivelAutorizacion.Nivel2 ? rq.Autorizaciones.Where(a => a.Nivel == NivelAutorizacion.Nivel1).Max(a => a.FechaHora) : rq.FechaSolicitud));
            }
            if (await permisos.TieneAsync(PermisosCanonicos.ComprasRequisicionesEditar, ct))
                foreach (var rq in await query.Where(r => r.Estado == EstadoRequisicion.Borrador && r.FechaDevolucion != null &&
                    r.CreadorId == contexto.UsuarioId).ToListAsync(ct))
                    avisos.Add(Rq(rq, "corregir", rq.FechaDevolucion!.Value.ToString("O", CultureInfo.InvariantCulture),
                        "Requisición devuelta a corrección", rq.ComentarioDevolucion ?? "Corrige y vuelve a transmitir.", rq.FechaDevolucion.Value));
        }
        return avisos;
    }

    private static AvisoUsuario Oc(OrdenCompra oc, string paso, string ciclo, string titulo, string motivo, DateTimeOffset fecha) =>
        new(ClaveAviso.Crear(oc.EmpresaId, "compras", "orden-compra", oc.Id, paso, ciclo), "compras", "orden-compra", oc.Id,
            oc.Folio.Valor, titulo, motivo, fecha, "Accion", $"/compras/ordenes/{oc.Id:D}", oc.SucursalDestinoId);

    private static AvisoUsuario Rq(Requisicion rq, string paso, string ciclo, string titulo, string motivo, DateTimeOffset fecha) =>
        new(ClaveAviso.Crear(rq.EmpresaId, "compras", "requisicion", rq.Id, paso, ciclo), "compras", "requisicion", rq.Id,
            rq.Folio.Valor, titulo, motivo, fecha, "Accion", $"/compras/requisiciones/{rq.Id:D}", rq.SucursalId);
}
