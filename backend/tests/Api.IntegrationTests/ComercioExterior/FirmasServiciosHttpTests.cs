using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ComercioExterior;

[Collection(ApiCompartida.Nombre)]
public sealed class FirmasServiciosHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Firmas_HTTP_exigen_facultad_CE_y_personas_distintas_y_puerto_expone_el_ciclo_vigente(bool sustituir)
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var capturista = await e.ClienteAsync(false, extra: [PermisosCanonicos.ComprasOrdenesCrear, PermisosCanonicos.ComprasOrdenesCrearSinRq]);
        Millet.Compras.Application.Oc.CrearOrdenCompraVacia.CrearOrdenCompraVaciaCommand cabecera;
        Guid articuloId, departamentoId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var original = await db.OrdenesCompra.Include(o => o.Lineas).FirstAsync(o => o.Id == DemoSesionSeedHostedService.Id("DEMO-CE-P6/ORDEN"));
            articuloId = original.Lineas.First().ArticuloId; departamentoId = original.Lineas.First().DepartamentoSolicitanteId;
            var maestros = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            var articulo = await maestros.Articulos.SingleAsync(x => x.Id == articuloId);
            // Catálogo ficticio: la naturaleza se lee del artículo; la API no acepta forzar EsServicio en una línea.
            maestros.Entry(articulo).Property(x => x.Naturaleza).CurrentValue = Millet.Catalogos.Domain.Naturaleza.Servicio;
            await maestros.SaveChangesAsync();
            var sucursal = await maestros.Sucursales.SingleAsync(x => x.Id == original.SucursalDestinoId);
            cabecera = new(original.SucursalDestinoId, sucursal.Clave, 2026, original.ProveedorId, original.CondicionesPagoId,
                original.UsoPrincipalId, new(2026, 11, 11), SinRequisicionPrevia: true, MotivoSinRequisicion: "Prueba ficticia CE",
                UsoCfdiClave: "G03", MetodoPago: "PPD", FormaPagoClave: "99", EsServicioComercioExterior: true);
        }
        using var creada = await capturista.PostAsJsonAsync("/api/v1/compras/ordenes/", cabecera);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = (await creada.Content.ReadFromJsonAsync<Millet.Compras.Application.Oc.CrearOrdenCompraVacia.CrearOrdenCompraVaciaResponse>())!.Id;
        using var lineaCreada = await capturista.PostAsJsonAsync($"/api/v1/compras/ordenes/{id}/lineas", new { articuloId, cantidad = 1, unidadMedida = "SRV", precioUnitario = 100, departamentoSolicitanteId = departamentoId, centroCostoId = Guid.Parse("0000000c-0005-0000-0000-000000000001") });
        Assert.True(lineaCreada.StatusCode == HttpStatusCode.Created, await lineaCreada.Content.ReadAsStringAsync());
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(x => x.Adjuntos).SingleAsync(x => x.Id == id);
            foreach (var tipo in await db.TiposDocumentoOc.Where(x => x.Clave == "cotizacion" || x.Clave == "correo_autorizacion").ToListAsync())
                oc.AdjuntarDocumento(Guid.NewGuid(), tipo.Id, "PRUEBA-CE.pdf", "blob://PRUEBA-CE/soporte", "application/pdf", 10, e.Ahora, oc.CompradorTitularId);
            await db.SaveChangesAsync();
        }
        using var transmitida = await capturista.PostAsync($"/api/v1/compras/ordenes/{id}/transmitir", null);
        Assert.True(transmitida.IsSuccessStatusCode, await transmitida.Content.ReadAsStringAsync());
        string[] genericos = [PermisosCanonicos.ComprasOrdenesAutorizarNivel1, PermisosCanonicos.ComprasOrdenesAutorizarNivel2];
        using var sinCe = await e.ClienteAsync(false, extra: genericos);
        using var n1 = await e.ClienteAsync(false, extra: [..genericos, PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1, PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2]);
        using var n2 = await e.ClienteAsync(false, extra: [..genericos, PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2]);
        async Task<HttpResponseMessage> Firma(HttpClient c, int nivel) => await c.PostAsJsonAsync($"/api/v1/compras/ordenes/{id}/autorizaciones", new { nivel, notas = "Prueba ficticia CE" });
        using var denegada = await Firma(sinCe, 1); Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
        using var primera = await Firma(n1, 1); Assert.True(primera.IsSuccessStatusCode, await primera.Content.ReadAsStringAsync());
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var lectura = await scope.ServiceProvider.GetRequiredService<IComprasOcReadPort>().ObtenerAsync(id, default);
            Assert.True(lectura!.EsServicioComercioExterior); Assert.True(lectura.ServicioCeNivel1Autorizado);
            Assert.False(lectura.ServicioCeNivel2Autorizado); Assert.Equal("EnAutorizacionDireccion", lectura.Estado);
        }
        using var sinFactura = await Firma(n2, 2); Assert.Equal(HttpStatusCode.UnprocessableEntity, sinFactura.StatusCode);
        Assert.Contains("CE_SERVICIO_SIN_PASIVO", await sinFactura.Content.ReadAsStringAsync());
        using var cxpClient = await e.ClienteAsync(false, extra: [PermisosCanonicos.CuentasPorPagarFacturasCapturar, PermisosCanonicos.CuentasPorPagarFacturasAutorizar, PermisosCanonicos.CuentasPorPagarFacturasCancelar]);
        // FACTURA_AUTOAUTORIZACION: quien captura la factura no la autoriza.
        using var cxpAutoriza = await e.ClienteAsync(false, extra: [PermisosCanonicos.CuentasPorPagarFacturasAutorizar]);
        Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc.CapturarFacturaConOcCommand captura;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var oc = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == id);
            var linea = oc.Lineas.Single();
            captura = new(id, oc.ProveedorId, oc.SucursalDestinoId, null, Guid.NewGuid().ToString(), "PRUEBA-FIRMAS", null,
                new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero), e.Ahora, new(2026, 12, 1), "MXN", null, 100, 0, 16, 0, 116,
                [new(linea.ArticuloId, null, "Servicio ficticio CE", 1, "SRV", null, 100, 100, null, linea.Id, null)]);
        }
        using var capturada = await cxpClient.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas/", captura);
        Assert.Equal(HttpStatusCode.Created, capturada.StatusCode);
        var factura = await capturada.Content.ReadFromJsonAsync<Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc.CapturarFacturaConOcResponse>();
        await ComunicarAComprasAsync(e, factura!.Id, "cuentas_por_pagar.factura.registrada.v1");
        async Task<HttpResponseMessage> Liberar()
        {
            using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var f = await scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>().FacturasProveedor.SingleAsync(f => f.Id == factura!.Id);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cuentas-por-pagar/facturas/{f.Id}/autorizar");
            request.Headers.Add("X-Expected-Version", f.Version.ToString());
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            return await cxpAutoriza.SendAsync(request);
        }
        using var sinSegunda = await Liberar();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sinSegunda.StatusCode);
        using var mismo = await Firma(n1, 2); Assert.Equal(HttpStatusCode.UnprocessableEntity, mismo.StatusCode);
        Assert.Contains("OC_FIRMA_MISMA_PERSONA", await mismo.Content.ReadAsStringAsync());
        using var segunda = await Firma(n2, 2); Assert.True(segunda.IsSuccessStatusCode, await segunda.Content.ReadAsStringAsync());
        if (sustituir)
        {
            using (var scope = e.Scope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var f = await scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>().FacturasProveedor.SingleAsync(x => x.Id == factura!.Id);
                using var cancelar = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cuentas-por-pagar/facturas/{f.Id}/cancelar")
                { Content = JsonContent.Create(new { motivo = Millet.CuentasPorPagar.Domain.FacturaProveedor.MotivoCancelacion.ErrorCaptura, texto = "Sustitución ficticia después de N2" }) };
                cancelar.Headers.Add("X-Expected-Version", f.Version.ToString()); cancelar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                using var cancelada = await cxpClient.SendAsync(cancelar);
                Assert.True(cancelada.IsSuccessStatusCode, await cancelada.Content.ReadAsStringAsync());
            }
            using var recaptura = await cxpClient.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas/", captura);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, recaptura.StatusCode);
            Assert.Contains("CE_SERVICIO_REAUTORIZACION_REQUERIDA", await recaptura.Content.ReadAsStringAsync());
            using var scopeEvento = e.Scope(); using var bypassEvento = scopeEvento.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxpEvento = scopeEvento.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var evento = (await cxpEvento.OutboxEntries.Where(x => x.EventType == "cuentas_por_pagar.factura.cancelada.v1").ToListAsync()).Single(x => x.Payload.Contains(factura!.Id.ToString()));
            using var worker = new Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker(null!, scopeEvento.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker>.Instance);
            var consumidor = (Millet.SharedKernel.Application.Integration.ILocalIntegrationEventConsumer)worker;
            await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
            await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
            var oc = await scopeEvento.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.Include(x => x.Autorizaciones).SingleAsync(x => x.Id == id);
            Assert.Equal(EstadoOrdenCompra.Rechazada, oc.Estado); Assert.Equal(2, oc.Autorizaciones.Count);
            using var liberacionCancelada = await Liberar();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, liberacionCancelada.StatusCode);
            using var retransmitida = await capturista.PostAsync($"/api/v1/compras/ordenes/{id}/transmitir", null);
            Assert.True(retransmitida.IsSuccessStatusCode, await retransmitida.Content.ReadAsStringAsync());
            using var nuevaPrimera = await Firma(n1, 1);
            Assert.True(nuevaPrimera.IsSuccessStatusCode, await nuevaPrimera.Content.ReadAsStringAsync());
            using var sustituta = await cxpClient.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas/", captura with { UuidCfdi = Guid.NewGuid().ToString(), FolioProveedor = "PRUEBA-SUSTITUTA" });
            Assert.Equal(HttpStatusCode.Created, sustituta.StatusCode);
            var anteriorId = factura!.Id;
            factura = await sustituta.Content.ReadFromJsonAsync<Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc.CapturarFacturaConOcResponse>();
            Assert.NotEqual(anteriorId, factura!.Id);
            await ComunicarAComprasAsync(e, factura.Id, "cuentas_por_pagar.factura.registrada.v1");
            using var sinNuevaSegunda = await Liberar();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinNuevaSegunda.StatusCode);
            using var nuevaSegunda = await Firma(n2, 2);
            Assert.True(nuevaSegunda.IsSuccessStatusCode, await nuevaSegunda.Content.ReadAsStringAsync());
            using var nuevaLiberacion = await Liberar();
            Assert.True(nuevaLiberacion.IsSuccessStatusCode, await nuevaLiberacion.Content.ReadAsStringAsync());
            // Un nuevo aviso de la cancelación antigua no invalida la factura sustituta vigente.
            await consumidor.ConsumirLocalAsync(evento.EventType, Guid.NewGuid(), evento.Payload, default);
            using var lecturaFinal = e.Scope(); using var empresaFinal = lecturaFinal.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ocFinal = await lecturaFinal.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.Include(x => x.Autorizaciones).SingleAsync(x => x.Id == id);
            Assert.Equal(EstadoOrdenCompra.Autorizada, ocFinal.Estado); Assert.Equal(2, ocFinal.CicloAutorizacion); Assert.Equal(4, ocFinal.Autorizaciones.Count);
            Assert.Equal(1, await lecturaFinal.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>().FacturasProveedor.CountAsync(x => x.OrdenCompraId == id && x.Estado != Millet.CuentasPorPagar.Domain.FacturaProveedor.EstadoPasivo.Cancelada));
            return;
        }
        using var liberada = await Liberar();
        Assert.True(liberada.IsSuccessStatusCode, await liberada.Content.ReadAsStringAsync());
        using var final = e.Scope(); using var ignorar = final.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var resultado = await final.ServiceProvider.GetRequiredService<IComprasOcReadPort>().ObtenerAsync(id, default);
        Assert.True(resultado!.ServicioCeNivel2Autorizado); Assert.Equal("Autorizada", resultado.Estado);
        var cxp = final.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
        var pasivo = await cxp.FacturasProveedor.SingleAsync(f => f.OrdenCompraId == id);
        Assert.Equal(factura!.Id, pasivo.Id);
        Assert.Equal(Millet.CuentasPorPagar.Domain.FacturaProveedor.EstadoPasivo.Autorizada, pasivo.Estado);
        await VerificarPagosAsync(e, factura.Id);
    }
    private static async Task ComunicarAComprasAsync(ExpedientesHttpTests.Escenario e, Guid facturaId, string tipo, Guid? aplicacionId = null)
    {
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
        var evento = (await cxp.OutboxEntries.Where(x => x.EventType == tipo).ToListAsync())
            .Single(x => x.Payload.Contains(facturaId.ToString()) && (aplicacionId is null || x.Payload.Contains(aplicacionId.Value.ToString())));
        if (tipo == Millet.Contabilidad.Application.Motor.NormalizadorEventos.FacturaProveedorRegistrada)
            Assert.True(Millet.Contabilidad.Application.Motor.NormalizadorEventos.Normalizar(tipo, evento.Id, evento.Payload).EsServicioComercioExterior);
        using var worker = new Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker(null!, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker>.Instance);
        var consumidor = (Millet.SharedKernel.Application.Integration.ILocalIntegrationEventConsumer)worker;
        await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
        await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
    }

    private static async Task VerificarPagosAsync(ExpedientesHttpTests.Escenario e, Guid facturaId)
    {
        const string pagos = "/api/v1/tesoreria/pagos";
        using var tesClient = await e.ClienteAsync(false, extra: [PermisosCanonicos.TesoreriaPagosAplicar, PermisosCanonicos.TesoreriaPagosRevertir]);
        using var ceClient = await e.ClienteAsync(true);
        const string expedientes = "/api/v1/comercio-exterior/expedientes";
        using var creado = await ceClient.PostAsJsonAsync(expedientes, e.Importacion());
        Assert.True(creado.IsSuccessStatusCode, await creado.Content.ReadAsStringAsync());
        var expedienteId = (await creado.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();
        Guid cuentaId;
        Millet.ComercioExterior.Domain.DatosGasto gasto;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var factura = await cxp.FacturasProveedor.SingleAsync(f => f.Id == facturaId);
            var ce = scope.ServiceProvider.GetRequiredService<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>();
            var expediente = await ce.Expedientes.SingleAsync(x => x.Id == expedienteId);
            var concepto = new Millet.ComercioExterior.Domain.ConceptoGasto(expediente.EmpresaId, "BANCO-PRUEBA", "Servicio ficticio pagado por Tesorería");
            ce.ConceptosGasto.Add(concepto);
            gasto = new(concepto.Id, factura.ProveedorId, factura.UuidCfdi!, "MXN", 100, new(2026, 11, 11), FacturaProveedorId: facturaId, OrdenServicioId: factura.OrdenCompraId);
            await ce.SaveChangesAsync();
            var compartido = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            compartido.CuentasBancariasProveedor.Add(Millet.DatosMaestros.Domain.CuentaBancariaProveedor.Heredada(factura.ProveedorId, "Banco ficticio", "012180000000001234", "Proveedor de prueba", e.Ahora));
            await Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente.AgregarOpinionCumplimientoAsync(compartido, factura.ProveedorId);
            await compartido.SaveChangesAsync();
            var tes = scope.ServiceProvider.GetRequiredService<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>();
            cuentaId = (await tes.CuentasBancarias.FirstAsync(c => c.EmpresaId == factura.EmpresaId && c.Activa && c.Moneda == "MXN")).Id;
            var evento = (await cxp.OutboxEntries.Where(x => x.EventType == "cuentas_por_pagar.pasivo.autorizado-para-pago.v1").ToListAsync())
                .Last(x => x.Payload.Contains(facturaId.ToString()));
            using var worker = new Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker(null!, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker>.Instance);
            await ((Millet.SharedKernel.Application.Integration.ILocalIntegrationEventConsumer)worker).ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
        }
        using (var actual = await ceClient.GetAsync($"{expedientes}/{expedienteId}/finanzas"))
        {
            actual.EnsureSuccessStatusCode();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{expedientes}/{expedienteId}/gastos") { Content = JsonContent.Create(gasto) };
            request.Headers.Add("If-Match", actual.Headers.ETag!.Tag); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var registrado = await ceClient.SendAsync(request);
            Assert.True(registrado.IsSuccessStatusCode, await registrado.Content.ReadAsStringAsync());
        }
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = scope.ServiceProvider.GetRequiredService<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>();
            var expediente = await ce.Expedientes.Include(x => x.Gastos).SingleAsync(x => x.Id == expedienteId);
            Assert.Single(expediente.Gastos);
            var concepto = await ce.ConceptosGasto.SingleAsync(x => x.Id == gasto.ConceptoId);
            // La base física se prepara aquí; firmas, pasivo, gasto, banco y cierre se ejecutan por HTTP.
            expediente.ConfirmarDistribucion(expediente.PrepararDistribucion([concepto],
                [new Millet.ComercioExterior.Domain.BasePartidaCosto(Guid.NewGuid(), null, "Base ficticia del ensayo financiero", 100, 1, 1, OcId: expediente.OcId)],
                e.Responsable, e.Ahora, incrementablesAlInventario: false));
            await ce.SaveChangesAsync();
        }
        async Task Cierre(bool pagado)
        {
            foreach (var accion in new[] { "cerrar-financiero", "cerrar" })
            {
                using var actual = await ceClient.GetAsync($"{expedientes}/{expedienteId}"); actual.EnsureSuccessStatusCode();
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{expedientes}/{expedienteId}/{accion}") { Content = JsonContent.Create(new { }) };
                request.Headers.Add("If-Match", actual.Headers.ETag!.Tag); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                using var cierre = await ceClient.SendAsync(request);
                var contenido = await cierre.Content.ReadAsStringAsync();
                if (!pagado)
                {
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
                    Assert.Contains("CE_SERVICIOS_PENDIENTES", contenido);
                }
                else if (accion == "cerrar-financiero") Assert.True(cierre.IsSuccessStatusCode, contenido);
                else
                {
                    // El ensayo financiero no suplanta documentos ni recepción física del expediente.
                    Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
                    Assert.Contains("CE_TRES_CIERRES_PENDIENTES", contenido);
                }
            }
        }
        await Cierre(false);
        async Task<decimal> Saldo()
        {
            using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            return (await scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Application.PublicPorts.IFacturaGastoReadPort>()
                .ObtenerSaldosAsync(DemoSesionSeedHostedService.EmpresaId, [facturaId], default)).Single().SaldoPendiente;
        }
        async Task Proyectar(string tipo, Guid movimiento)
        {
            using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var tes = scope.ServiceProvider.GetRequiredService<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>();
            var evento = (await tes.OutboxEntries.Where(x => x.EventType == tipo).ToListAsync())
                .Single(x => x.Payload.Contains(movimiento.ToString()) && x.Payload.Contains(facturaId.ToString()));
            using var worker = new Millet.CuentasPorPagar.Infrastructure.Workers.TesoreriaEventListenerWorker(null!, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.CuentasPorPagar.Infrastructure.Workers.TesoreriaEventListenerWorker>.Instance);
            var consumidor = (Millet.SharedKernel.Application.Integration.ILocalIntegrationEventConsumer)worker;
            await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
            await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
            await ComunicarAComprasAsync(e, facturaId, "cuentas_por_pagar.factura.pago-aplicado.v1",
                tipo == "tesoreria.pago-factura-proveedor.aplicado.v1" ? movimiento : evento.Id);
        }
        var aplicaciones = new List<Guid>();
        foreach (var importe in new[] { 40m, 76m })
        {
            using var pago = await tesClient.PostAsJsonAsync(pagos + "/", new { cuentaBancariaId = cuentaId, fechaValor = new DateOnly(2026, 11, 11), referenciaBancaria = "CE-PRUEBA-" + Guid.NewGuid().ToString("N"), aplicaciones = new[] { new { facturaProveedorId = facturaId, importe } } });
            Assert.True(pago.IsSuccessStatusCode, await pago.Content.ReadAsStringAsync());
            var pagado = await pago.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.NotEqual(Guid.Empty, pagado.GetProperty("movimientoId").GetGuid());
            var aplicacion = pagado.GetProperty("aplicaciones")[0].GetProperty("pagoId").GetGuid();
            aplicaciones.Add(aplicacion);
            await Cierre(false);
            await Proyectar("tesoreria.pago-factura-proveedor.aplicado.v1", aplicacion);
            Assert.Equal(importe == 40 ? 76 : 0, await Saldo());
            await Cierre(importe == 76);
            using var scopeOc = e.Scope(); using var alcanceOc = scopeOc.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxpOc = scopeOc.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var ocId = (await cxpOc.FacturasProveedor.SingleAsync(x => x.Id == facturaId)).OrdenCompraId;
            Assert.Equal(importe == 76 ? EstadoOrdenCompra.Cerrada : EstadoOrdenCompra.Autorizada,
                (await scopeOc.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.SingleAsync(x => x.Id == ocId)).Estado);
        }
        using var reversa = await tesClient.PostAsJsonAsync($"{pagos}/{aplicaciones[0]}/revertir", new { motivo = "Reversa ficticia para comprobar cierre CE" });
        Assert.True(reversa.IsSuccessStatusCode, await reversa.Content.ReadAsStringAsync());
        Assert.Equal(40, await Saldo());
        await Cierre(false);
        await Proyectar("tesoreria.pago-factura-proveedor.revertido.v1", aplicaciones[0]);
        Assert.Equal(40, await Saldo());
        await Cierre(false);
        using var final = e.Scope(); using var alcance = final.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var facturaFinal = await final.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>().FacturasProveedor.SingleAsync(x => x.Id == facturaId);
        Assert.Equal(EstadoOrdenCompra.Autorizada, (await final.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.SingleAsync(x => x.Id == facturaFinal.OrdenCompraId)).Estado);
        Assert.False(await final.ServiceProvider.GetRequiredService<Millet.Tesoreria.Infrastructure.Persistence.TesoreriaDbContext>()
            .AutorizacionesPagoComercioExterior.AnyAsync(x => x.FacturaProveedorId == facturaId));
    }

}
