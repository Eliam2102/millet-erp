using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.Web;
public sealed class DocumentoSucursalScope(ICurrentUserContext user, ICurrentUserPermissions permisos,
    IUsuarioSucursalReadPort sucursales, IFacturacionSucursalReadPort facturacion,
    ICxpSucursalReadPort cxp, ICxcSucursalReadPort cxc, ITesoreriaSucursalReadPort tesoreria, IComprasSucursalReadPort compras, IAlmacenSucursalReadPort almacen)
{
    public Task VerificarSucursalAsync(Guid? sucursalId, string permisoTodas, CancellationToken ct)
        => SucursalScopeGuard.VerificarAsync(user.UserId, permisoTodas, permisos,
            (uid, c) => sucursalId is Guid id ? sucursales.EstaAsociadoAsync(uid, id, c) : Task.FromResult(false), ct);
    public IDocumentoSucursalReadPort Puerto(string tipo) => tipo switch
    {
        "cfdi_recibido" or "movimiento_tc" or "estado_cuenta_tc" or "anticipo_proveedor" or "nota_credito_proveedor" or "factura_proveedor" or "nota_cargo" or "comprobacion" or "reposicion_caja" => cxp,
        "requisicion" or "orden_compra" => compras,
        "recepcion" or "salida_almacen" or "reorden" or "ubicacion_almacen" or "almacen" => almacen,
        "comprobante" or "sesion_caja" => facturacion,
        "cliente_cartera" or "factura_cartera" or "propuesta_cxc" or "seguimiento_cobranza" or "alerta_cartera" => cxc,
        _ => tesoreria,
    };
    public static bool Permitido(DocumentoSucursales documento, IReadOnlyList<Guid> permitidas)
        => documento.Sucursales.Count > 0 && documento.Sucursales.All(permitidas.Contains);
    public async Task VerificarAsync(string tipo, Guid id, string permisoTodas, CancellationToken ct)
    {
        if (await permisos.TieneAsync(permisoTodas, ct)) return;
        var documento = (await Puerto(tipo).ListarAsync(tipo, ct)).FirstOrDefault(x => x.Id == id);
        if (documento is null || documento.Sucursales.Count == 0)
            throw new ForbiddenException("SUCURSAL_NO_DETERMINADA",
                "Este documento no tiene una sucursal verificable. Requiere un usuario con alcance corporativo.");
        foreach (var sucursal in documento.Sucursales)
            await SucursalScopeGuard.VerificarAsync(user.UserId, permisoTodas, permisos,
                (uid, c) => sucursales.EstaAsociadoAsync(uid, sucursal, c), ct);
    }
}
