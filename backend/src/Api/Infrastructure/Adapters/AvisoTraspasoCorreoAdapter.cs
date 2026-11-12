using System.Net;
using Millet.Almacen.Domain.Ports;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="IAvisoTraspasoPort"/> (M-249): correo a los usuarios de la sucursal destino con el permiso de recibir (resueltos por
/// <see cref="IDestinatariosAvisoReadPort"/>, que vive en Identidad) a su correo para avisos. Vive en Api porque PT no referencia Almacén.
/// Sin destinatarios no se envía nada; el rechazo del proveedor de correo queda en <c>core.correo_envio</c>.
/// </summary>
public sealed class AvisoTraspasoCorreoAdapter(IDestinatariosAvisoReadPort destinatarios, IUsuarioCorreoAvisosPort correos, ICorreoSaliente correo) : IAvisoTraspasoPort
{
    public async Task AvisarPorRecibirAsync(AvisoTraspasoPorRecibir aviso, CancellationToken ct)
    {
        var usuarios = await destinatarios.UsuariosConPermisoAsync(aviso.EmpresaId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, aviso.SucursalDestinoId, ct);
        if (usuarios.Count == 0) return;
        var para = (await correos.ObtenerCorreosAvisosAsync(usuarios.ToList(), ct)).Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (para.Count == 0) return;
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   "<h2>Traspaso pendiente por recibir</h2>" +
                   $"<p>El traspaso <strong>{WebUtility.HtmlEncode(aviso.Folio)}</strong> con {aviso.Piezas} pieza(s) salió hacia su sucursal y espera su recepción.</p>" +
                   "<p>Recíbelo en el ERP, Producto terminado › Traspasos › Por recibir.</p></div>";
        await correo.EnviarAsync(new MensajeCorreo(aviso.EmpresaId, "producto_terminado", "traspaso-por-recibir", aviso.TraspasoId, para, $"Traspaso {aviso.Folio} por recibir", html), ct);
    }
}
