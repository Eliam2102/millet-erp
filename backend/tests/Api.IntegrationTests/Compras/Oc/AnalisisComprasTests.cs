using Millet.DatosMaestros.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Almacen.Domain.Catalogo;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Application.Oc.Analisis;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Compras.Oc;

public partial class OrdenesCompraEndpointsTests
{
    // DEMO ficticio: almacén y CxP reales, aislado de otras pruebas por departamento único.
    [Fact]
    public async Task COM13_abcde_Trimestre_con_devolucion_factura_pago_compromiso_y_sustento()
    {
        var client = await CreateSuperAdminClientAsync();
        var usuario = await UsuarioIdDeAsync(client);
        var (ocId, _) = await SembrarP2(client, recepcion: false);
        Guid depto, recepcionId, devolucionId, lineaId;
        decimal compra;
        var enero = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero);
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = sp.GetRequiredService<ComprasDbContext>();
            var oc = await compras.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
            var l = oc.Lineas.Single(); lineaId = l.Id; depto = l.DepartamentoSolicitanteId;
            // El análisis convierte cantidades con el catálogo: la partida necesita un artículo real.
            var catalogos = sp.GetRequiredService<CompartidoDbContext>();
            var pza = await catalogos.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var articulo = new Articulo(Guid.NewGuid(), $"C13A{Guid.NewGuid():N}"[..16], "DEMO COM-13 artículo ficticio", "PZA", unidadMedidaId: pza.Id);
            catalogos.Set<Articulo>().Add(articulo);
            await catalogos.SaveChangesAsync();
            compras.Entry(l).Property(x => x.ArticuloId).CurrentValue = articulo.Id;
            compras.Entry(oc).Property(o => o.FechaDocumento).CurrentValue = new(2026, 1, 1);
            compras.Entry(oc).Property(o => o.FechaContabilizacion).CurrentValue = enero;
            compras.Entry(oc).Property(o => o.FechaEntregaEsperada).CurrentValue = new(2026, 1, 10);
            var incidencia = oc.RegistrarIncidenciaProveedor(l.Id, TipoIncidenciaProveedorOc.Atraso, 10,
                new(2026, 1, 12), usuario, "DEMO compromiso ficticio", new(2026, 1, 5), usuario, enero);
            oc.ReprogramarIncidenciaProveedor(incidencia.Id, new(2026, 1, 20), "DEMO reprogramación ficticia",
                new(2026, 1, 6), usuario, enero.AddDays(1));
            compra = oc.CalcularTotales().TotalAPagar;
            await compras.SaveChangesAsync();

            var almacen = sp.GetRequiredService<AlmacenDbContext>();
            // Las entradas exigen ubicación real (la ÚNICA del sub-almacén sólo se drena).
            var subAlmacenId = await (from s in almacen.SubAlmacenes join a in almacen.Almacenes on s.AlmacenId equals a.Id
                where a.SucursalId == oc.SucursalDestinoId select s.Id).FirstAsync();
            var rack = new Ubicacion(Guid.NewGuid(), subAlmacenId, "DEMO-COM13-R1", "Rack DEMO COM-13");
            almacen.Ubicaciones.Add(rack);
            await almacen.SaveChangesAsync();
            var ubicacion = new { rack.Id, SubId = subAlmacenId };
            var rec = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, oc.EmpresaId, new(2026, 1, 15));
            var lr = new LineaMovimiento(Guid.NewGuid(), rec.Id, 1, l.ArticuloId, 10, "PZA", 100,
                ubicacionId: ubicacion.Id, lineaOcId: l.Id);
            rec.AgregarLinea(lr);
            rec.Registrar(new FolioMovimiento("DEMO-REC-" + Guid.NewGuid().ToString("N")[..12]), usuario);
            almacen.Entry(rec).Property(m => m.OcId).CurrentValue = ocId;
            almacen.Movimientos.Add(rec); await almacen.SaveChangesAsync(); recepcionId = rec.Id;
            var salida = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaPorDevolucionAProveedor,
                oc.EmpresaId, new(2026, 2, 1));
            salida.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), salida.Id, 1, l.ArticuloId, 2, "PZA", 100, ubicacionId: ubicacion.Id));
            salida.Registrar(new FolioMovimiento("DEMO-DEV-" + Guid.NewGuid().ToString("N")[..12]), usuario);
            almacen.Movimientos.Add(salida); await almacen.SaveChangesAsync();
            var dev = new DevolucionAProveedor(Guid.NewGuid(), oc.EmpresaId, oc.ProveedorId, "DEMO devolución ficticia", usuario,
                recepcionOrigenId: rec.Id, ordenCompraOrigenId: ocId, subAlmacenOrigenId: ubicacion.SubId);
            dev.AgregarLinea(new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, l.ArticuloId, 2, "PZA", 100, lr.Id));
            dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.NewGuid(), dev.Id, "foto", "DEMO.txt", "demo/ficticio.txt"));
            dev.SolicitarAutorizacion(); dev.Autorizar(usuario); dev.MarcarRegistrada(salida.Id, salida.Folio!);
            almacen.DevolucionesProveedor.Add(dev); await almacen.SaveChangesAsync(); devolucionId = dev.Id;

            var factura = FacturaProveedor.CapturarConOc(oc.EmpresaId, null, null, oc.ProveedorId, oc.SucursalDestinoId,
                "DEMO-FACT", null, enero.AddDays(20), enero.AddDays(20), new(2026, 3, 1), "MXN", null,
                606, 0, 0, 0, 606, ocId, usuario, Tolerancia.MontoAbsoluto(10), 6, 0, enero.AddDays(20));
            factura.AgregarLinea(l.ArticuloId, null, "DEMO artículo ficticio", 6, "H87", "PZA", 101, 606, null, l.Id, null);
            factura.Autorizar(usuario, enero.AddDays(21));
            factura.RegistrarPago(200, enero.AddDays(25), "DEMO pago ficticio");
            factura.RegistrarPago(100, new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), "DEMO pago posterior al corte");
            // Siembra histórica para probar lectura, independiente del calendario operativo de la base del revisor.
            await using var cxp = new CuentasPorPagarDbContext(sp.GetRequiredService<DbContextOptions<CuentasPorPagarDbContext>>(),
                sp.GetRequiredService<ICurrentEmpresaContext>(), new PeriodoDemoAnalisis(), sp.GetRequiredService<IClock>());
            cxp.FacturasProveedor.Add(factura); await cxp.SaveChangesAsync();
        }
        var url = $"{EndpointBase}/analisis?desde=2026-01-01&hasta=2026-03-31&departamentoId={depto}";
        var response = await client.GetAsync(url); response.EnsureSuccessStatusCode();
        var reporte = (await response.Content.ReadFromJsonAsync<AnalisisComprasResultado>())!;
        var p = Assert.Single(reporte.Partidas); var proveedor = Assert.Single(reporte.Proveedores);
        Assert.Equal(ocId, p.OrdenCompraId); Assert.Equal(lineaId, p.LineaId);
        Assert.Equal(compra, proveedor.ImporteCompra); Assert.Equal(compra, reporte.Partidas.Sum(x => x.ImporteCompra));
        Assert.Equal(0, proveedor.PorcentajeATiempo); Assert.Equal(0, proveedor.PorcentajeCompleto); Assert.Equal(1, proveedor.Incidencias);
        Assert.Equal(8, p.RecibidoNeto); Assert.Equal(2, p.PorRecibir); Assert.Equal(4, p.PorFacturar);
        Assert.Equal(406, p.PorPagar); Assert.Equal(6, p.DiferenciaPrecio); Assert.Equal(70, p.DiasAtraso);
        Assert.Equal(new DateOnly(2026, 1, 20), p.Compromiso);
        Assert.Contains(p.Movimientos, m => m.DocumentoId == recepcionId && !m.EsDevolucion);
        Assert.Contains(p.Movimientos, m => m.DocumentoId == devolucionId && m.EsDevolucion);
        Assert.Contains(lineaId, proveedor.LineasIds);
        foreach (var filtro in new[] { $"&proveedorId={Guid.NewGuid()}", "&articuloId=" + Guid.NewGuid() })
            Assert.Empty((await client.GetFromJsonAsync<AnalisisComprasResultado>(url + filtro))!.Proveedores);
        Assert.Empty((await client.GetFromJsonAsync<AnalisisComprasResultado>(url.Replace("desde=2026-01-01", "desde=2026-02-01")))!.Proveedores);
        var eneroReporte = (await client.GetFromJsonAsync<AnalisisComprasResultado>(url.Replace("hasta=2026-03-31", "hasta=2026-01-31")))!;
        Assert.Equal(100, Assert.Single(eneroReporte.Proveedores).PorcentajeATiempo);
        Assert.Equal(10, Assert.Single(eneroReporte.Partidas).RecibidoNeto);
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            // La auditoría está particionada por mes desde mayo de 2026; la base desechable
            // necesita la partición de abril para el registro histórico ficticio.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS core.audit_log_y2026m04 PARTITION OF core.audit_log " +
                "FOR VALUES FROM ('2026-04-01 00:00:00+00') TO ('2026-05-01 00:00:00+00')");
            db.Entry(oc).Property(o => o.Estado).CurrentValue = EstadoOrdenCompra.Cancelada;
            db.Set<AuditLogEntry>().Add(new AuditLogEntry { Id = Guid.NewGuid(), EmpresaId = oc.EmpresaId,
                EntidadId = ocId, AggregateRootId = ocId, Modulo = "Compras", Entidad = "OrdenCompra", Operacion = "Actualizar",
                Timestamp = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero), CorrelationId = Guid.NewGuid(),
                Cambios = "{\"diff\":{\"Estado\":{\"antes\":3,\"despues\":5}}}" });
            await db.SaveChangesAsync();
        }
        Assert.Single((await client.GetFromJsonAsync<AnalisisComprasResultado>(url))!.Partidas);
        Assert.Empty((await client.GetFromJsonAsync<AnalisisComprasResultado>(url.Replace("hasta=2026-03-31", "hasta=2026-04-02")))!.Partidas);

    }

    [Fact]
    public async Task COM13_Acceso_por_permiso_y_sucursales_no_es_controlado_por_cliente()
    {
        var sinReporte = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);
        var operativo = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesReportesPartidasAbiertas);
        var usuario = await UsuarioIdDeAsync(operativo);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            identidad.UsuarioSucursales.RemoveRange(await identidad.UsuarioSucursales.Where(s => s.UsuarioId == usuario).ToListAsync());
            await identidad.SaveChangesAsync();
        }
        const string url = EndpointBase + "/analisis?desde=2026-01-01&hasta=2026-03-31";
        Assert.Equal(HttpStatusCode.Forbidden, (await sinReporte.GetAsync(url)).StatusCode);
        var respuesta = await operativo.GetAsync(url); respuesta.EnsureSuccessStatusCode();
        Assert.Empty((await respuesta.Content.ReadFromJsonAsync<AnalisisComprasResultado>())!.Partidas);
        Assert.Equal(HttpStatusCode.Forbidden, (await operativo.GetAsync(url + "&sucursalId=" + Guid.NewGuid())).StatusCode);
    }
    private sealed class PeriodoDemoAnalisis : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
