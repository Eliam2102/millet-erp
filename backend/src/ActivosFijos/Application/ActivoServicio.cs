using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Administracion.Application.Abstractions;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;
public sealed class ActivoServicio(ActivosFijosDbContext db, ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, ICuentaContableReadPort cuentas, IPolizaContablePort polizas, ICatalogosActivoReadPort catalogos, ICentroCostoContabilidadPort centros)
{
    public Guid Actor => user.UserId ?? throw new UnauthorizedAccessException();
    public async Task PermisoAsync(string permiso, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(permiso, ct)) throw new ForbiddenException("AF_PERMISO_REQUERIDO", "No tiene permiso para esta acción.");
    }
    public Task SucursalAsync(Guid sucursal, bool escribir, CancellationToken ct) => SucursalScopeGuard.VerificarAsync(user.UserId,
        escribir ? PermisosCanonicos.ActivosFijosGestionarTodasSucursales : PermisosCanonicos.ActivosFijosLeerTodasSucursales,
        permisos, (id, c) => sucursales.EstaAsociadoAsync(id, sucursal, c), ct);
    public async Task<IReadOnlyList<Guid>?> SucursalesAsync(Guid? seleccion, CancellationToken ct)
    {
        if (seleccion is Guid id) { await SucursalAsync(id, false, ct); return [id]; }
        return await permisos.TieneAsync(PermisosCanonicos.ActivosFijosLeerTodasSucursales, ct) ? null : await sucursales.ListarIdsAsync(Actor, ct);
    }
    public async Task<Activo> CargarAsync(Guid id, bool escribir, CancellationToken ct)
    {
        var a = await db.Activos.Include(x => x.Historial).Include(x => x.Traslados).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("AF_NO_EXISTE", "No existe el activo.");
        await SucursalAsync(a.SucursalId, escribir, ct);
        a.CustodiaDescripcion = await catalogos.DescribirAsync(a.Custodia(), ct);
        foreach (var traslado in a.Traslados)
        {
            traslado.RegistradoPorNombre = (await catalogos.DescribirAsync(traslado.Origen with { ResponsableId = traslado.RegistradoPor }, ct)).Responsable;
            traslado.OrigenDescripcion = await catalogos.DescribirAsync(traslado.Origen, ct);
            traslado.DestinoDescripcion = await catalogos.DescribirAsync(traslado.Destino, ct);
        }
        return a;
    }
    public async Task<ClaseActivo> ClaseAsync(Guid id, CancellationToken ct) => await db.Clases.SingleOrDefaultAsync(x => x.Id == id && x.Activa, ct)
        ?? throw new BusinessRuleException("AF_CLASE_INACTIVA", "Seleccione una clase activa.");
    public async Task ValidarTasasActivoAsync(DatosActivo datos, Guid? id, CancellationToken ct)
    {
        var clase = await ClaseAsync(datos.ClaseId, ct);
        if ((datos.VidaUtilMeses != null && clase.Clave != "160") || datos.VidaUtilMeses is < 1 or > 600
            || (datos.ActivoMejoradoId != null && clase.Clave != "170"))
            throw new BusinessRuleException("AF_TASA_ACTIVO_INVALIDA", "Vida útil de 1 a 600 meses sólo para clase 160; activo mejorado sólo para clase 170.");
        if (datos.ActivoMejoradoId is Guid mejorado)
        {
            var destino = await db.Activos.Join(db.Clases, a => a.ClaseId, c => c.Id, (a, c) => new { Activo = a, c.Clave })
                .SingleOrDefaultAsync(x => x.Activo.Id == mejorado && x.Activo.Estado == EstadoActivo.Activo && x.Clave != "170", ct);
            if (mejorado == id || destino == null)
                throw new BusinessRuleException("AF_MEJORADO_INVALIDO", "Seleccione otro activo vigente de la misma empresa que no sea una mejora.");
            await SucursalAsync(destino.Activo.SucursalId, false, ct);
        }
    }
    public async Task ValidarCuentasAsync(ClaseActivoDatos d, CancellationToken ct)
    {
        foreach (var id in new[] { d.CuentaActivoId, d.CuentaDepreciacionId, d.CuentaGastoId, d.CuentaPuenteId }.OfType<Guid>())
            if (!(await cuentas.ValidarParaMovimientoAsync(id, OrigenMovimiento.AuxiliarCxP, ct)).Valida)
                throw new BusinessRuleException("AF_CUENTA_INVALIDA", "La cuenta no admite movimientos en Contabilidad.");
    }
    public async Task<Guid> PolizaAsync(Activo a, ClaseActivo clase, Guid documento, DateOnly fecha, decimal importe, string llave, string concepto, CancellationToken ct)
    {
        if (clase.CuentaActivoId is not Guid activo || clase.CuentaPuenteId is not Guid puente || activo == puente)
            throw new BusinessRuleException("AF_CUENTAS_PENDIENTES", "Configure cuentas distintas de activo y puente; las clases DEMO requieren validación contable.");
        var nodos = await centros.ObtenerAsync([a.EquipoId ?? a.CentroCostoId], ct);
        if (!nodos.TryGetValue(a.EquipoId ?? a.CentroCostoId, out var nodo) || !nodo.ActivoEnCadena)
            throw new BusinessRuleException("AF_CENTRO_INVALIDO", "El centro de costo no está activo.");
        var positivo = importe > 0;
        var monto = Math.Abs(importe);
        var resultado = await polizas.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica("AF", fecha, concepto,
            new DocumentoOrigenPoliza("ActivosFijos", "Capitalizacion", documento, a.Numero, null), llave,
            [new PartidaRequest(activo, concepto, positivo ? monto : 0, positivo ? 0 : monto, a.SucursalId, Dim1Id: nodo.Dim1Id, Dim2Id: nodo.Dim2Id, Dim3Id: nodo.Nivel == DimensionContable.Dim3 ? nodo.Id : null),
             new PartidaRequest(puente, concepto, positivo ? 0 : monto, positivo ? monto : 0, a.SucursalId, Dim1Id: nodo.Dim1Id, Dim2Id: nodo.Dim2Id, Dim3Id: nodo.Nivel == DimensionContable.Dim3 ? nodo.Id : null,
                 // K10.5 (E7): la capitalización salda la puente de la OC del activo.
                 Puente: a.OrdenCompraId is Guid oc ? new ReferenciaPuente(oc, a.LineaOcId, TipoDocumentoPuente.CapitalizacionAf, documento) : null)], OrigenMovimiento.AuxiliarCxP), ct);
        return resultado.Id;
    }
}
