using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Folios;
using Millet.Compras.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de <see cref="IComprasCrearRqAppPort"/> (contrato Almacenes v1 §8.1). Misma
/// mecánica que <see cref="ComprasCrearRqSistemaAdapter"/>: usuario de servicio (creador,
/// requisitante y empresa), depto de sistema, folio por <see cref="IFolioSecuenciaService"/> y
/// <c>Bypass()</c> acotado al guardado. La RQ nace Autorizada y se deja EnSurtido con
/// <c>CantidadDeCompra = piezas</c> para que Compras pueda crear la OC (sus handlers exigen EnSurtido).
/// <para>
/// Supuestos: el artículo se resuelve por <c>Articulo.ProductoAwId → ProductoAw.ReferenciaExterna ==
/// articuloAw</c>; la sucursal por <c>Sucursal.Clave</c> (o <c>ClaveAw</c>); sin almacén destino;
/// departamento = Reabastecimiento Automático (no hay depto propio de la app).
/// </para>
/// </summary>
public sealed class ComprasCrearRqAppAdapter : IComprasCrearRqAppPort
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _maestros;
    private readonly IFolioSecuenciaService _folios;
    private readonly IUsuarioServicioReadPort _usuarioServicio;
    private readonly ICurrentEmpresaContext _empresa;

    public ComprasCrearRqAppAdapter(ComprasDbContext db, CompartidoDbContext maestros,
        IFolioSecuenciaService folios, IUsuarioServicioReadPort usuarioServicio, ICurrentEmpresaContext empresa)
    {
        _db = db; _maestros = maestros; _folios = folios; _usuarioServicio = usuarioServicio; _empresa = empresa;
    }

    public async Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud s, CancellationToken ct)
    {
        var sp = await _usuarioServicio.ObtenerReordenAsync(ct);
        if (sp is null || !sp.Activo)
            return Error("El usuario de servicio del ERP no está disponible; no se puede crear la requisición.");

        using var bypass = _empresa.Bypass();

        var previo = await FolioPrevioAsync(sp.EmpresaId, s.FolioExterno, ct);
        if (previo is not null) return new(true, previo, null);

        var proveedorId = await _maestros.Proveedores.AsNoTracking()
            .Where(p => p.ClaveAw == s.ProveedorClaveAw).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (proveedorId is null)
            return Error($"No existe un proveedor con clave A+W {s.ProveedorClaveAw}.");

        var sucursal = await _maestros.Sucursales.AsNoTracking()
            .Where(x => x.Clave == s.SucursalCodigo || x.ClaveAw == s.SucursalCodigo)
            .Select(x => new { x.Id, x.Clave }).FirstOrDefaultAsync(ct);
        if (sucursal is null)
            return Error($"No existe la sucursal '{s.SucursalCodigo}'.");

        var claves = s.Lineas.Select(l => l.ArticuloAw).Distinct().ToList();
        var articulos = await (from a in _maestros.Articulos.AsNoTracking()
                               join p in _maestros.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                               where claves.Contains(p.ReferenciaExterna)
                               select new
                               {
                                   Clave = p.ReferenciaExterna, a.Id, a.UnidadMedidaDefault,
                                   a.PrecioReferenciaMonto, a.PrecioReferenciaMoneda,
                               }).ToListAsync(ct);
        var porClave = articulos.GroupBy(a => a.Clave).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var c in claves)
        {
            if (!porClave.TryGetValue(c, out var lista))
                return Error($"No existe un artículo ligado al artículo A+W {c}.");
            if (lista.Count > 1)
                return Error($"El artículo A+W {c} está ligado a más de un artículo del ERP.");
        }

        var folioAnio = (short)DateTimeOffset.UtcNow.Year;
        var folioStr = await _folios.SiguienteFolioRequisicionAsync(
            sp.EmpresaId, sucursal.Id, sucursal.Clave, folioAnio, ct);

        Requisicion rq;
        try
        {
            rq = Requisicion.CrearDesdeAlmacenesApp(
                Guid.CreateVersion7(), sp.EmpresaId, Folio.Parse(folioStr), folioAnio, sucursal.Id,
                DepartamentosSistema.ReabastecimientoAutomatico, null, sp.Id, sp.Id, DateTimeOffset.UtcNow,
                s.FolioExterno, proveedorId.Value,
                s.Lineas.Select(l =>
                {
                    var a = porClave[l.ArticuloAw][0];
                    return new LineaAppRequisicion(a.Id, l.Piezas, a.UnidadMedidaDefault,
                        Money.Of(a.PrecioReferenciaMonto ?? 0m, a.PrecioReferenciaMoneda ?? "MXN"),
                        l.Piezas, l.AnchoMm, l.AltoMm, s.FechaRequerida,
                        l.Obra is { Length: > 200 } ? l.Obra[..200] : l.Obra);
                }).ToList(),
                s.Comentario is { Length: > 500 } ? s.Comentario[..500] : s.Comentario);
            rq.RegistrarCubrimiento(
                rq.Lineas.Select(l => new CubrimientoLinea(l.Id, 0, l.Cantidad)).ToList(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is BusinessRuleException or DomainException)
        {
            return Error(ex.Message);
        }

        _db.Requisiciones.Add(rq);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Carrera con otro envío del mismo folio: gana el primero.
            _db.ChangeTracker.Clear();
            var ganador = await FolioPrevioAsync(sp.EmpresaId, s.FolioExterno, ct);
            if (ganador is null) throw;
            return new(true, ganador, null);
        }
        return new(false, rq.Folio.Valor, null);
    }

    private async Task<string?> FolioPrevioAsync(Guid empresaId, string folioExterno, CancellationToken ct)
    {
        var fila = await _db.Requisiciones.AsNoTracking()
            .Where(r => r.EmpresaId == empresaId && r.Origen == OrigenRequisicion.AlmacenesApp
                        && r.FolioExterno == folioExterno)
            .Select(r => new { r.Folio }).FirstOrDefaultAsync(ct);
        return fila?.Folio.Valor;
    }

    private static CrearRqAppResultado Error(string texto) => new(false, null, texto);
}
