using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Tesoreria.Application.EventListeners;
using MediatR;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

// Solo PostgreSQL desechable vía tools/validate-integration-isolated.sh.
public sealed class DevolucionesClienteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private readonly List<Guid> _cuentas = [];
    private readonly List<Guid> _clientes = [];
    private readonly List<Guid> _notasCredito = [];
    private readonly List<Guid> _eventos = [];
    private readonly List<Guid> _depositos = [];
    private static readonly JsonSerializerOptions Insensible = new() { PropertyNameCaseInsensitive = true };
    private const string Clabe = "032180000118359719";
    private const string Tes = "/api/v1/tesoreria";

    [Fact]
    public async Task CA9_11_deposito_erroneo_se_solicita_autoriza_y_paga_con_contramovimiento_ligado()
    {
        // El calendario contable del entorno de prueba puede tener el mes cerrado; aquí se aísla.
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<IPeriodoContablePort, PeriodoAbierto>();
        }));
        var (client, _) = await Login(app);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 1000m, motivo = "Depósito erróneo P-15", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        solicitud.StatusCode.Should().Be(HttpStatusCode.Created);
        var devolucionId = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // El superadmin no puede autorizar su propia solicitud: se cambia el solicitante en BD.
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var otro = Guid.NewGuid();
            await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
                .Where(d => d.Id == devolucionId).ExecuteUpdateAsync(s => s.SetProperty(d => d.SolicitadaPor, otro));
        }
        var autorizada = await Post(client, $"{Tes}/devoluciones/{devolucionId}/autorizar", new { }, await Version(client, devolucionId));
        autorizada.StatusCode.Should().Be(HttpStatusCode.OK);

        var sinFondos = await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar",
            new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), fondosVerificados = false }, await Version(client, devolucionId));
        sinFondos.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var llave = Guid.NewGuid().ToString();
        var cuerpo = new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), fondosVerificados = true };
        var version = await Version(client, devolucionId);
        var pagada = await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar", cuerpo, version, llave);
        pagada.StatusCode.Should().Be(HttpStatusCode.OK, await pagada.Content.ReadAsStringAsync());
        var cuerpoPagada = await pagada.Content.ReadFromJsonAsync<JsonElement>();
        cuerpoPagada.GetProperty("estado").GetInt32().Should().Be((int)EstadoDevolucionCliente.Pagada);
        var movimientoPagoId = cuerpoPagada.GetProperty("movimientoPagoId").GetGuid();
        // Mismo Idempotency-Key: misma respuesta, sin segundo egreso.
        var repetida = await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar", cuerpo, version, llave);
        repetida.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repetida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("movimientoPagoId").GetGuid()
            .Should().Be(movimientoPagoId);
        // Otro Idempotency-Key sobre la devolución ya pagada: no se paga dos veces.
        await Esperar(await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar", cuerpo, await Version(client, devolucionId)),
            HttpStatusCode.UnprocessableEntity, "DEV_NO_AUTORIZADA");

        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var egresos = await db.MovimientosBancarios.Where(m => m.ContramovimientoDe == deposito.Id).ToListAsync();
        var egreso = egresos.Should().ContainSingle().Subject;
        egreso.Monto.Should().Be(1000m);
        egreso.Sentido.Should().Be(SentidoMovimiento.Egreso);
        egreso.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Cliente);
        egreso.MotivoReversa.Should().Be("Depósito erróneo P-15");
        egreso.FondosVerificados.Should().BeTrue();
        var original = await db.MovimientosBancarios.SingleAsync(m => m.Id == deposito.Id);
        original.Monto.Should().Be(1000m);
        original.EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
    }

    [Fact]
    public async Task T9_6a_devolucion_mayor_al_deposito_se_rechaza_al_solicitar_y_al_editar()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        string Ruta() => $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion";
        object Cuerpo(decimal importe) => new { clienteId, importe, motivo = "Exceso T9.6-a", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" };

        var excede = await Post(client, Ruta(), Cuerpo(1000.01m));
        await Esperar(excede, HttpStatusCode.UnprocessableEntity, "DEV_IMPORTE_EXCEDE_DEPOSITO");

        var primera = await Post(client, Ruta(), Cuerpo(600m));
        primera.StatusCode.Should().Be(HttpStatusCode.Created);
        var primeraId = (await primera.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Lo ya comprometido cuenta contra el tope.
        await Esperar(await Post(client, Ruta(), Cuerpo(500m)), HttpStatusCode.UnprocessableEntity, "DEV_IMPORTE_EXCEDE_DEPOSITO");

        var editar = await Put(client, $"{Tes}/devoluciones/{primeraId}",
            new { clienteId, importe = 1100m, motivo = "Exceso T9.6-a", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" },
            await Version(client, primeraId));
        await Esperar(editar, HttpStatusCode.UnprocessableEntity, "DEV_IMPORTE_EXCEDE_DEPOSITO");
        // Editar dentro del tope (la propia solicitud no se cuenta dos veces) sí procede.
        var valido = await Put(client, $"{Tes}/devoluciones/{primeraId}",
            new { clienteId, importe = 1000m, motivo = "Exceso T9.6-a", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" },
            await Version(client, primeraId));
        valido.StatusCode.Should().Be(HttpStatusCode.OK, await valido.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Quien_solicita_no_autoriza()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 300m, motivo = "Segregación", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        solicitud.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var autorizar = await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id));
        await Esperar(autorizar, HttpStatusCode.UnprocessableEntity, "DEV_MISMO_USUARIO");
        (await client.GetFromJsonAsync<JsonElement>($"{Tes}/devoluciones/{id}")).GetProperty("estado").GetInt32()
            .Should().Be((int)EstadoDevolucionCliente.Solicitada);
    }

    [Fact]
    public async Task CA6_17_reembolso_de_nc_llega_a_tesoreria_una_sola_vez_y_se_paga_sin_contramovimiento()
    {
        await using var app = AppConPeriodoAbierto();
        var (client, _) = await Login(app);
        var ncId = Guid.NewGuid(); _notasCredito.Add(ncId);
        var clienteId = Guid.NewGuid();
        // Se serializa exactamente como lo hace el Outbox de Facturación (opciones por defecto).
        var evento = new NotaCreditoReembolsoSolicitadoIntegrationEvent(Empresa, DateTimeOffset.UtcNow, ncId, "NC-CA617", Guid.NewGuid().ToString(),
            clienteId, "AAA010101AAA", 400m, 500m, "MXN", TestComprasFixtures.SucursalMid, "Bonificación a reembolsar", Guid.NewGuid());
        var payload = JsonSerializer.Deserialize<NotaCreditoReembolsoSolicitadoPayload>(
            JsonSerializer.Serialize(evento, evento.GetType()), Insensible)!;
        NotaCreditoReembolsoSolicitadoPayload.EventType.Should().Be(evento.EventType);
        var (e1, e2) = (Guid.NewGuid(), Guid.NewGuid()); _eventos.AddRange([e1, e2]);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ProyectarReembolsoNotaCreditoCommand(e1, payload));
            await mediator.Send(new ProyectarReembolsoNotaCreditoCommand(e2, payload));
        }
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var d = await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
                .Where(x => x.NotaCreditoId == ncId).ToListAsync();
            var una = d.Should().ContainSingle().Subject;
            una.Origen.Should().Be(OrigenDevolucionCliente.NotaCredito);
            una.Estado.Should().Be(EstadoDevolucionCliente.Solicitada);
            una.Importe.Should().Be(400m);
            id = una.Id;
        }

        var destino = await Put(client, $"{Tes}/devoluciones/{id}",
            new { clienteId, importe = 400m, motivo = "Bonificación a reembolsar", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" },
            await Version(client, id));
        destino.StatusCode.Should().Be(HttpStatusCode.OK, await destino.Content.ReadAsStringAsync());
        // Quien editó la solicitud tampoco la autoriza: otro editor en BD para poder autorizar con el mismo login.
        await Esperar(await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id)),
            HttpStatusCode.UnprocessableEntity, "DEV_MISMO_USUARIO");
        await OtroEditor(id);
        (await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id))).StatusCode.Should().Be(HttpStatusCode.OK);

        var cuenta = await Deposito();
        var cuerpo = new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), cuentaBancariaId = cuenta.CuentaBancariaId, fondosVerificados = true };
        var pagada = await Post(client, $"{Tes}/devoluciones/{id}/pagar", cuerpo, await Version(client, id));
        pagada.StatusCode.Should().Be(HttpStatusCode.OK, await pagada.Content.ReadAsStringAsync());

        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var final = await db.DevolucionesCliente.SingleAsync(x => x.Id == id);
        final.Estado.Should().Be(EstadoDevolucionCliente.Pagada);
        var egreso = await db.MovimientosBancarios.SingleAsync(m => m.Id == final.MovimientoPagoId);
        egreso.Sentido.Should().Be(SentidoMovimiento.Egreso);
        egreso.Monto.Should().Be(400m);
        egreso.BeneficiarioTipo.Should().Be(BeneficiarioTipo.Cliente);
        egreso.ContramovimientoDe.Should().BeNull();
    }

    [Fact]
    public async Task Alcance_por_sucursal_operativo_ve_solo_las_suyas()
    {
        var propia = TestComprasFixtures.SucursalMid; var ajena = TestComprasFixtures.SucursalMty;
        var oid = $"test-dev-sucursal-{Guid.NewGuid():N}"[..40];
        await CrearOperativo(oid, propia, PermisosCanonicos.TesoreriaDevolucionesVer);
        var devPropia = await SembrarDevolucionNc(propia); var devAjena = await SembrarDevolucionNc(ajena);
        var (client, _) = await Login(factory, oid);

        (await client.GetAsync($"{Tes}/devoluciones/{devPropia}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"{Tes}/devoluciones/{devAjena}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var lista = await client.GetFromJsonAsync<JsonElement>($"{Tes}/devoluciones?limit=200");
        var ids = lista.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(devPropia).And.NotContain(devAjena);

        // El corporativo (superadmin: bypass) ve ambas.
        var (admin, _) = await Login(factory);
        (await admin.GetAsync($"{Tes}/devoluciones/{devAjena}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sin_permiso_cada_endpoint_responde_403()
    {
        var (client, _) = await Login(factory, "test-no-perms");
        var id = Guid.NewGuid();
        var respuestas = new[]
        {
            await client.GetAsync($"{Tes}/devoluciones"),
            await client.GetAsync($"{Tes}/devoluciones/{id}"),
            await Post(client, $"{Tes}/movimientos/{id}/solicitar-devolucion", new { clienteId = id, importe = 1m, motivo = "x" }),
            await Put(client, $"{Tes}/devoluciones/{id}", new { clienteId = id, importe = 1m, motivo = "x" }, 1),
            await Post(client, $"{Tes}/devoluciones/{id}/cancelar", new { motivo = "x" }, 1),
            await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, 1),
            await Post(client, $"{Tes}/devoluciones/{id}/rechazar", new { motivo = "x" }, 1),
            await Post(client, $"{Tes}/devoluciones/{id}/pagar", new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), fondosVerificados = true }, 1),
            await Post(client, $"/api/v1/facturacion/notas-credito/{id}/solicitar-reembolso", new { importe = 1m, motivo = "x" }),
        };
        respuestas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Solicitar_reembolso_de_nc_escribe_el_evento_en_el_outbox_de_facturacion()
    {
        var (client, _) = await Login(factory);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio TES-02", "601", "97000", "G03", "MEX", false);
        var emisor = new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio TES-02", "601", "97000");
        var clienteId = Guid.NewGuid();
        var pedido = PedidoFacturable.CrearManual(Empresa, $"FIX-TES02-{Guid.NewGuid():N}", TestComprasFixtures.SucursalMid, clienteId,
            "Cliente ficticio TES-02", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null);
        var factura = FacturaVenta.CrearBorrador(Empresa, $"FIX-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid, null, null, receptor, emisor,
            "PUE", "01", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        factura.AgregarLinea(null, "01010101", "FIX prueba ficticia TES-02", "H87", 1, 100, 0, "02", 0.16m, null, null);
        factura.RecalcularTotales(); factura.MarcarTimbradoEnProceso();
        factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        var nc = NotaCredito.CrearBonificacion(Empresa, $"FIX-NC-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid, null, null, receptor, emisor,
            "01", "MXN", null, 2026, 10, 1, factura.Id, 116m, null, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        try
        {
            fac.AddRange(pedido, factura); await fac.SaveChangesAsync();
            fac.Add(nc); await fac.SaveChangesAsync();

            var ok = await Post(client, $"/api/v1/facturacion/notas-credito/{nc.Id}/solicitar-reembolso", new { importe = nc.Total, motivo = "Reembolso TES-02" });
            ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
            var repetido = await Post(client, $"/api/v1/facturacion/notas-credito/{nc.Id}/solicitar-reembolso", new { importe = nc.Total, motivo = "Reembolso TES-02" });
            await Esperar(repetido, HttpStatusCode.UnprocessableEntity, "NC_REEMBOLSO_YA_SOLICITADO");

            // Payload es jsonb: se compara en memoria.
            var filas = (await fac.OutboxEntries.Where(e => e.EventType == "facturacion.nota-credito.reembolso-solicitado.v1")
                .Select(e => new { e.Id, e.Payload }).ToListAsync()).Where(e => e.Payload.Contains(nc.Id.ToString())).ToList();
            filas.Should().ContainSingle();
            filas[0].Payload.Should().Contain(clienteId.ToString());
        }
        finally
        {
            var filas = (await fac.OutboxEntries.Where(e => e.EventType == "facturacion.nota-credito.reembolso-solicitado.v1")
                .Select(e => new { e.Id, e.Payload }).ToListAsync()).Where(e => e.Payload.Contains(nc.Id.ToString())).Select(e => e.Id).ToList();
            await fac.OutboxEntries.Where(e => filas.Contains(e.Id)).ExecuteDeleteAsync();
            // NotaCredito/FacturaVenta son TPT: ExecuteDelete no aplica, se borra por el rastreador.
            // El request actualizó la NC: se recarga en un contexto nuevo para no chocar con la versión.
            using var limpieza = factory.Services.CreateScope();
            using var bypassLimpieza = limpieza.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db2 = limpieza.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            db2.Remove(await db2.NotasCredito.SingleAsync(x => x.Id == nc.Id)); await db2.SaveChangesAsync();
            db2.Remove(await db2.FacturasVenta.SingleAsync(x => x.Id == factura.Id)); await db2.SaveChangesAsync();
            db2.Remove(await db2.PedidosFacturables.SingleAsync(x => x.Id == pedido.Id)); await db2.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Carrera_deposito_cxc_confirmar_con_devolucion_en_curso_se_rechaza()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 500m, motivo = "Carrera CxC", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        solicitud.StatusCode.Should().Be(HttpStatusCode.Created);
        var devolucionId = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var confirmacionId = await SembrarPropuesta(clienteId);
        string Ruta() => $"{Tes}/depositos/{confirmacionId}/confirmar";

        // Solicitada y luego Autorizada: ambas bloquean la confirmación contra CxC.
        await Esperar(await Post(client, Ruta(), new { movimientoBancarioId = deposito.Id }, await VersionDeposito(confirmacionId)),
            HttpStatusCode.UnprocessableEntity, "DEP_DEVOLUCION_EN_CURSO");
        await OtroSolicitante(devolucionId);
        (await Post(client, $"{Tes}/devoluciones/{devolucionId}/autorizar", new { }, await Version(client, devolucionId))).StatusCode.Should().Be(HttpStatusCode.OK);
        await Esperar(await Post(client, Ruta(), new { movimientoBancarioId = deposito.Id }, await VersionDeposito(confirmacionId)),
            HttpStatusCode.UnprocessableEntity, "DEP_DEVOLUCION_EN_CURSO");

        // Cancelada ya no bloquea: se puede confirmar (la otra puerta queda cerrada por el candado de depósito confirmado).
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
                .Where(d => d.Id == devolucionId).ExecuteUpdateAsync(s => s.SetProperty(d => d.Estado, EstadoDevolucionCliente.Cancelada));
        }
        var ok = await Post(client, Ruta(), new { movimientoBancarioId = deposito.Id }, await VersionDeposito(confirmacionId));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Carrera_deposito_cxc_pagar_devolucion_de_deposito_ya_confirmado_se_rechaza()
    {
        await using var app = AppConPeriodoAbierto();
        var (client, _) = await Login(app);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 500m, motivo = "Carrera CxC", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        var devolucionId = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await OtroSolicitante(devolucionId);
        (await Post(client, $"{Tes}/devoluciones/{devolucionId}/autorizar", new { }, await Version(client, devolucionId))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Se simula la confirmación que, antes del candado, podía colarse entre autorizar y pagar.
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var mov = await db.MovimientosBancarios.SingleAsync(m => m.Id == deposito.Id);
            var dep = DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), clienteId, "REF CARRERA", mov.Monto, "MXN", "[]", Guid.NewGuid());
            dep.Confirmar(mov, Guid.NewGuid(), DateTimeOffset.UtcNow);
            db.DepositosConfirmacion.Add(dep); await db.SaveChangesAsync();
            _depositos.Add(dep.Id);
        }
        var pagar = await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar",
            new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), fondosVerificados = true }, await Version(client, devolucionId));
        await Esperar(pagar, HttpStatusCode.UnprocessableEntity, "DEV_DEPOSITO_CONFIRMADO");
        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        (await verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>().MovimientosBancarios
            .AnyAsync(m => m.ContramovimientoDe == deposito.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Tope_del_deposito_cuenta_todo_egreso_ligado_no_solo_devoluciones_pagadas()
    {
        await using var app = AppConPeriodoAbierto();
        var (client, _) = await Login(app);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 600m, motivo = "Tope egresos", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await OtroSolicitante(id);
        (await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Una reversa manual (no es DevolucionCliente) ya consumió 500 del depósito.
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var original = await db.MovimientosBancarios.SingleAsync(m => m.Id == deposito.Id);
            db.MovimientosBancarios.Add(original.CrearContramovimiento(500m, DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), DateTimeOffset.UtcNow, "Reversa previa"));
            await db.SaveChangesAsync();
        }

        var pagar = await Post(client, $"{Tes}/devoluciones/{id}/pagar",
            new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), fondosVerificados = true }, await Version(client, id));
        await Esperar(pagar, HttpStatusCode.UnprocessableEntity, "DEV_IMPORTE_EXCEDE_DEPOSITO");
    }

    [Fact]
    public async Task Quien_edita_la_solicitud_no_la_autoriza()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 300m, motivo = "Editor", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await OtroSolicitante(id);
        var editar = await Put(client, $"{Tes}/devoluciones/{id}",
            new { clienteId, importe = 250m, motivo = "Editor", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" }, await Version(client, id));
        editar.StatusCode.Should().Be(HttpStatusCode.OK, await editar.Content.ReadAsStringAsync());
        await Esperar(await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id)),
            HttpStatusCode.UnprocessableEntity, "DEV_MISMO_USUARIO");
    }

    [Fact]
    public async Task Autorizador_sin_ver_cuenta_completa_recibe_la_clabe_enmascarada()
    {
        var oid = $"test-dev-aut-{Guid.NewGuid():N}"[..40];
        await CrearOperativo(oid, TestComprasFixtures.SucursalMid, PermisosCanonicos.TesoreriaDevolucionesVer, PermisosCanonicos.TesoreriaDevolucionesAutorizar);
        var id = await SembrarDevolucionNc(TestComprasFixtures.SucursalMid);
        var (admin, _) = await Login(factory);
        var editar = await Put(admin, $"{Tes}/devoluciones/{id}",
            new { clienteId = await ClienteDe(id), importe = 100m, motivo = "PII", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" }, await Version(admin, id));
        editar.StatusCode.Should().Be(HttpStatusCode.OK, await editar.Content.ReadAsStringAsync());
        (await editar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("destinoClabe").GetString().Should().Be(Clabe);

        var (autorizador, _) = await Login(factory, oid);
        var autorizada = await Post(autorizador, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(autorizador, id));
        autorizada.StatusCode.Should().Be(HttpStatusCode.OK, await autorizada.Content.ReadAsStringAsync());
        var clabe = (await autorizada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("destinoClabe").GetString();
        clabe.Should().NotBe(Clabe).And.EndWith(Clabe[^4..]).And.StartWith("**");
    }

    [Fact]
    public async Task Solicitar_sin_autorizar_recibe_403_en_autorizar_y_rechazar()
    {
        var oid = $"test-dev-sol-{Guid.NewGuid():N}"[..40];
        await CrearOperativo(oid, TestComprasFixtures.SucursalMid, PermisosCanonicos.TesoreriaDevolucionesVer, PermisosCanonicos.TesoreriaDevolucionesSolicitar);
        var id = await SembrarDevolucionNc(TestComprasFixtures.SucursalMid);
        var (client, _) = await Login(factory, oid);
        (await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Post(client, $"{Tes}/devoluciones/{id}/rechazar", new { motivo = "x" }, 1)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cancelar_y_rechazar_liberan_el_tope_del_deposito()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        async Task<Guid> Solicitar()
        {
            var r = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
                new { clienteId, importe = 1000m, motivo = "Libera tope", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
            r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var primera = await Solicitar();
        (await Post(client, $"{Tes}/devoluciones/{primera}/cancelar", new { motivo = "Ya no aplica" }, await Version(client, primera)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var segunda = await Solicitar();
        (await Post(client, $"{Tes}/devoluciones/{segunda}/rechazar", new { motivo = "No procede" }, await Version(client, segunda)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await Solicitar();
    }

    [Fact]
    public async Task Pago_de_nc_con_cuenta_en_otra_moneda_se_rechaza()
    {
        await using var app = AppConPeriodoAbierto();
        var (client, _) = await Login(app);
        var id = await NcAutorizada(client);
        var usd = await NuevaCuenta("USD");
        var pagar = await Post(client, $"{Tes}/devoluciones/{id}/pagar",
            new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), cuentaBancariaId = usd, fondosVerificados = true }, await Version(client, id));
        await Esperar(pagar, HttpStatusCode.UnprocessableEntity, "DEV_CUENTA_OTRA_MONEDA");
    }

    [Fact]
    public async Task Pago_de_nc_con_periodo_cerrado_se_rechaza()
    {
        await using var abierto = AppConPeriodoAbierto();
        var (admin, _) = await Login(abierto);
        var id = await NcAutorizada(admin);
        var cuenta = await NuevaCuenta("MXN");
        await using var cerrado = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IPeriodoContablePort>();
            services.AddSingleton<IPeriodoContablePort, PeriodoCerrado>();
        }));
        var (client, _) = await Login(cerrado);
        var pagar = await Post(client, $"{Tes}/devoluciones/{id}/pagar",
            new { fechaValor = DateOnly.FromDateTime(DateTime.UtcNow), cuentaBancariaId = cuenta, fondosVerificados = true }, await Version(client, id));
        await Esperar(pagar, HttpStatusCode.UnprocessableEntity, "MOV_PERIODO_CERRADO");
    }

    [Fact]
    public async Task La_bitacora_no_contiene_la_clabe_completa()
    {
        var (client, _) = await Login(factory);
        var clienteId = await NuevoCliente();
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 100m, motivo = "Bitácora", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" });
        var id = (await solicitud.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Put(client, $"{Tes}/devoluciones/{id}",
            new { clienteId, importe = 90m, motivo = "Bitácora", banco = "BBVA", clabe = "012180015678912342", titular = "Cliente SA" }, await Version(client, id)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var entradas = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().AuditLog.AsNoTracking()
            .Where(a => a.EntidadId == id).ToListAsync();
        entradas.Should().NotBeEmpty();
        entradas.Should().OnlyContain(a => !a.Cambios.Contains(Clabe) && !a.Cambios.Contains("012180015678912342"));
    }

    private async Task<Guid> NcAutorizada(HttpClient client)
    {
        var id = await SembrarDevolucionNc(TestComprasFixtures.SucursalMid);
        var edit = await Put(client, $"{Tes}/devoluciones/{id}",
            new { clienteId = await ClienteDe(id), importe = 100m, motivo = "NC pago", banco = "BBVA", clabe = Clabe, titular = "Cliente SA" }, await Version(client, id));
        edit.StatusCode.Should().Be(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        await OtroEditor(id);
        (await Post(client, $"{Tes}/devoluciones/{id}/autorizar", new { }, await Version(client, id))).StatusCode.Should().Be(HttpStatusCode.OK);
        return id;
    }

    private async Task<int> VersionDeposito(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DepositosConfirmacion
            .Where(d => d.Id == id).Select(d => d.Version).SingleAsync();
    }

    private async Task<Guid> ClienteDe(Guid devolucionId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
            .Where(d => d.Id == devolucionId).Select(d => d.ClienteId).SingleAsync();
    }

    private async Task OtroSolicitante(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var otro = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
            .Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.SolicitadaPor, otro));
    }

    private async Task OtroEditor(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var otro = Guid.NewGuid();
        await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DevolucionesCliente
            .Where(d => d.Id == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.EditadaPor, otro));
    }

    private async Task<Guid> NuevaCuenta(string moneda)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var cuenta = new CuentaBancaria(Empresa, "Banco ficticio DEV", Guid.NewGuid().ToString("N")[..16], null, moneda);
        db.CuentasBancarias.Add(cuenta); await db.SaveChangesAsync();
        _cuentas.Add(cuenta.Id);
        return cuenta.Id;
    }

    private async Task<Guid> SembrarPropuesta(Guid clienteId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var dep = DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), clienteId, "REF CARRERA", 1000m, "MXN", "[]", Guid.NewGuid());
        db.DepositosConfirmacion.Add(dep); await db.SaveChangesAsync();
        _depositos.Add(dep.Id);
        return dep.Id;
    }

    private WebApplicationFactory<Program> AppConPeriodoAbierto() => factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
    {
        services.RemoveAll<IPeriodoContablePort>();
        services.AddSingleton<IPeriodoContablePort, PeriodoAbierto>();
    }));

    private async Task<Guid> NuevoCliente()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var cliente = new Cliente(Guid.CreateVersion7(), $"DV{Guid.NewGuid():N}"[..14], "CLIENTE DEVOLUCION PRUEBA");
        compartido.Clientes.Add(cliente); await compartido.SaveChangesAsync();
        _clientes.Add(cliente.Id);
        return cliente.Id;
    }

    private async Task<Guid> SembrarDevolucionNc(Guid sucursalId)
    {
        var ncId = Guid.NewGuid(); _notasCredito.Add(ncId);
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var d = DevolucionCliente.SolicitarPorNotaCredito(Empresa, ncId, "NC-SUC", null, Guid.NewGuid(), 100m, 100m, "MXN",
            sucursalId, "Alcance por sucursal", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.DevolucionesCliente.Add(d); await db.SaveChangesAsync();
        return d.Id;
    }

    // Usuario operativo: rol con solo los permisos indicados + asociación a una sucursal, sin permiso de bypass.
    private async Task CrearOperativo(string oid, Guid sucursalId, params string[] permisos)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var rolId = Guid.CreateVersion7(); var usuarioId = Guid.CreateVersion7();
        identidad.Roles.Add(new Rol(rolId, $"rol-{oid}"[..40], "Rol de prueba de devoluciones"));
        foreach (var codigo in permisos)
        {
            var permisoId = await identidad.Permisos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
            identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
        }
        identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@dev.local", "Operativo devoluciones"));
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, asignadoPorUsuarioId: null));
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, sucursalId, Empresa));
        await identidad.SaveChangesAsync();
    }

    private static async Task Esperar(HttpResponseMessage r, HttpStatusCode estado, string codigo)
    {
        var cuerpo = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(estado, cuerpo);
        JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be(codigo);
    }

    private static async Task<HttpResponseMessage> Put(HttpClient client, string path, object body, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }

    private async Task<MovimientoBancario> Deposito()
    {
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var cuenta = new CuentaBancaria(Empresa, "Banco ficticio DEV", DateTime.UtcNow.Ticks.ToString(), null, "MXN");
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1000m, DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(m); await db.SaveChangesAsync();
        _cuentas.Add(cuenta.Id);
        return m;
    }

    private static async Task<int> Version(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Tes}/devoluciones/{id}")).GetProperty("version").GetInt32();

    private sealed class PeriodoAbierto : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class PeriodoCerrado : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(false);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        await tes.DepositosConfirmacion.Where(d => _depositos.Contains(d.Id)).ExecuteDeleteAsync();
        var movimientos = tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).Select(m => m.Id);
        await tes.DevolucionesCliente.Where(d => movimientos.Contains(d.MovimientoOrigenId!.Value)
            || (d.NotaCreditoId != null && _notasCredito.Contains(d.NotaCreditoId.Value))).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId) && m.ContramovimientoDe != null).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).ExecuteDeleteAsync();
        await tes.CuentasBancarias.Where(c => _cuentas.Contains(c.Id)).ExecuteDeleteAsync();
        await tes.EventosProcesados.Where(e => _eventos.Contains(e.Id)).ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Clientes.Where(c => _clientes.Contains(c.Id)).ExecuteDeleteAsync();
    }

    private static async Task<(HttpClient, Guid)> Login(WebApplicationFactory<Program> app, string oid = "dev-superadmin")
    {
        var client = app.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = oid, email = $"{oid}@dev.local", nombre = "Pruebas DEV", empresaId = (Guid?)null }); r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, body.GetProperty("usuario").GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, int? version = null, string? llave = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", llave ?? Guid.NewGuid().ToString());
        if (version is not null) request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }
}
