using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Cuentas;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

// Solo PostgreSQL desechable vía tools/validate-integration-isolated.sh.
// Cuentas y documentos ficticios por prueba; conceptos tomados del seed compartido.
public sealed class P5TesoreriaCobranzaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly List<Guid> _cuentas = [];
    private readonly List<Guid> _propuestas = [];
    private readonly List<Guid> _facturas = [];
    private readonly List<Guid> _pasivos = [];
    private readonly List<Guid> _conceptos = [];
    private readonly List<Guid> _depositos = [];
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DateOnly Fecha = new(2026, 10, 9);
    private const string Tes = "/api/v1/tesoreria";
    private const string Cxc = "/api/v1/cuentas-por-cobrar/propuestas-aplicacion";

    [Fact]
    public async Task Superadmin_que_propone_recibe_422_sin_aplicar_ni_publicar_confirmacion()
    {
        var (client, user) = await Login(); var (deposito, movimiento) = await Deposito(user);
        var response = await Post(client, $"{Tes}/depositos/{deposito.Id}/confirmar", new { movimientoBancarioId = movimiento.Id }, deposito.Version);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("DEP_MISMO_USUARIO");
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await db.DepositosConfirmacion.SingleAsync(d => d.Id == deposito.Id)).Estado.Should().Be(EstadoDepositoConfirmacion.Pendiente);
        (await db.MovimientosBancarios.SingleAsync(m => m.Id == movimiento.Id)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        var payloads = await db.OutboxEntries.Where(e => e.EventType == "tesoreria.pago-cliente.confirmado.v1").Select(e => e.Payload).ToListAsync();
        payloads.Should().NotContain(p => p.Contains(movimiento.Id.ToString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Resolucion_publica_outbox_y_consumidor_actualiza_Cxc(bool confirmar)
    {
        var (client, _) = await Login(); var proponente = Guid.NewGuid();
        var (deposito, movimiento) = await Deposito(proponente);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
            var propuesta = PropuestaAplicacionPago.Crear(Empresa, deposito.ClienteId!.Value, "P5", 1100, "MXN", "REM-P5",
                [(Guid.NewGuid().ToString(), Guid.NewGuid(), 1000, null)], 0, proponente);
            // La proyección de Tesorería usa el mismo id de propuesta que el evento real.
            var tesDb = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var anterior = await tesDb.DepositosConfirmacion.SingleAsync(d => d.Id == deposito.Id);
            tesDb.DepositosConfirmacion.Remove(anterior);
            deposito = DepositoConfirmacion.CrearDesdePropuesta(Empresa, propuesta.Id, propuesta.ClienteId, "P5", 1100, "MXN", "[]", proponente, 100);
            tesDb.DepositosConfirmacion.Add(deposito); db.PropuestasAplicacionPago.Add(propuesta);
            _depositos.Add(deposito.Id);
            _propuestas.Add(propuesta.Id);
            await db.SaveChangesAsync(); await tesDb.SaveChangesAsync();
        }
        var response = await Post(client, $"{Tes}/depositos/{deposito.Id}/{(confirmar ? "confirmar" : "rechazar")}",
            confirmar ? (object)new { movimientoBancarioId = movimiento.Id } : new { motivo = "Depósito no identificado" }, deposito.Version);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var tipo = confirmar ? ResolverPropuestaTesoreriaHandler.ConfirmadaEventType : ResolverPropuestaTesoreriaHandler.RechazadaEventType;
        var eventos = await tes.OutboxEntries.Where(e => e.EventType == tipo).ToListAsync();
        var evento = eventos.Single(e => e.Payload.Contains(deposito.PropuestaCxcId!.Value.ToString()));
        var json = JsonSerializerOptions.Web;
        ResolverPropuestaTesoreriaCommand command;
        if (confirmar)
        {
            var payload = JsonSerializer.Deserialize<PagoClienteConfirmadoIntegrationEvent>(evento.Payload, json)!;
            payload.SaldoAFavorPorIdentificar.Should().Be(100);
            command = new(evento.Id, payload.EmpresaId, payload.PropuestaId!.Value, payload.ConfirmadaPor!.Value, payload.OcurridoEn, payload.MovimientoBancarioId, null);
        }
        else
        {
            var payload = JsonSerializer.Deserialize<PropuestaRechazadaPayload>(evento.Payload, json)!;
            command = new(evento.Id, payload.EmpresaId, payload.PropuestaId, payload.RechazadaPor, payload.OcurridoEn, null, payload.Motivo);
        }
        var sender = verificar.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(command); await sender.Send(command);
        var cxc = verificar.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var final = await cxc.PropuestasAplicacionPago.SingleAsync(p => p.Id == deposito.PropuestaCxcId);
        final.Estado.Should().Be(confirmar ? EstadoPropuestaAplicacion.Confirmada : EstadoPropuestaAplicacion.Rechazada);
    }

    [Theory]
    [InlineData("confirmar")]
    [InlineData("rechazar")]
    public async Task Cxc_ya_no_expone_resolucion_directa(string accion)
    {
        var (client, _) = await Login();
        var response = await Post(client, $"{Cxc}/{Guid.NewGuid()}/{accion}", new { motivo = "P5" }, 1);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Propuestas_concurrentes_reservan_saldo_y_excedente_se_conserva()
    {
        var (client, _) = await Login(); var cliente = Guid.NewGuid(); var uuid = Guid.NewGuid().ToString();
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
            var factura = FacturaCartera.Crear(Empresa, Guid.NewGuid(), cliente, "AAA010101AAA", "Cliente ficticio P5", uuid, "P5", 1000, "MXN", "PPD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30));
            db.FacturasCartera.Add(factura); _facturas.Add(factura.Id);
            await db.SaveChangesAsync();
        }
        var body = new CrearPropuestaAplicacionCommand(cliente, "DEP-P5", 800, "MXN", "REM-P5", [new(uuid, 700, null)]);
        var respuestas = await Task.WhenAll(Post(client, Cxc, body), Post(client, Cxc, body));
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respuestas.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).Should().Be(1);
        var creada = await respuestas.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<PropuestaAplicacionResponse>();
        creada!.SaldoAFavorPorIdentificar.Should().Be(100);
        _propuestas.Add(creada.Id);
    }

    [Fact]
    public async Task Pago_a_cuenta_sin_proveedor_400_y_con_parcial_abierto_422()
    {
        var (client, user) = await Login(); var (deposito, ingreso) = await Deposito(Guid.NewGuid()); var proveedor = Guid.NewGuid();
        var sin = await Post(client, $"{Tes}/pagos-cuenta", new { cuentaBancariaId = ingreso.CuentaBancariaId, monto = 100, fechaValor = Fecha, motivo = "Prueba P5" });
        sin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var cuenta = await db.CuentasBancarias.SingleAsync(c => c.Id == ingreso.CuentaBancariaId);
            var pago = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, proveedor, 500, Fecha, null, null, "Prueba P5", user, DateTimeOffset.UtcNow);
            pago.ActualizarEstadoAplicacion(100); db.MovimientosBancarios.Add(pago); await db.SaveChangesAsync();
        }
        var segundo = await Post(client, $"{Tes}/pagos-cuenta", new { cuentaBancariaId = ingreso.CuentaBancariaId, proveedorId = proveedor, monto = 100, fechaValor = Fecha, motivo = "Prueba P5" });
        segundo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Saldo_inicial_y_reclasificacion_persisten_con_bitacora()
    {
        var (client, _) = await Login();
        var crear = await Post(client, $"{Tes}/cuentas", new { banco = "Banco ficticio P5", numeroCuenta = DateTime.UtcNow.Ticks.ToString(), moneda = "MXN", sucursal = "Ejemplo P5", titular = "Millet ficticio", finalidad = "Pruebas", firmantes = "Firmante ficticio" });
        crear.EnsureSuccessStatusCode(); var cuenta = (await crear.Content.ReadFromJsonAsync<CuentaSaldoResponse>())!;
        _cuentas.Add(cuenta.Id);
        (await Post(client, $"{Tes}/cuentas/{cuenta.Id}/saldo-inicial", new { saldo = 1000, fechaCorte = Fecha.AddDays(-1), motivo = "Carga ficticia P5" }, cuenta.Version)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ingreso = await Post(client, $"{Tes}/movimientos", new { cuentaBancariaId = cuenta.Id, monto = 100, fechaValor = Fecha });
        ingreso.EnsureSuccessStatusCode(); var mov = await ingreso.Content.ReadFromJsonAsync<JsonElement>();
        var id = mov.GetProperty("id").GetGuid();
        var concepto = Guid.Parse("0000000b-1001-0000-0000-000000000008");
        (await Post(client, $"{Tes}/movimientos/{id}/reclasificar", new { conceptoId = concepto, motivo = "Corrección ficticia P5" }, mov.GetProperty("version").GetInt32())).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await db.MovimientosBancarios.SingleAsync(m => m.Id == id)).ConceptoId.Should().Be(concepto);
        var logs = await db.Set<Millet.SharedKernel.Domain.Audit.AuditLogEntry>().Where(a => a.EntidadId == id || a.EntidadId == cuenta.Id).ToListAsync();
        logs.Should().Contain(a => a.Cambios.Contains("Corrección ficticia P5"));
        logs.Should().Contain(a => a.Cambios.Contains("Carga ficticia P5"));
        var reporte = await client.GetAsync($"{Tes}/reportes/flujo-efectivo?desde={Fecha:yyyy-MM-dd}&hasta={Fecha:yyyy-MM-dd}&cuentaBancariaId={cuenta.Id}");
        reporte.EnsureSuccessStatusCode();
        var filas = (await reporte.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("filas").EnumerateArray().ToList();
        filas.Single(f => f.GetProperty("tipo").GetString() == "saldoInicial").GetProperty("neto").GetDecimal().Should().Be(1000);
        filas.Single(f => f.GetProperty("tipo").GetString() == "saldoFinal").GetProperty("neto").GetDecimal().Should().Be(1100);
        filas.Single(f => f.GetProperty("tipo").GetString() == "movimiento").GetProperty("clasificacion").GetString().Should().Be("Inversión");
    }

    [Fact]
    public async Task Desligar_por_endpoint_conserva_un_solo_egreso_y_guarda_motivo()
    {
        var (client, user) = await Login(); var (_, ingreso) = await Deposito(Guid.NewGuid());
        Guid movimientoId; Guid facturaId;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var cuenta = await db.CuentasBancarias.SingleAsync(c => c.Id == ingreso.CuentaBancariaId);
            facturaId = Guid.NewGuid(); var proveedor = Guid.NewGuid();
            var pasivo = new PasivoPendientePago(Empresa, facturaId, proveedor, null, 500, 500, "MXN", null, Fecha, null, "P5", DateTimeOffset.UtcNow, "PUE");
            var pago = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, proveedor, 500, Fecha, null, null, "Prueba ficticia P5", user, DateTimeOffset.UtcNow);
            movimientoId = pago.Id; _pasivos.Add(facturaId);
            db.PasivosPendientesPago.Add(pasivo); db.MovimientosBancarios.Add(pago); await db.SaveChangesAsync();
        }
        var ligada = await Post(client, $"{Tes}/pagos-cuenta/{movimientoId}/ligar", new { facturaProveedorId = facturaId, importe = 200 });
        ligada.EnsureSuccessStatusCode();
        var pagoId = (await ligada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("aplicaciones")[0].GetProperty("pagoId").GetGuid();
        (await Post(client, $"{Tes}/pagos-cuenta/{movimientoId}/aplicaciones/{pagoId}/desligar", new { motivo = "Liga equivocada P5" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await tes.MovimientosBancarios.SingleAsync(m => m.Id == movimientoId)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);
        (await tes.AplicacionesPagoProveedor.SingleAsync(a => a.Id == pagoId)).MotivoReversa.Should().Be("Liga equivocada P5");
        (await tes.MovimientosBancarios.AnyAsync(m => m.ContramovimientoDe == movimientoId)).Should().BeFalse();
    }

    [Fact]
    public async Task Catalogo_permite_alta_edicion_y_baja_logica_sin_alterar_seed()
    {
        var (client, _) = await Login();
        var nombre = $"Subconcepto ficticio P5 {Guid.NewGuid():N}";
        var alta = await Post(client, $"{Tes}/conceptos", new { nombre, clasificacionFlujo = 2 });
        alta.EnsureSuccessStatusCode(); var concepto = (await alta.Content.ReadFromJsonAsync<ConceptoResponse>())!;
        _conceptos.Add(concepto.Id);
        using var editar = new HttpRequestMessage(HttpMethod.Put, $"{Tes}/conceptos/{concepto.Id}")
        { Content = JsonContent.Create(new { nombre, clasificacionFlujo = 3, activo = false }) };
        editar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        editar.Headers.Add("X-Expected-Version", concepto.Version.ToString());
        var baja = await client.SendAsync(editar); baja.EnsureSuccessStatusCode();
        var final = (await baja.Content.ReadFromJsonAsync<ConceptoResponse>())!;
        final.Activo.Should().BeFalse(); final.ClasificacionFlujo.Should().Be(ClasificacionFlujo.Financiamiento);
        var activos = (await client.GetFromJsonAsync<List<ConceptoResponse>>($"{Tes}/conceptos"))!;
        activos.Should().NotContain(c => c.Id == concepto.Id);
        var todos = (await client.GetFromJsonAsync<List<ConceptoResponse>>($"{Tes}/conceptos?incluirInactivos=true"))!;
        todos.Should().Contain(c => c.Id == concepto.Id && !c.Activo);
    }

    private async Task<(DepositoConfirmacion, MovimientoBancario)> Deposito(Guid proponente)
    {
        using var scope = factory.Services.CreateScope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var cuenta = new CuentaBancaria(Empresa, "Banco ficticio P5", DateTime.UtcNow.Ticks.ToString(), null, "MXN");
        var movimiento = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1100, Fecha, null, null, null, null, proponente, DateTimeOffset.UtcNow);
        var deposito = DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), Guid.NewGuid(), "P5", 1100, "MXN", "[]", proponente, 100);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(movimiento); db.DepositosConfirmacion.Add(deposito); await db.SaveChangesAsync();
        _cuentas.Add(cuenta.Id);
        _depositos.Add(deposito.Id);
        return (deposito, movimiento);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var movimientos = tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).Select(m => m.Id);
        await tes.DepositosConfirmacion.Where(d => _depositos.Contains(d.Id)).ExecuteDeleteAsync();
        await tes.AplicacionesPagoProveedor.Where(a => movimientos.Contains(a.MovimientoId)).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).ExecuteDeleteAsync();
        await tes.CuentasBancarias.Where(c => _cuentas.Contains(c.Id)).ExecuteDeleteAsync();
        await tes.PasivosPendientesPago.Where(p => _pasivos.Contains(p.FacturaProveedorId)).ExecuteDeleteAsync();
        await tes.ConceptosMovimiento.Where(c => _conceptos.Contains(c.Id)).ExecuteDeleteAsync();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        await cxc.PropuestasAplicacionPago.Where(p => _propuestas.Contains(p.Id)).ExecuteDeleteAsync();
        await cxc.FacturasCartera.Where(f => _facturas.Contains(f.Id)).ExecuteDeleteAsync();
    }
    private async Task<(HttpClient, Guid)> Login()
    {
        var client = factory.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "p5@dev.local", nombre = "Pruebas P5", empresaId = (Guid?)null }); r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, body.GetProperty("usuario").GetProperty("id").GetGuid());
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, int? version = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (version is not null) request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }
}
