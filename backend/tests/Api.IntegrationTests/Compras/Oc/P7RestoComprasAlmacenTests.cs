using MediatR;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Application.Autorizar;
using Millet.Compras.Application.Cancelar;
using Millet.Compras.Application.CerrarManual;
using Millet.Compras.Application.Oc.EnviarAAutorizacion;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// Documentos y ubicaciones propios; los catálogos compartidos usan el seed.
public partial class OrdenesCompraEndpointsTests
{
    [Theory]
    [InlineData("motivo", "OC_MOTIVO_SIN_RQ_REQUERIDO")]
    [InlineData("correo", "OC_SIN_RQ_CORREO_REQUERIDO")]
    [InlineData("cotizacion", "OC_COTIZACION_REQUERIDA")]
    [InlineData("valida", null)]
    public async Task P7_Handler_OcSinRq_ExigeMotivoCorreoYCotizacion(string caso, string? codigo)
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { SinRequisicionPrevia = true, MotivoSinRequisicion = "Compra DEMO P7", CotizacionExcepcionada = true });
        response.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.Include(o => o.Adjuntos).SingleAsync(o => o.Id == id);
        AgregarLineaP2(oc);
        foreach (var tipo in await db.TiposDocumentoOc.Where(t => t.Clave == "cotizacion" || t.Clave == "correo_autorizacion").ToListAsync())
            if (!(caso == "correo" && tipo.Clave == "correo_autorizacion") && !(caso == "cotizacion" && tipo.Clave == "cotizacion"))
                oc.AdjuntarDocumento(Guid.NewGuid(), tipo.Id, "DEMO-P7.pdf", "blob://DEMO/P7", "application/pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid());
        await db.SaveChangesAsync();
        // La BD exige motivo por CHECK. Simula el dato incompleto sólo en el
        // agregado rastreado que lee el handler, sin persistir un estado inválido.
        if (caso == "motivo") db.Entry(oc).Property(o => o.MotivoSinRequisicion).CurrentValue = null;
        var handler = new EnviarAAutorizacionOcHandler(db, sp.GetRequiredService<CompartidoDbContext>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IPublisher>());
        try
        {
            if (codigo is not null)
            {
                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(id), default));
                Assert.Equal(codigo, ex.Code);
                Assert.Equal(EstadoOrdenCompra.Borrador, oc.Estado);
            }
            else
            {
                await handler.Handle(new(id), default);
                Assert.Equal(EstadoOrdenCompra.EnAutorizacionJefeCompras, oc.Estado);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.OrdenesCompra.Where(o => o.Id == id).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task P7_AutorizarRq_ApartaDos_CompraOcho_NoReofrece_YRollbackEsAtomico(bool habilitado, bool fallo)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var almDb = sp.GetRequiredService<AlmacenDbContext>();
        var compartido = sp.GetRequiredService<CompartidoDbContext>();
        var articulo = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"P7A{Guid.NewGuid():N}"[..16], "DEMO P7 apartado", "PZA");
        var art = articulo.Id;
        var sucursal = SucursalIdFija;
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"P7-{Guid.NewGuid():N}"[..18], "DEMO P7", sucursal);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "P7", "DEMO P7", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "P7", "DEMO P7");
        var settings = await db.ComprasSettings.SingleOrDefaultAsync(s => s.EmpresaId == EmpresaInicialId);
        var settingsExistia = settings is not null;
        var anteriorApartar = settings?.ApartarExistenciaAlAutorizar ?? true;
        var anteriorOc = settings?.AutoGenerarOcAlAutorizar ?? false;
        if (settings is null) { settings = ComprasSettings.CrearDefault(EmpresaInicialId); db.Add(settings); }
        var rqs = Enumerable.Range(0, 2).Select(_ => NuevaRqP7(sucursal, art)).ToList();
        try
        {
            compartido.Add(articulo); await compartido.SaveChangesAsync();
            almDb.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art, 2, 100));
            await almDb.SaveChangesAsync();
            settings.EstablecerApartarExistenciaAlAutorizar(habilitado);
            settings.EstablecerAutoGenerarOcAlAutorizar(fallo);
            db.AddRange(rqs); await db.SaveChangesAsync();
            AutorizarRequisicionHandler Handler() => new(db, sp.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), new NivelP7(),
                sp.GetRequiredService<IConsultarStockPort>(), fallo ? new OcFallaP7() : sp.GetRequiredService<IGenerarSolicitudCompraPort>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<TransaccionApartadosRq>(), sp.GetRequiredService<IConversionUnidadPort>());
            if (fallo)
            {
                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Handler().Handle(new(rqs[0].Id, NivelAutorizacion.Nivel1), default));
                Assert.Equal("BIFURCACION_FALLO", ex.Code);
                Assert.Contains("Fallo DEMO P7", ex.Message);
                // Una lectura en otro scope acredita rollback persistido, sin
                // depender del agregado mutado ni del contexto que falló.
                using var lectura = _factory.Services.CreateScope();
                using var libre = lectura.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var rqDb = lectura.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var stockDb = lectura.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                var rqPersistida = await rqDb.Requisiciones.AsNoTracking().Include(r => r.Lineas).Include(r => r.Autorizaciones).SingleAsync(r => r.Id == rqs[0].Id);
                Assert.Equal(EstadoRequisicion.EnAutorizacion, rqPersistida.Estado);
                Assert.Empty(rqPersistida.Autorizaciones);
                Assert.Equal(0, rqPersistida.Lineas.Single().CantidadDeAlmacen);
                Assert.False(await stockDb.ApartadosRequisicion.AnyAsync(a => a.RequisicionId == rqs[0].Id));
                Assert.False(await rqDb.OrdenesCompra.AnyAsync(o => o.Lineas.Any(l => l.RequisicionId == rqs[0].Id)));
                Assert.Equal(2, (await stockDb.SaldosInventario.SingleAsync(s => s.UbicacionId == bin.Id)).Cantidad);
                // El mismo scope debe seguir utilizable tras deshacer la unión.
                Assert.Equal(2, (await almDb.SaldosInventario.SingleAsync(s => s.UbicacionId == bin.Id)).Cantidad);
                return;
            }
            await Handler().Handle(new(rqs[0].Id, NivelAutorizacion.Nivel1), default);
            Assert.Equal(2, rqs[0].Lineas.Single().CantidadDeAlmacen);
            Assert.Equal(8, rqs[0].Lineas.Single().CantidadDeCompra);
            using var client = await CreateSuperAdminClientAsync();
            var creadaOc = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rqs[0].Id));
            creadaOc.EnsureSuccessStatusCode();
            var ocId = (await ReadJsonAsync(creadaOc)).GetProperty("ordenCompraId").GetGuid();
            Assert.Equal(8, (await db.OrdenesCompra.AsNoTracking().Include(o => o.Lineas).SingleAsync(o => o.Id == ocId)).Lineas.Single().Cantidad);
            // El POST compromete la RQ y aumenta su versión en otro scope.
            // La instancia de preparación sigue obsoleta; reutilizarla para
            // cancelar simularía dos unidades de trabajo superpuestas.
            var comprometida = await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == rqs[0].Id);
            Assert.Equal(ocId, comprometida.ComprometidaEnOcId);
            Assert.True(comprometida.Version > rqs[0].Version);
            Assert.Null(rqs[0].ComprometidaEnOcId);
            Assert.Equal(habilitado ? 2 : 0, await almDb.ApartadosRequisicion.Where(a => a.RequisicionId == rqs[0].Id).SumAsync(a => a.Pendiente));
            await Handler().Handle(new(rqs[1].Id, NivelAutorizacion.Nivel1), default);
            Assert.Equal(habilitado ? 0 : 2, rqs[1].Lineas.Single().CantidadDeAlmacen);
            Assert.Equal(habilitado ? 10 : 8, rqs[1].Lineas.Single().CantidadDeCompra);
            // Cada comando obtiene contextos y mediator nuevos, igual que una
            // petición de producción; no se desactiva la concurrencia optimista.
            using (var cancelacion = _factory.Services.CreateScope())
            {
                var servicios = cancelacion.ServiceProvider;
                using var libre = servicios.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var compras = servicios.GetRequiredService<ComprasDbContext>();
                var cancelar = await compras.MotivosRechazo.FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.Cancelacion) != 0);
                await new CancelarRequisicionHandler(compras, servicios.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), servicios.GetRequiredService<IClock>(), servicios.GetRequiredService<TransaccionApartadosRq>())
                    .Handle(new(rqs[0].Id, cancelar.Id, "DEMO P7 cancelar"), default);
            }
            using (var cierreManual = _factory.Services.CreateScope())
            {
                var servicios = cierreManual.ServiceProvider;
                using var libre = servicios.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var compras = servicios.GetRequiredService<ComprasDbContext>();
                var cierre = await compras.MotivosRechazo.FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.CierreManual) != 0);
                await new CerrarManualRequisicionHandler(compras, servicios.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), servicios.GetRequiredService<IClock>(), servicios.GetRequiredService<TransaccionApartadosRq>())
                    .Handle(new(rqs[1].Id, cierre.Id, "DEMO P7 cerrar"), default);
            }
            using (var verificacion = _factory.Services.CreateScope())
            {
                var servicios = verificacion.ServiceProvider;
                using var libre = servicios.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var compras = servicios.GetRequiredService<ComprasDbContext>();
                var almacen = servicios.GetRequiredService<AlmacenDbContext>();
                var cancelada = await compras.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == rqs[0].Id);
                Assert.Equal(EstadoRequisicion.Cancelada, cancelada.Estado);
                Assert.True(cancelada.Version > comprometida.Version);
                Assert.Equal(EstadoRequisicion.CerradaSinSurtir, (await compras.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == rqs[1].Id)).Estado);
                Assert.Equal(0, await almacen.ApartadosRequisicion.Where(a => a.RequisicionId == rqs[0].Id || a.RequisicionId == rqs[1].Id).SumAsync(a => a.Pendiente));
                Assert.Equal(2, (await almacen.SaldosInventario.SingleAsync(s => s.UbicacionId == bin.Id)).Cantidad);
                Assert.Equal(2, (await servicios.GetRequiredService<IConsultarStockPort>().ConsultarPorSucursalAsync(sucursal, art, default)).Disponible);
            }

        }
        finally
        {
            db.ChangeTracker.Clear(); almDb.ChangeTracker.Clear();
            var ids = rqs.Select(r => r.Id).ToArray();
            await almDb.ApartadosRequisicion.Where(a => ids.Contains(a.RequisicionId)).ExecuteDeleteAsync();
            await db.OrdenesCompra.Where(o => o.Lineas.Any(l => l.RequisicionId != null && ids.Contains(l.RequisicionId.Value))).ExecuteDeleteAsync();
            await db.Requisiciones.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();
            var restaurar = await db.ComprasSettings.SingleAsync(s => s.EmpresaId == EmpresaInicialId);
            if (settingsExistia)
            { restaurar.EstablecerApartarExistenciaAlAutorizar(anteriorApartar); restaurar.EstablecerAutoGenerarOcAlAutorizar(anteriorOc); }
            else db.ComprasSettings.Remove(restaurar);
            await db.SaveChangesAsync();
            await almDb.SaldosInventario.Where(s => s.UbicacionId == bin.Id).ExecuteDeleteAsync();
            await almDb.Ubicaciones.Where(b => b.Id == bin.Id).ExecuteDeleteAsync();
            await almDb.SubAlmacenes.Where(s => s.Id == sub.Id).ExecuteDeleteAsync();
            await almDb.Almacenes.Where(a => a.Id == alm.Id).ExecuteDeleteAsync();
            await compartido.Articulos.Where(a => a.Id == art).ExecuteDeleteAsync();
        }
    }
    [Fact]
    public async Task P7_Obra_RqOcFactura_YArbolRecursivoConPagoDesdeCadaEntrada()
    {
        var client = await CreateSuperAdminClientAsync();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var art = Guid.Parse("00000007-0001-0000-0000-000000000001");
        var rq = NuevaRqP7(SucursalIdFija, art, "DEMO Obra P7");
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new(rq.Lineas.Single().Id, 0, 10)], DateTimeOffset.UtcNow);
        var almDb = sp.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"P7-{Guid.NewGuid():N}"[..18], "DEMO P7", SucursalIdFija);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "P7", "DEMO", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "P7", "DEMO");
        var tes = sp.GetRequiredService<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>();
        var cuenta = new Millet.Tesoreria.Domain.Cuentas.CuentaBancaria(EmpresaInicialId, "DEMO P7", $"{Random.Shared.NextInt64(1000000000, 9999999999)}", null, "MXN");
        var cxp = sp.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
        Guid ocId = Guid.Empty, facturaId = Guid.Empty, recepcionId = Guid.Empty, movimientoId = Guid.Empty, pagoId = Guid.Empty;
        try
        {
            db.Add(rq); await db.SaveChangesAsync();
            var creada = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rq.Id));
            creada.EnsureSuccessStatusCode();
            ocId = (await ReadJsonAsync(creada)).GetProperty("ordenCompraId").GetGuid();
            var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
            Assert.Equal("DEMO Obra P7", oc.Obra);
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
            var lineaOc = oc.Lineas.Single();
            oc.RegistrarRecepcionLinea(lineaOc.Id, 10, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            var totales = oc.CalcularTotales();
            await Millet.Api.IntegrationTests.Contabilidad.ContabTestKit.AsegurarPeriodosAbiertosAsync(client, new DateOnly(2026, 10, 9));
            var captura = await client.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
            {
                OrdenCompraId = oc.Id, ProveedorId = oc.ProveedorId, SucursalId = oc.SucursalDestinoId,
                FolioProveedor = $"DEMO-P7-{Guid.NewGuid():N}", FechaDocumento = "2026-10-09T12:00:00Z", FechaContabilizacion = "2026-10-09T12:00:00Z", FechaVencimiento = "2026-11-09",
                Moneda = "MXN", Subtotal = 1000m, Descuentos = 0m, ImpuestosTrasladados = totales.IvaTotal, Retenciones = totales.RetencionIsrTotal, Total = totales.TotalAPagar,
                Obra = "No debe sustituir la herencia",
                Lineas = new[] { new { ArticuloId = art, LineaOcId = lineaOc.Id, Descripcion = "DEMO P7", Cantidad = 10m, ClaveUnidad = "H87", PrecioUnitario = 100m, Importe = 1000m } }
            });
            captura.EnsureSuccessStatusCode();
            facturaId = (await ReadJsonAsync(captura)).GetProperty("id").GetGuid();
            var factura = await cxp.FacturasProveedor.SingleAsync(f => f.Id == facturaId);
            Assert.Equal("DEMO Obra P7", factura.Obra);
            // Documentos propios para probar los proveedores reales de lectura; no se modifica el flujo P4.
            almDb.AddRange(alm, sub, bin); await almDb.SaveChangesAsync();
            var rec = new Millet.Almacen.Domain.Movimientos.MovimientoInventario(Guid.NewGuid(), Millet.Almacen.Domain.Movimientos.TipoMovimiento.EntradaCompra, EmpresaInicialId, new(2026, 10, 9));
            recepcionId = rec.Id;
            rec.VincularRecepcionVarianteB(oc.Id, lineaOc.Id, "blob://DEMO/P7");
            rec.ConciliarConFacturaProveedor(factura.Id);
            rec.AgregarLinea(new(Guid.NewGuid(), rec.Id, 1, art, 10, "PZA", 100, ubicacionId: bin.Id, lineaOcId: lineaOc.Id));
            rec.Registrar(Millet.Almacen.Domain.Movimientos.FolioMovimiento.Construir(Millet.Almacen.Domain.Movimientos.TipoMovimiento.EntradaCompra, 2026, Random.Shared.Next(100000, 999999)), Guid.NewGuid());
            almDb.Add(rec); await almDb.SaveChangesAsync();
            var mov = Millet.Tesoreria.Domain.Movimientos.MovimientoBancario.RegistrarPagoProveedor(EmpresaInicialId, cuenta, factura.ProveedorId, factura.Total, new(2026, 10, 9), "DEMO-PAGO-P7", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
            var pago = new Millet.Tesoreria.Domain.Movimientos.AplicacionPagoProveedor(mov.Id, factura.Id, factura.ProveedorId, factura.Total, DateTimeOffset.UtcNow);
            movimientoId = mov.Id; pagoId = pago.Id;
            tes.AddRange(cuenta, mov, pago); await tes.SaveChangesAsync();
            factura.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
            factura.RegistrarPago(factura.Total, DateTimeOffset.UtcNow, "Pago DEMO P7");
            await cxp.SaveChangesAsync();
            oc.RegistrarPago(factura.Total, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            Assert.Equal(SubEstadoPago.Pagada, oc.SubEstadoPago);
            var arbol = sp.GetRequiredService<Millet.Compras.Domain.Trazabilidad.IObtenerArbolDocumentosService>();
            var desdeRq = await arbol.ObtenerAsync(Millet.Compras.Domain.Trazabilidad.TipoDocumentoTrazabilidad.Requisicion, rq.Id, default);
            var nOc = Assert.Single(desdeRq!.Descendentes);
            var nRec = Assert.Single(nOc.Descendentes, n => n.TipoDocumento == Millet.Compras.Domain.Trazabilidad.TipoDocumentoTrazabilidad.Recepcion);
            var nFact = Assert.Single(nRec.Descendentes);
            Assert.Equal(pago.Id, Assert.Single(nFact.Descendentes).Id);
            foreach (var (tipo, id) in new[] { (Millet.Compras.Domain.Trazabilidad.TipoDocumentoTrazabilidad.Recepcion, rec.Id), (Millet.Compras.Domain.Trazabilidad.TipoDocumentoTrazabilidad.FacturaProveedor, factura.Id), (Millet.Compras.Domain.Trazabilidad.TipoDocumentoTrazabilidad.PagoProveedor, pago.Id) })
                Assert.NotEmpty((await arbol.ObtenerAsync(tipo, id, default))!.Ascendentes);
        }
        finally
        {
            await tes.AplicacionesPagoProveedor.Where(p => p.Id == pagoId).ExecuteDeleteAsync();
            await tes.MovimientosBancarios.Where(m => m.Id == movimientoId).ExecuteDeleteAsync();
            await tes.CuentasBancarias.Where(c => c.Id == cuenta.Id).ExecuteDeleteAsync();
            await almDb.Movimientos.Where(m => m.Id == recepcionId).ExecuteDeleteAsync();
            await almDb.SaldosInventario.Where(s => s.UbicacionId == bin.Id).ExecuteDeleteAsync();
            await almDb.Ubicaciones.Where(b => b.Id == bin.Id).ExecuteDeleteAsync();
            await almDb.SubAlmacenes.Where(s => s.Id == sub.Id).ExecuteDeleteAsync();
            await almDb.Almacenes.Where(a => a.Id == alm.Id).ExecuteDeleteAsync();
            await cxp.FacturasProveedor.Where(f => f.Id == facturaId).ExecuteDeleteAsync();
            await db.OrdenesCompra.Where(o => o.Id == ocId).ExecuteDeleteAsync();
            await db.Requisiciones.Where(r => r.Id == rq.Id).ExecuteDeleteAsync();
        }
    }
    private static Requisicion NuevaRqP7(Guid sucursal, Guid articulo, string? obra = null)
    {
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = new Requisicion(Guid.NewGuid(), EmpresaInicialId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero}"), 2026,
            Clasificacion.MateriaPrima, sucursal, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, descripcion: "DEMO P7");
        rq.AgregarLinea(Guid.NewGuid(), articulo, 10, "PZA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
        rq.AsignarObra(obra);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        return rq;
    }
    private sealed class NivelP7 : IRequiereNivelEvaluator
    { public Task<RequiereNivel> EvaluarAsync(Requisicion r, CancellationToken ct = default) => Task.FromResult(RequiereNivel.SoloN1); }
    private sealed class OcFallaP7 : IGenerarSolicitudCompraPort
    { public Task<Guid> GenerarBorradorAsync(Guid id, IReadOnlyList<LineaSaldo> lineas, CancellationToken ct) => throw new InvalidOperationException("Fallo DEMO P7"); }
}
