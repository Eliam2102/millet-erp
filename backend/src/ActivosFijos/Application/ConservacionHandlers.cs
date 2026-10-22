using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed record RegistrarDocumentoActivo(Guid Id, int Version, DocumentoActivoDatos Datos) : IRequest<DocumentoActivo>;
public sealed record RegistrarMantenimientoActivo(Guid Id, int Version, MantenimientoDatos Datos) : IRequest<MantenimientoActivo>;
public sealed record AbrirConteoActivo(Guid SucursalId, string? Ubicacion) : IRequest<ConteoActivo>;
public sealed record CapturaConteoDatos(string? Numero, ResultadoConteo Resultado, string? UbicacionReal, string Evidencia);
public sealed record MutarConteoActivo(Guid Id, int Version, string Accion, CapturaConteoDatos? Captura = null, Guid? PartidaId = null, string? Evidencia = null) : IRequest<ConteoActivo>;
public sealed class ConservacionHandlers(ActivosFijosDbContext db, ActivoServicio servicio, IConservacionReadPort lectura, ICapitalizacionProveedorReadPort facturas, ICurrentEmpresaContext empresa, ICatalogosActivoReadPort catalogos, IClock clock) :
    IRequestHandler<RegistrarDocumentoActivo, DocumentoActivo>, IRequestHandler<RegistrarMantenimientoActivo, MantenimientoActivo>, IRequestHandler<AbrirConteoActivo, ConteoActivo>, IRequestHandler<MutarConteoActivo, ConteoActivo>
{
    public async Task<DocumentoActivo> Handle(RegistrarDocumentoActivo request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosDocumentosGestionar, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await BloquearAsync(request.Id, cancellationToken);
        var a = await servicio.CargarAsync(request.Id, true, cancellationToken); Version(a, request.Version);
        await lectura.ValidarAdjuntoAsync(a.Id, request.Datos, cancellationToken);
        var previo = request.Datos.ReemplazaA is Guid id ? await db.Documentos.SingleOrDefaultAsync(x => x.Id == id && x.ActivoId == a.Id, cancellationToken) : null;
        var d = new DocumentoActivo(a, request.Datos, previo);
        db.Documentos.Add(d); Tocar(a);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return d;
    }
    public async Task<MantenimientoActivo> Handle(RegistrarMantenimientoActivo request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosMantenimientosGestionar, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await BloquearAsync(request.Id, cancellationToken);
        var a = await servicio.CargarAsync(request.Id, true, cancellationToken); Version(a, request.Version);
        await lectura.ValidarProveedorAsync(request.Datos.ProveedorId, cancellationToken);
        if (request.Datos.FacturaProveedorId is Guid factura && !await facturas.ValidarMantenimientoAsync(factura, request.Datos.ProveedorId, a.SucursalId, cancellationToken))
            throw new BusinessRuleException("AF_FACTURA_MANTENIMIENTO_INVALIDA", "Seleccione una factura vigente del proveedor y de la sucursal del activo.");
        var h = new MantenimientoActivo(a, request.Datos); db.Mantenimientos.Add(h); Tocar(a);
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return h;
    }
    public async Task<ConteoActivo> Handle(AbrirConteoActivo request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosConteosGestionar, cancellationToken); await servicio.SucursalAsync(request.SucursalId, true, cancellationToken);
        await catalogos.ValidarCustodiaAsync(new CustodiaActivo(Guid.Empty, request.SucursalId, request.Ubicacion ?? "", Guid.Empty, null), false, cancellationToken);
        var ubicacion = string.IsNullOrWhiteSpace(request.Ubicacion) ? null : request.Ubicacion.Trim();
        var esperados = await db.Activos.AsNoTracking().Where(x => x.SucursalId == request.SucursalId && x.Estado != EstadoActivo.DadoDeBaja && (ubicacion == null || x.Ubicacion == ubicacion)).ToListAsync(cancellationToken);
        var conteo = new ConteoActivo(empresa.Current ?? throw new UnauthorizedAccessException(), request.SucursalId, ubicacion, servicio.Actor, esperados);
        db.Conteos.Add(conteo); await db.SaveChangesAsync(cancellationToken); return conteo;
    }
    public async Task<ConteoActivo> Handle(MutarConteoActivo request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(request.Accion == "autorizar" ? PermisosCanonicos.ActivosFijosConteosAutorizar : PermisosCanonicos.ActivosFijosConteosGestionar, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await BloquearAsync(request.Id, cancellationToken);
        var c = await db.Conteos.Include(x => x.Partidas).SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ?? throw new EntityNotFoundException("AF_CONTEO_NO_EXISTE", "No existe el conteo.");
        await servicio.SucursalAsync(c.SucursalId, true, cancellationToken); Version(c, request.Version);
        switch (request.Accion)
        {
            case "capturar":
                var d = request.Captura ?? throw new BusinessRuleException("AF_CAPTURA_REQUERIDA", "Capture el resultado.");
                c.Capturar(d.Numero, d.Resultado, d.UbicacionReal, d.Evidencia, servicio.Actor); break;
            case "cerrar": c.Cerrar(servicio.Actor, clock.UtcNow); break;
            case "autorizar": c.Autorizar(request.PartidaId ?? Guid.Empty, servicio.Actor, request.Evidencia ?? "", clock.UtcNow); break;
            default: throw new BusinessRuleException("AF_ACCION_INVALIDA", "Acción desconocida.");
        }
        Tocar(c); await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return c;
    }
    private Task<int> BloquearAsync(Guid id, CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", cancellationToken);
    private static void Version(Millet.SharedKernel.Domain.BaseEntity a, int version) { if (a.Version != version) throw new ConcurrencyException(a.GetType().Name, a.Id); }
    private void Tocar(Millet.SharedKernel.Domain.BaseEntity a) => db.Entry(a).Property(x => x.UpdatedAt).IsModified = true;
}
