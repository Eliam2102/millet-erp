using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;
public sealed class CrearActivoHandler(ActivosFijosDbContext db, ActivoServicio servicio, ICatalogosActivoReadPort catalogos, ICurrentEmpresaContext empresa) : IRequestHandler<CrearActivoCommand, Activo>
{
    public async Task<Activo> Handle(CrearActivoCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosActivosGestionar, cancellationToken);
        await servicio.SucursalAsync(request.Datos.Custodia.SucursalId, true, cancellationToken);
        await catalogos.ValidarCustodiaAsync(request.Datos.Custodia, false, cancellationToken);
        var clase = await servicio.ClaseAsync(request.Datos.ClaseId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serializa el consecutivo por empresa y clase; el índice único protege también la carga histórica.
        var llave = clase.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", cancellationToken);
        var numero = await NumeroActivo.ReservarAsync(db, clase, cancellationToken);
        var a = new Activo(Guid.CreateVersion7(), empresa.Current ?? throw new UnauthorizedAccessException(), numero, request.Datos);
        db.Activos.Add(a); await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return a;
    }
}
public sealed class ListarActivosHandler(ActivosFijosDbContext db, ActivoServicio servicio, ICatalogosActivoReadPort catalogos) : IRequestHandler<ListarActivosQuery, ActivosPagina>
{
    public async Task<ActivosPagina> Handle(ListarActivosQuery request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosActivosVer, cancellationToken);
        var ids = await servicio.SucursalesAsync(request.SucursalId, cancellationToken);
        var responsables = await catalogos.ResponsablesAsync(servicio.Actor, cancellationToken);
        var q = db.Activos.AsNoTracking().Where(x => ids == null || ids.Contains(x.SucursalId) || x.Traslados.Any(t => t.AceptadoEn == null && responsables.Contains(t.Destino.ResponsableId) && ids.Contains(t.Destino.SucursalId)));
        if (request.ClaseId != null) q = q.Where(x => x.ClaseId == request.ClaseId);
        if (request.Estado != null) q = q.Where(x => x.Estado == request.Estado);
        if (request.ResponsableId != null) q = q.Where(x => x.ResponsableId == request.ResponsableId);
        if (request.EquipoId != null) q = q.Where(x => x.EquipoId == request.EquipoId);
        return new(await q.Include(x => x.Traslados).OrderBy(x => x.Numero).Skip(Math.Max(0, request.Offset)).Take(Math.Clamp(request.Limit, 1, 100)).ToListAsync(cancellationToken), await q.CountAsync(cancellationToken));
    }
}
public sealed class ObtenerActivoHandler(ActivoServicio servicio) : IRequestHandler<ObtenerActivoQuery, Activo>
{
    public async Task<Activo> Handle(ObtenerActivoQuery request, CancellationToken cancellationToken)
    { await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosActivosVer, cancellationToken); return await servicio.CargarAsync(request.Id, false, cancellationToken, aceptar: true); }
}
public sealed class MutarActivoHandler(ActivosFijosDbContext db, ActivoServicio servicio, ICatalogosActivoReadPort catalogos, IClock clock, Millet.CuentasPorPagar.Application.PublicPorts.ICapitalizacionProveedorReadPort proveedores, ITransaccionPolizaActivo transaccionPoliza) : IRequestHandler<MutarActivoCommand, Activo>
{
    public async Task<Activo> Handle(MutarActivoCommand request, CancellationToken cancellationToken)
    {
        var permiso = request.Accion switch { "capitalizar" or "mejora" or "nota-credito" => PermisosCanonicos.ActivosFijosCapitalizar,
            "trasladar" or "aceptar" => PermisosCanonicos.ActivosFijosTrasladosGestionar, "editar" => PermisosCanonicos.ActivosFijosActivosGestionar,
            _ => throw new BusinessRuleException("AF_ACCION_INVALIDA", "Acción desconocida.") };
        await servicio.PermisoAsync(permiso, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var llave = request.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", cancellationToken);
        await using var enlaceContable = request.Accion is "capitalizar" or "mejora" or "nota-credito" ? await transaccionPoliza.VincularAsync(cancellationToken) : null;
        var a = await servicio.CargarAsync(request.Id, true, cancellationToken, aceptar: request.Accion == "aceptar");
        if (a.Version != request.Version) throw new ConcurrencyException("Activo", a.Id);
        switch (request.Accion)
        {
            case "editar":
                var datos = request.Datos ?? throw new BusinessRuleException("AF_DATOS_REQUERIDOS", "Capture los datos.");
                await servicio.SucursalAsync(datos.Custodia.SucursalId, true, cancellationToken);
                await catalogos.ValidarCustodiaAsync(datos.Custodia, false, cancellationToken); await servicio.ClaseAsync(datos.ClaseId, cancellationToken); a.Completar(datos); break;
            case "capitalizar":
                if (a.Estado == EstadoActivo.Activo) break;
                a.ValidarCapitalizacion(); await catalogos.ValidarCustodiaAsync(a.Custodia(), true, cancellationToken);
                var clase = await servicio.ClaseAsync(a.ClaseId, cancellationToken);
                a.Capitalizar(await servicio.PolizaAsync(a, clase, a.Id, a.InicioUso!.Value, a.ValorOriginal, $"AF-CAP-{a.Id}", $"Alta {a.Numero}", cancellationToken)); break;
            case "mejora":
            case "nota-credito":
                var ajuste = request.Ajuste ?? throw new BusinessRuleException("AF_AJUSTE_REQUERIDO", "Capture el ajuste.");
                if (a.Historial.SingleOrDefault(x => x.DocumentoId == ajuste.DocumentoId) is { } previo)
                {
                    a.Ajustar(ajuste.DocumentoId, ajuste.FacturaId, ajuste.Fecha, ajuste.Importe, request.Accion == "nota-credito", ajuste.Descripcion, previo.PolizaId);
                    break;
                }
                var credito = request.Accion == "nota-credito";
                var docLlave = ajuste.DocumentoId.ToString();
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({docLlave}, 0))", cancellationToken);
                var soporte = await proveedores.ObtenerAsync(ajuste.DocumentoId, ajuste.FacturaId, credito, cancellationToken)
                    ?? throw new BusinessRuleException("AF_DOCUMENTO_PROVEEDOR_INVALIDO", "Seleccione una factura vigente o una nota de crédito ligada a la factura origen.");
                var aplicado = await db.Set<MovimientoValorActivo>().Where(x => x.DocumentoId == ajuste.DocumentoId).SumAsync(x => Math.Abs(x.Importe), cancellationToken);
                if (!credito) aplicado += await db.Activos.Where(x => x.FacturaProveedorId == ajuste.DocumentoId).SumAsync(x => x.ValorOriginal, cancellationToken);
                if (aplicado + ajuste.Importe > soporte.BaseMxn) throw new BusinessRuleException("AF_DOCUMENTO_EXCEDIDO", "El importe supera la base disponible del documento del proveedor.");
                a.ValidarAjuste(ajuste.DocumentoId, ajuste.FacturaId, ajuste.Importe, credito, ajuste.Descripcion);
                var poliza = await servicio.PolizaAsync(a, await servicio.ClaseAsync(a.ClaseId, cancellationToken), ajuste.DocumentoId, ajuste.Fecha,
                    credito ? -ajuste.Importe : ajuste.Importe, $"AF-{(credito ? "NC" : "MEJ")}-{a.Id}-{ajuste.DocumentoId}", ajuste.Descripcion, cancellationToken);
                a.Ajustar(ajuste.DocumentoId, ajuste.FacturaId, ajuste.Fecha, ajuste.Importe, credito, ajuste.Descripcion, poliza); break;
            case "trasladar":
                var destino = request.Destino ?? throw new BusinessRuleException("AF_DESTINO_REQUERIDO", "Capture el destino.");
                await catalogos.ValidarCustodiaAsync(destino, true, cancellationToken);
                a.Trasladar(Guid.CreateVersion7(), destino, servicio.Actor, clock.UtcNow); break;
            case "aceptar":
                var receptor = a.Traslados.SingleOrDefault(x => x.Id == request.TrasladoId)?.Destino.ResponsableId ?? Guid.Empty;
                if (!(await catalogos.ResponsablesAsync(servicio.Actor, cancellationToken)).Contains(receptor)) throw new ForbiddenException("AF_RECEPTOR_REQUERIDO", "Solo el receptor puede aceptar la custodia.");
                a.AceptarTraslado(request.TrasladoId ?? Guid.Empty, receptor, clock.UtcNow); break;
        }
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return a;
    }
}
public sealed class GuardarClaseHandler(ActivosFijosDbContext db, ActivoServicio servicio, ICurrentEmpresaContext empresa) : IRequestHandler<GuardarClaseCommand, ClaseActivo>
{
    public async Task<ClaseActivo> Handle(GuardarClaseCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ActivosFijosClasesAdministrar, cancellationToken); await servicio.ValidarCuentasAsync(request.Datos, cancellationToken);
        var d = request.Datos;
        var c = request.Id is Guid id ? await db.Clases.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new EntityNotFoundException("AF_CLASE_NO_EXISTE", "No existe la clase.")
            : new ClaseActivo(Guid.CreateVersion7(), empresa.Current ?? throw new UnauthorizedAccessException(), d.Clave, d.Nombre, d.TasaFiscalAnual);
        if (request.Id != null && c.Version != request.Version) throw new ConcurrencyException("ClaseActivo", c.Id);
        if (await db.Clases.AnyAsync(x => x.Clave == c.Clave && x.Id != c.Id, cancellationToken)) throw new BusinessRuleException("AF_CLAVE_DUPLICADA", "Ya existe esta clave.");
        c.Editar(d.Nombre, d.TasaFiscalAnual, d.CuentaActivoId, d.CuentaDepreciacionId, d.CuentaGastoId, d.CuentaPuenteId, d.Activa);
        if (request.Id == null) db.Clases.Add(c);
        await db.SaveChangesAsync(cancellationToken); return c;
    }
}
