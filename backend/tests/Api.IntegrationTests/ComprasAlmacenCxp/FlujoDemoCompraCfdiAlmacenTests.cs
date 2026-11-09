using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Almacen;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.ComprasAlmacenCxp;

/// <summary>
/// Prueba de integración cross-module end-to-end (P2P):
/// Orden de Compra -> Enviar OC -> Disparo de CFDI 4.0 sintético automático ->
/// Recepción en Almacén con CFDI recibido -> Captura de factura 3-Way Match en CxP sin diferencia.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class FlujoDemoCompraCfdiAlmacenTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid ProveedorId = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private const string ProveedorRfc = "SES010101AAA";

    [Fact]
    public async Task FlujoP2P_OrdenCompra_GeneraCfdiAutomatico_RecepcionAlmacen_FacturaCxPConciliada()
    {
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        using var admin = await LoginAsync(enEmpresa);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(enEmpresa, hoy);

        var articuloId = Guid.NewGuid();
        var claveArticulo = $"CP{Guid.NewGuid():N}"[..16];

        // Sembrar artículo en catálogo compartido
        using (var scope = enEmpresa.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compartidoDb = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var pza = await compartidoDb.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(
                id: articuloId,
                clave: claveArticulo,
                nombre: "Articulo P2P CFDI Demo",
                unidadMedidaDefault: "PZA",
                unidadMedidaId: pza.Id);
            compartidoDb.Add(art);
            await compartidoDb.SaveChangesAsync();
        }

        // Sembrar almacén y ubicación bin
        var (almacenId, subAlmacenId, binId) = await SembrarBinAsync(enEmpresa, articuloId);

        // 1. Setup / Seed: Crear OC autorizada con 10 unidades a $100 MXN y su OrdenCompraPdf
        var oc = CrearOcAutorizada(EmpresaBootstrapId, ProveedorId, articuloId, hoy);
        var lineaOc = oc.Lineas.Single();
        var pdf = new OrdenCompraPdf(
            id: Guid.NewGuid(),
            ordenCompraId: oc.Id,
            blobUrl: "blob://demo/oc.pdf",
            contentType: "application/pdf",
            tamañoBytes: 1024,
            generadoEn: DateTimeOffset.UtcNow,
            generadoPor: "DEMO");

        using (var scope = enEmpresa.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            comprasDb.OrdenesCompra.Add(oc);
            comprasDb.OrdenCompraPdfs.Add(pdf);
            await comprasDb.SaveChangesAsync();
        }

        Guid cfdiId = Guid.Empty;
        Guid recepcionId = Guid.Empty;
        Guid facturaId = Guid.Empty;

        try
        {
            // 2. Enviar OC al proveedor (dispara notificación y generación de CFDI sintético automático)
            var envioResp = await admin.PostAsJsonAsync($"/api/v1/compras/ordenes/{oc.Id}/envios", new
            {
                Destinatario = "proveedor-demo@example.test",
                Modo = "manual"
            });
            Assert.Equal(HttpStatusCode.OK, envioResp.StatusCode);

            // 3. Verificar que el CFDI fue ingerido automáticamente en Cuentas por Pagar
            CfdiRecibido? cfdiRecibido;
            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                cfdiRecibido = await cxpDb.CfdisRecibidos.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.EmpresaId == oc.EmpresaId && c.Folio == oc.Folio.Valor);
            }

            Assert.NotNull(cfdiRecibido);
            cfdiId = cfdiRecibido.Id;
            Assert.Equal(CanalOrigenCfdi.Mailbox, cfdiRecibido.CanalOrigen);
            Assert.Equal(EstadoCfdiRecibido.PorProcesar, cfdiRecibido.Estado);
            Assert.Equal(ProveedorRfc, cfdiRecibido.RfcEmisor.Valor);

            // Subtotal = 10 * 100 = 1000, IVA 16% = 160 => Total = 1160
            const decimal subtotalEsperado = 1000m;
            const decimal ivaEsperado = 160m;
            const decimal totalEsperado = 1160m;
            Assert.Equal(totalEsperado, cfdiRecibido.Total);
            Assert.False(string.IsNullOrWhiteSpace(cfdiRecibido.UuidCfdi.Valor));

            // Verificar también vía GET HTTP /api/v1/cuentas-por-pagar/cfdis/{id}
            var getCfdiResp = await admin.GetAsync($"/api/v1/cuentas-por-pagar/cfdis/{cfdiId}");
            Assert.Equal(HttpStatusCode.OK, getCfdiResp.StatusCode);
            var cfdiJson = await Json(getCfdiResp);
            Assert.Equal(cfdiId, cfdiJson.GetProperty("id").GetGuid());
            Assert.Equal(oc.Folio.Valor, cfdiJson.GetProperty("folio").GetString());

            // 4. Recepción en Almacén con el CFDI generado
            var recepcionResp = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", new
            {
                OrdenCompraId = oc.Id,
                FechaMovimiento = hoy,
                CfdiRecibidoId = cfdiId,
                CfdiUuidFiscal = cfdiRecibido.UuidCfdi.Valor,
                Observaciones = "DEMO recepción con CFDI generado automáticamente",
                Lineas = new[]
                {
                    new
                    {
                        ArticuloId = articuloId,
                        LineaOcId = lineaOc.Id,
                        Cantidad = 10m,
                        UbicacionId = binId
                    }
                }
            });
            Assert.Equal(HttpStatusCode.Created, recepcionResp.StatusCode);
            var recepcionJson = await Json(recepcionResp);
            recepcionId = recepcionJson.GetProperty("recepcionId").GetGuid();
            Assert.NotEqual(Guid.Empty, recepcionId);
            Assert.False(string.IsNullOrWhiteSpace(recepcionJson.GetProperty("folio").GetString()));

            // 5. Captura de Factura en CxP (3-Way Match contra OC y XML de CFDI)
            var ahora = DateTimeOffset.UtcNow;
            var capturaResp = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
            {
                OrdenCompraId = oc.Id,
                ProveedorId = ProveedorId,
                SucursalId = TestComprasFixtures.SucursalMid,
                CfdiRecibidoId = cfdiId,
                UuidCfdi = cfdiRecibido.UuidCfdi.Valor,
                FolioProveedor = oc.Folio.Valor,
                SerieProveedor = "PRUEBA",
                FechaDocumento = ahora,
                FechaContabilizacion = ahora,
                FechaVencimiento = hoy.AddDays(30),
                Moneda = "MXN",
                TipoCambio = 1m,
                Subtotal = subtotalEsperado,
                Descuentos = 0m,
                ImpuestosTrasladados = ivaEsperado,
                Retenciones = 0m,
                Total = totalEsperado,
                Lineas = new[]
                {
                    new
                    {
                        ArticuloId = (Guid?)articuloId,
                        ClaveProdServ = "01010101",
                        Descripcion = "Articulo P2P CFDI Demo",
                        Cantidad = 10m,
                        ClaveUnidad = "H87",
                        Unidad = "PZA",
                        PrecioUnitario = 100m,
                        Importe = subtotalEsperado,
                        Descuento = 0m,
                        LineaOcId = (Guid?)lineaOc.Id,
                        ConceptoContableId = (Guid?)null
                    }
                }
            });
            Assert.Equal(HttpStatusCode.Created, capturaResp.StatusCode);
            var facturaJson = await Json(capturaResp);
            facturaId = facturaJson.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, facturaId);

            // Verificar estado Capturada
            var estadoProp = facturaJson.GetProperty("estado");
            if (estadoProp.ValueKind == JsonValueKind.Number)
            {
                Assert.Equal((int)EstadoPasivo.Capturada, estadoProp.GetInt32());
            }
            else
            {
                Assert.Equal(nameof(EstadoPasivo.Capturada), estadoProp.GetString());
            }

            // Verificar que no hay diferencia contra la OC (three-way match perfecto)
            Assert.Equal(0m, facturaJson.GetProperty("diferenciaContraOc").GetDecimal());

            // Verificar persistencia en base de datos de CxP
            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var facDb = await cxpDb.FacturasProveedor.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facturaId);
                Assert.NotNull(facDb);
                Assert.Equal(EstadoPasivo.Capturada, facDb.Estado);
                Assert.Equal(0m, facDb.DiferenciaContraOc);
            }
        }
        finally
        {
            using var scope = enEmpresa.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var almacenDb = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var compartidoDb = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();

            if (facturaId != Guid.Empty)
            {
                await cxpDb.FacturasProveedor.Where(f => f.Id == facturaId).ExecuteDeleteAsync();
            }

            if (cfdiId != Guid.Empty)
            {
                await cxpDb.CfdisRecibidos.Where(c => c.Id == cfdiId).ExecuteDeleteAsync();
            }

            if (recepcionId != Guid.Empty)
            {
                await almacenDb.LineasMovimiento.Where(l => l.MovimientoId == recepcionId).ExecuteDeleteAsync();
                await almacenDb.Movimientos.Where(m => m.Id == recepcionId).ExecuteDeleteAsync();
            }

            await almacenDb.SaldosInventario.Where(s => s.SubAlmacenId == subAlmacenId || s.ArticuloId == articuloId).ExecuteDeleteAsync();
            await almacenDb.AsignacionesArticuloUbicacion.Where(a => a.ArticuloId == articuloId || a.UbicacionId == binId).ExecuteDeleteAsync();
            await almacenDb.Ubicaciones.Where(u => u.Id == binId).ExecuteDeleteAsync();
            await almacenDb.SubAlmacenes.Where(s => s.Id == subAlmacenId).ExecuteDeleteAsync();
            await almacenDb.Almacenes.Where(a => a.Id == almacenId).ExecuteDeleteAsync();

            await comprasDb.EnviosOc.Where(e => e.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.OrdenCompraPdfs.Where(p => p.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.AutorizacionesOc.Where(a => a.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.LineasOrdenCompra.Where(l => l.OrdenCompraId == oc.Id).ExecuteDeleteAsync();
            await comprasDb.OrdenesCompra.Where(o => o.Id == oc.Id).ExecuteDeleteAsync();

            await compartidoDb.Articulos.Where(a => a.Id == articuloId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task FlujoP2P_DesdeRequisicionSinRevisor_A_OrdenCompra_CfdiAutomatico_RecepcionAlmacen_FacturaCxP()
    {
        await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);
        using var admin = await LoginAsync(enEmpresa);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(enEmpresa, hoy);

        var articuloId = Guid.NewGuid();
        var claveArticulo = $"CP{Guid.NewGuid():N}"[..16];

        // Sembrar artículo en catálogo compartido
        using (var scope = enEmpresa.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compartidoDb = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var pza = await compartidoDb.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(
                id: articuloId,
                clave: claveArticulo,
                nombre: "Articulo P2P Requisicion CFDI Demo",
                unidadMedidaDefault: "PZA",
                unidadMedidaId: pza.Id);
            compartidoDb.Add(art);
            await compartidoDb.SaveChangesAsync();
        }

        // Sembrar almacén y ubicación bin
        var (almacenId, subAlmacenId, binId) = await SembrarBinAsync(enEmpresa, articuloId);

        Guid rqId = Guid.Empty;
        Guid ocId = Guid.Empty;
        Guid cfdiId = Guid.Empty;
        Guid recepcionId = Guid.Empty;
        Guid facturaId = Guid.Empty;

        try
        {
            // 1. Crear Requisición directa sin segundo revisor (menor cuantía: RequiereNivel.SoloN1)
            var rq = new Requisicion(
                id: Guid.CreateVersion7(),
                empresaId: EmpresaBootstrapId,
                folio: Millet.Compras.Domain.Folio.Parse($"MID2026-{Random.Shared.Next(100000, 999999):D6}"),
                folioAnio: 2026,
                clasificacion: Clasificacion.OrdenCompra,
                sucursalId: TestComprasFixtures.SucursalMid,
                departamentoId: TestComprasFixtures.DeptoCompras,
                almacenDestinoId: null,
                requisitanteId: Guid.NewGuid(),
                creadorId: Guid.NewGuid(),
                prioridad: Prioridad.Normal,
                fechaSolicitud: DateTimeOffset.UtcNow,
                descripcion: "DEMO Requisicion P2P sin revisor");

            var lineaRq = rq.AgregarLinea(
                lineaId: Guid.CreateVersion7(),
                articuloId: articuloId,
                cantidad: 10m,
                unidadMedida: "PZA",
                precioEstimado: Money.Mxn(100m),
                centroCostoId: Guid.Parse("0000000c-0005-0000-0000-000000000001"));

            var ahora = DateTimeOffset.UtcNow;
            rq.EnviarAAutorizacion(ahora);
            rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora, RequiereNivel.SoloN1);
            rq.RegistrarCubrimiento([new CubrimientoLinea(lineaRq.Id, 0, 10m)], ahora);

            // Configurar umbrales para que el departamento de Compras no requiera segundo revisor
            // (menor cuantía hasta $100k, requiereNivel2 solo arriba de $1M).
            var umbral = new UmbralAprobacionDepartamento(
                empresaId: EmpresaBootstrapId,
                departamentoId: TestComprasFixtures.DeptoCompras,
                vigenteDesde: hoy.AddDays(-1),
                umbralMonto: 1_000_000m,
                moneda: "MXN",
                vigenteHasta: null,
                umbralMenorCuantia: 100_000m,
                modoNivel2: ModoNivel2.SoloMonto);

            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var viejos = await comprasDb.UmbralesAprobacionDepartamento
                    .Where(u => u.EmpresaId == EmpresaBootstrapId && u.DepartamentoId == TestComprasFixtures.DeptoCompras)
                    .ToListAsync();
                comprasDb.UmbralesAprobacionDepartamento.RemoveRange(viejos);
                comprasDb.UmbralesAprobacionDepartamento.Add(umbral);
                comprasDb.Requisiciones.Add(rq);
                await comprasDb.SaveChangesAsync();
            }
            rqId = rq.Id;

            // 2. Convertir Requisición a Orden de Compra vía endpoint real
            var ocResp = await admin.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
            {
                RequisicionId = rq.Id,
                SucursalCodigo = "MID",
                FolioAnio = (short)hoy.Year,
                ProveedorId = ProveedorId,
                CondicionesPagoId = Guid.NewGuid(),
                UsoPrincipalId = Guid.NewGuid(),
                UsoCfdiClave = "G03",
                MetodoPago = "PPD",
                FormaPagoClave = "99",
                FechaDocumento = hoy,
                Moneda = "MXN"
            });
            Assert.Equal(HttpStatusCode.Created, ocResp.StatusCode);
            var ocJson = await Json(ocResp);
            ocId = ocJson.GetProperty("ordenCompraId").GetGuid();
            Assert.NotEqual(Guid.Empty, ocId);

            // 3. Adjuntar cotización requerida para transmitir y autorizar la OC
            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var ocDb = await comprasDb.OrdenesCompra.Include(o => o.Adjuntos).SingleAsync(o => o.Id == ocId);
                // Establecer comprador titular como otro usuario para evitar conflicto de segregación de funciones (OC_AUTOAUTORIZACION)
                comprasDb.Entry(ocDb).Property(o => o.CompradorTitularId).CurrentValue = Guid.NewGuid();
                var tipoDoc = await comprasDb.TiposDocumentoOc.SingleAsync(t => t.Clave == "cotizacion");
                ocDb.AdjuntarDocumento(Guid.NewGuid(), tipoDoc.Id, "cotizacion-demo.pdf", "blob://DEMO/cotizacion", "application/pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid());
                await comprasDb.SaveChangesAsync();
            }

            // Transmitir OC y autorizar con firma N1 (sin segundo revisor)
            var transmitirOc = await admin.PostAsync($"/api/v1/compras/ordenes/{ocId}/transmitir", null);
            Assert.Equal(HttpStatusCode.NoContent, transmitirOc.StatusCode);

            var autorizarOc = await admin.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/autorizaciones", new { Nivel = 1 });
            Assert.Equal(HttpStatusCode.NoContent, autorizarOc.StatusCode);

            // Asegurar OrdenCompraPdf registrado antes del envío
            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var pdfExistente = await comprasDb.OrdenCompraPdfs.FirstOrDefaultAsync(p => p.OrdenCompraId == ocId);
                if (pdfExistente is null)
                {
                    comprasDb.OrdenCompraPdfs.Add(new OrdenCompraPdf(
                        id: Guid.NewGuid(),
                        ordenCompraId: ocId,
                        blobUrl: "blob://demo/oc.pdf",
                        contentType: "application/pdf",
                        tamañoBytes: 1024,
                        generadoEn: DateTimeOffset.UtcNow,
                        generadoPor: "DEMO"));
                    await comprasDb.SaveChangesAsync();
                }
            }

            // 4. Enviar OC al proveedor: ¡Disparo del generador automático de CFDI 4.0!
            var envioResp = await admin.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/envios", new
            {
                Destinatario = "proveedor-demo@example.test",
                Modo = "manual"
            });
            Assert.Equal(HttpStatusCode.OK, envioResp.StatusCode);

            // 5. Verificar que el CFDI 4.0 llegó y se ingresó automáticamente en Cuentas por Pagar (sin frenarse)
            CfdiRecibido? cfdiRecibido;
            string folioOc;
            Guid lineaOcId;
            using (var scope = enEmpresa.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var ocDb = await comprasDb.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
                folioOc = ocDb.Folio.Valor;
                lineaOcId = ocDb.Lineas.Single().Id;

                var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                cfdiRecibido = await cxpDb.CfdisRecibidos.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.EmpresaId == ocDb.EmpresaId && c.Folio == folioOc);
            }

            Assert.NotNull(cfdiRecibido);
            cfdiId = cfdiRecibido.Id;
            Assert.Equal(CanalOrigenCfdi.Mailbox, cfdiRecibido.CanalOrigen);
            Assert.Equal(EstadoCfdiRecibido.PorProcesar, cfdiRecibido.Estado);
            Assert.Equal(ProveedorRfc, cfdiRecibido.RfcEmisor.Valor);
            Assert.Equal(1160m, cfdiRecibido.Total);

            // 6. Recepción en Almacén: Fluye sin frenarse gracias al CFDI generado automáticamente
            var recepcionResp = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", new
            {
                OrdenCompraId = ocId,
                FechaMovimiento = hoy,
                CfdiRecibidoId = cfdiId,
                CfdiUuidFiscal = cfdiRecibido.UuidCfdi.Valor,
                Observaciones = "DEMO recepción sin freno de CFDI",
                Lineas = new[]
                {
                    new
                    {
                        ArticuloId = articuloId,
                        LineaOcId = lineaOcId,
                        Cantidad = 10m,
                        UbicacionId = binId
                    }
                }
            });
            Assert.Equal(HttpStatusCode.Created, recepcionResp.StatusCode);
            var recepcionJson = await Json(recepcionResp);
            recepcionId = recepcionJson.GetProperty("recepcionId").GetGuid();
            Assert.NotEqual(Guid.Empty, recepcionId);

            // 7. Captura de Factura en CxP: Conciliación 3-Way Match sin diferencia
            var capturaResp = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
            {
                OrdenCompraId = ocId,
                ProveedorId = ProveedorId,
                SucursalId = TestComprasFixtures.SucursalMid,
                CfdiRecibidoId = cfdiId,
                UuidCfdi = cfdiRecibido.UuidCfdi.Valor,
                FolioProveedor = folioOc,
                SerieProveedor = "PRUEBA",
                FechaDocumento = ahora,
                FechaContabilizacion = ahora,
                FechaVencimiento = hoy.AddDays(30),
                Moneda = "MXN",
                TipoCambio = 1m,
                Subtotal = 1000m,
                Descuentos = 0m,
                ImpuestosTrasladados = 160m,
                Retenciones = 0m,
                Total = 1160m,
                Lineas = new[]
                {
                    new
                    {
                        ArticuloId = (Guid?)articuloId,
                        ClaveProdServ = "01010101",
                        Descripcion = "Articulo P2P Requisicion CFDI Demo",
                        Cantidad = 10m,
                        ClaveUnidad = "H87",
                        Unidad = "PZA",
                        PrecioUnitario = 100m,
                        Importe = 1000m,
                        Descuento = 0m,
                        LineaOcId = (Guid?)lineaOcId,
                        ConceptoContableId = (Guid?)null
                    }
                }
            });
            Assert.Equal(HttpStatusCode.Created, capturaResp.StatusCode);
            var facturaJson = await Json(capturaResp);
            facturaId = facturaJson.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, facturaId);

            // Validar 3-way match perfecto (SinDiferencia)
            Assert.Equal(0m, facturaJson.GetProperty("diferenciaContraOc").GetDecimal());
        }
        finally
        {
            using var scope = enEmpresa.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var cxpDb = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var almacenDb = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var compartidoDb = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();

            if (facturaId != Guid.Empty)
                await cxpDb.FacturasProveedor.Where(f => f.Id == facturaId).ExecuteDeleteAsync();

            if (cfdiId != Guid.Empty)
                await cxpDb.CfdisRecibidos.Where(c => c.Id == cfdiId).ExecuteDeleteAsync();

            if (recepcionId != Guid.Empty)
            {
                await almacenDb.LineasMovimiento.Where(l => l.MovimientoId == recepcionId).ExecuteDeleteAsync();
                await almacenDb.Movimientos.Where(m => m.Id == recepcionId).ExecuteDeleteAsync();
            }

            await almacenDb.SaldosInventario.Where(s => s.SubAlmacenId == subAlmacenId || s.ArticuloId == articuloId).ExecuteDeleteAsync();
            await almacenDb.AsignacionesArticuloUbicacion.Where(a => a.ArticuloId == articuloId || a.UbicacionId == binId).ExecuteDeleteAsync();
            await almacenDb.Ubicaciones.Where(u => u.Id == binId).ExecuteDeleteAsync();
            await almacenDb.SubAlmacenes.Where(s => s.Id == subAlmacenId).ExecuteDeleteAsync();
            await almacenDb.Almacenes.Where(a => a.Id == almacenId).ExecuteDeleteAsync();

            if (ocId != Guid.Empty)
            {
                await comprasDb.EnviosOc.Where(e => e.OrdenCompraId == ocId).ExecuteDeleteAsync();
                await comprasDb.OrdenCompraPdfs.Where(p => p.OrdenCompraId == ocId).ExecuteDeleteAsync();
                await comprasDb.AutorizacionesOc.Where(a => a.OrdenCompraId == ocId).ExecuteDeleteAsync();
                await comprasDb.LineasOrdenCompra.Where(l => l.OrdenCompraId == ocId).ExecuteDeleteAsync();
                await comprasDb.OrdenesCompra.Where(o => o.Id == ocId).ExecuteDeleteAsync();
            }

            if (rqId != Guid.Empty)
            {
                await comprasDb.Autorizaciones.Where(a => a.RequisicionId == rqId).ExecuteDeleteAsync();
                await comprasDb.LineaRequisiciones.Where(l => l.RequisicionId == rqId).ExecuteDeleteAsync();
                await comprasDb.Requisiciones.Where(r => r.Id == rqId).ExecuteDeleteAsync();
                await comprasDb.UmbralesAprobacionDepartamento
                    .Where(u => u.EmpresaId == EmpresaBootstrapId && u.DepartamentoId == TestComprasFixtures.DeptoCompras)
                    .ExecuteDeleteAsync();
            }

            await compartidoDb.Articulos.Where(a => a.Id == articuloId).ExecuteDeleteAsync();
        }
    }

    private static OrdenCompra CrearOcAutorizada(Guid empresaId, Guid proveedorId, Guid articuloId, DateOnly hoy)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: Millet.Compras.Domain.Oc.Folio.Parse($"OC-MID2026-{Random.Shared.Next(100000, 999999):D6}"),
            folioAnio: 2026,
            proveedorId: proveedorId,
            sucursalDestinoId: TestComprasFixtures.SucursalMid,
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: hoy,
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Prueba flujo demo CFDI almacén CxP");

        var lineaId = Guid.CreateVersion7();
        oc.AgregarLineaManual(
            lineaId: lineaId,
            articuloId: articuloId,
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: TestComprasFixtures.DeptoCompras);

        var ahora = DateTimeOffset.UtcNow;
        oc.ActualizarDatosFactura("G03", "PPD", "99");
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        if (oc.RequiereNivel2)
        {
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        }

        return oc;
    }

    private static async Task<(Guid AlmacenId, Guid SubAlmacenId, Guid BinId)> SembrarBinAsync(
        WebApplicationFactory<Program> app,
        Guid articuloId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(),
            $"DM{Guid.NewGuid():N}"[..16],
            "DEMO cfdi-p2p",
            TestComprasFixtures.SucursalMid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "DM", "DEMO", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "DM", "DEMO");
        almacen.AddRange(alm, sub, bin, new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, articuloId));
        await almacen.SaveChangesAsync();
        return (alm.Id, sub.Id, bin.Id);
    }

    private static async Task<IAsyncDisposable?> AbrirPeriodoSiHaceFaltaAsync(
        WebApplicationFactory<Program> app,
        DateOnly hoy)
    {
        using var scope = app.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().Database.GetConnectionString()!;
        var fx = new PeriodoContableFixture(cs, EmpresaBootstrapId, hoy.Year);
        try
        {
            await fx.SembrarAsync(hoy.Month, cerrarAnteriores: true);
            return fx;
        }
        catch
        {
            return fx;
        }
    }
}
