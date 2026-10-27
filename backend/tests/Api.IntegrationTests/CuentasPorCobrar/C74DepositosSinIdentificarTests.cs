using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// F1-CXC-03 · C7.4 de punta a punta (PostgreSQL desechable vía
/// tools/validate-integration-isolated.sh): depósito sin identificar (CA7.9),
/// propuesta contra 3 facturas con confirmación por otro usuario y REP que las
/// salda (CA7.8, R12), y el sobrante que vuelve a la bandeja de CxC.
/// Datos ficticios por prueba; los eventos se entregan con ISender (sin Service Bus).
/// </summary>
public sealed class C74DepositosSinIdentificarTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DateOnly Fecha = new(2026, 10, 26);
    private const string Tes = "/api/v1/tesoreria";
    private const string Cxc = "/api/v1/cuentas-por-cobrar/propuestas-aplicacion";
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    private static readonly JsonSerializerOptions JsonListener = new() { PropertyNameCaseInsensitive = true };
    private static readonly decimal[] Totales = [20_000m, 20_000m, 10_000m];

    private readonly List<Guid> _cuentas = [];
    private readonly List<Guid> _facturas = [];
    private readonly List<Guid> _clientes = [];

    [Fact]
    public async Task CA79_deposito_sin_detalle_queda_sin_identificar_y_al_identificarlo_sale_de_la_bandeja()
    {
        await using var app = ConPeriodoAbierto();
        var (client, _) = await Login(app);
        var cliente = NuevoCliente();
        var movimientoId = await RegistrarCobroDeCliente(client, 50_000m);

        // CA7.9: sin detalle queda «sin identificar» y no se aplica.
        var bandeja = await PorIdentificar(client);
        bandeja.Should().ContainSingle(d => d.MovimientoBancarioId == movimientoId)
            .Which.Disponible.Should().Be(50_000m);
        (await Movimiento(movimientoId)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.NoAplicado);

        // R13: sin remittance no se propone.
        var facturas = await TresFacturasPpd(cliente);
        var sinRemittance = await Post(client, Cxc, Comando(cliente, movimientoId, facturas, remittance: ""));
        sinRemittance.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Identificar = proponer con el remittance; el depósito sale de la bandeja.
        var creada = await Post(client, Cxc, Comando(cliente, movimientoId, facturas));
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync());
        var propuesta = (await creada.Content.ReadFromJsonAsync<PropuestaAplicacionResponse>(Json))!;
        propuesta.MovimientoBancarioId.Should().Be(movimientoId);
        propuesta.Facturas.Should().OnlyContain(f => f.NumParcialidad == 1 && f.SaldoInsoluto == 0m);
        (await PorIdentificar(client)).Should().NotContain(d => d.MovimientoBancarioId == movimientoId);

        // R12: quien propuso no confirma (la proyección de Tesorería preselecciona el movimiento).
        await ProyectarEnTesoreria(propuesta.Id);
        var deposito = await DepositoDe(propuesta.Id);
        deposito.MovimientoId.Should().Be(movimientoId); // sugerencia mientras está Pendiente
        var mismo = await Post(client, $"{Tes}/depositos/{deposito.Id}/confirmar", new { movimientoBancarioId = movimientoId }, deposito.Version);
        mismo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await mismo.Content.ReadAsStringAsync()).Should().Contain("DEP_MISMO_USUARIO");
    }

    [Fact]
    public async Task CA78_otro_usuario_confirma_y_las_tres_facturas_se_saldan_al_timbrar_el_REP()
    {
        await using var app = ConPeriodoAbierto();
        var (client, superadmin) = await Login(app);
        var cliente = NuevoCliente();
        var movimientoId = await RegistrarCobroDeCliente(client, 50_000m);
        var facturas = await TresFacturasPpd(cliente);
        // La propone otra persona de CxC (proponente ≠ quien confirma).
        var propuestaId = await ProponerComoOtroUsuario(cliente, movimientoId, 50_000m,
            facturas.Select(f => (f.Uuid, f.Id, f.Total)).ToList());
        var deposito = await DepositoDe(propuestaId);

        var confirmada = await Post(client, $"{Tes}/depositos/{deposito.Id}/confirmar", new { movimientoBancarioId = movimientoId }, deposito.Version);
        confirmada.StatusCode.Should().Be(HttpStatusCode.OK, await confirmada.Content.ReadAsStringAsync());
        (await Movimiento(movimientoId)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);

        // CxC consume la confirmación y, al timbrarse el REP, las facturas se saldan.
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var evento = (await tes.OutboxEntries.Where(e => e.EventType == ResolverPropuestaTesoreriaHandler.ConfirmadaEventType).ToListAsync())
            .Single(e => e.Payload.Contains(propuestaId.ToString()));
        var pago = JsonSerializer.Deserialize<PagoClienteConfirmadoIntegrationEvent>(evento.Payload, Json)!;
        pago.Monto.Should().Be(50_000m);
        pago.ConfirmadaPor.Should().Be(superadmin);
        await sender.Send(new ResolverPropuestaTesoreriaCommand(evento.Id, Empresa, propuestaId, superadmin, pago.OcurridoEn, movimientoId, null));
        await sender.Send(new ReciboPagoTimbradoCommand(Guid.NewGuid(), new Millet.CuentasPorCobrar.Application.EventListeners.ReciboPagoTimbradoPayload(
            Empresa, DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid().ToString(), 50_000m, 0m,
            facturas.Select(f => new Millet.CuentasPorCobrar.Application.EventListeners.ReppFacturaPagadaPayload(f.FacturaVentaId, f.Total, 1, "MXN", 0m)).ToList(),
            movimientoId)));

        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var ids = facturas.Select(f => f.Id).ToList();
        (await cxc.FacturasCartera.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync())
            .Should().HaveCount(3).And.OnlyContain(f => f.Estado == EstadoFacturaCartera.Pagada);
        var final = await cxc.PropuestasAplicacionPago.AsNoTracking().SingleAsync(p => p.Id == propuestaId);
        final.Estado.Should().Be(EstadoPropuestaAplicacion.Confirmada);
        final.ReppTimbrado.Should().BeTrue();
    }

    [Fact]
    public async Task Sobrante_de_la_confirmacion_vuelve_a_la_bandeja_por_identificar()
    {
        await using var app = ConPeriodoAbierto();
        var (client, _) = await Login(app);
        var cliente = NuevoCliente();
        var movimientoId = await RegistrarCobroDeCliente(client, 50_000m);
        var facturas = await TresFacturasPpd(cliente);
        // El cliente detalla solo 45,000; 5,000 quedan como saldo a favor por identificar.
        var propuestaId = await ProponerComoOtroUsuario(cliente, movimientoId, 50_000m,
            [(facturas[0].Uuid, facturas[0].Id, 20_000m), (facturas[1].Uuid, facturas[1].Id, 20_000m), (facturas[2].Uuid, facturas[2].Id, 5_000m)]);
        var deposito = await DepositoDe(propuestaId);

        (await Post(client, $"{Tes}/depositos/{deposito.Id}/confirmar", new { movimientoBancarioId = movimientoId }, deposito.Version))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await Movimiento(movimientoId)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.AplicadoParcial);
        (await PorIdentificar(client)).Should().ContainSingle(d => d.MovimientoBancarioId == movimientoId)
            .Which.Disponible.Should().Be(5_000m);
    }

    [Fact]
    public async Task Deposito_identificado_en_dos_partes_genera_dos_pendientes_de_REP()
    {
        await using var app = ConPeriodoAbierto();
        var (client, _) = await Login(app);
        var cliente = NuevoCliente();
        var movimientoId = await RegistrarCobroDeCliente(client, 50_000m);
        var facturas = await TresFacturasPpd(cliente);
        // A: 45,000 a facturas y 5,000 a favor; B: el sobrante de 5,000 contra la tercera factura.
        var propuestaA = await ProponerComoOtroUsuario(cliente, movimientoId, 50_000m,
            [(facturas[0].Uuid, facturas[0].Id, 20_000m), (facturas[1].Uuid, facturas[1].Id, 20_000m), (facturas[2].Uuid, facturas[2].Id, 5_000m)]);
        await Confirmar(client, propuestaA, movimientoId);
        var propuestaB = await ProponerComoOtroUsuario(cliente, movimientoId, 5_000m,
            [(facturas[2].Uuid, facturas[2].Id, 5_000m)]);
        await Confirmar(client, propuestaB, movimientoId);
        (await Movimiento(movimientoId)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);

        // Tesorería → Facturación: cada confirmación crea su pendiente de REP.
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var eventos = (await tes.OutboxEntries.AsNoTracking()
                .Where(e => e.EventType == ResolverPropuestaTesoreriaHandler.ConfirmadaEventType).ToListAsync())
            .Where(e => e.Payload.Contains(movimientoId.ToString())).ToList();
        eventos.Should().HaveCount(2);
        foreach (var evento in eventos)
        {
            var pago = JsonSerializer.Deserialize<Millet.Facturacion.Application.EventListeners.PagoClienteConfirmadoPayload>(evento.Payload, JsonListener)!;
            await sender.Send(new Millet.Facturacion.Application.EventListeners.EmitirReppDesdePagoConfirmadoCommand(evento.Id, pago));
        }

        var fac = scope.ServiceProvider.GetRequiredService<Millet.Facturacion.Infrastructure.Persistence.FacturacionDbContext>();
        var pendientes = await fac.ReppPendientes.AsNoTracking().Where(p => p.MovimientoBancarioId == movimientoId).ToListAsync();
        pendientes.Select(p => (p.PropuestaId, p.Monto)).Should().BeEquivalentTo([((Guid?)propuestaA, 45_000m), ((Guid?)propuestaB, 5_000m)]);

        // CxC consume ambas confirmaciones y cada REP timbrado (B llega primero) salda sus facturas.
        foreach (var evento in eventos)
        {
            var pago = JsonSerializer.Deserialize<PagoClienteConfirmadoIntegrationEvent>(evento.Payload, JsonListener)!;
            await sender.Send(new ResolverPropuestaTesoreriaCommand(evento.Id, Empresa, pago.PropuestaId!.Value, pago.ConfirmadaPor!.Value, pago.OcurridoEn, movimientoId, null));
        }
        await sender.Send(Timbrado(movimientoId, propuestaB, [(facturas[2].FacturaVentaId, 5_000m)]));
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        (await cxc.PropuestasAplicacionPago.AsNoTracking().SingleAsync(x => x.Id == propuestaB)).ReppTimbrado.Should().BeTrue();
        (await cxc.PropuestasAplicacionPago.AsNoTracking().SingleAsync(x => x.Id == propuestaA)).ReppTimbrado.Should().BeFalse();
        await sender.Send(Timbrado(movimientoId, propuestaA,
            [(facturas[0].FacturaVentaId, 20_000m), (facturas[1].FacturaVentaId, 20_000m), (facturas[2].FacturaVentaId, 5_000m)]));

        var ids = facturas.Select(f => f.Id).ToList();
        (await cxc.FacturasCartera.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync())
            .Should().HaveCount(3).And.OnlyContain(f => f.Estado == EstadoFacturaCartera.Pagada);
        (await cxc.PropuestasAplicacionPago.AsNoTracking().SingleAsync(x => x.Id == propuestaA)).ReppTimbrado.Should().BeTrue();
    }

    private static ReciboPagoTimbradoCommand Timbrado(Guid movimientoId, Guid propuestaId, List<(Guid FacturaVentaId, decimal Importe)> lineas) =>
        new(Guid.NewGuid(), new Millet.CuentasPorCobrar.Application.EventListeners.ReciboPagoTimbradoPayload(
            Empresa, DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid().ToString(), lineas.Sum(l => l.Importe), 0m,
            lineas.Select(l => new Millet.CuentasPorCobrar.Application.EventListeners.ReppFacturaPagadaPayload(l.FacturaVentaId, l.Importe, 1, "MXN", 0m)).ToList(),
            movimientoId, propuestaId));

    private async Task Confirmar(HttpClient client, Guid propuestaId, Guid movimientoId)
    {
        var deposito = await DepositoDe(propuestaId);
        var r = await Post(client, $"{Tes}/depositos/{deposito.Id}/confirmar", new { movimientoBancarioId = movimientoId }, deposito.Version);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    }

    // --------------------------------------------------- helpers

    private WebApplicationFactory<Program> ConPeriodoAbierto() => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>();
        services.AddSingleton<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort, PeriodoAbierto>();
    }));

    private Guid NuevoCliente()
    {
        var id = Guid.NewGuid();
        _clientes.Add(id);
        return id;
    }

    private async Task<Guid> RegistrarCobroDeCliente(HttpClient client, decimal monto)
    {
        Guid cuentaId;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var cuenta = new CuentaBancaria(Empresa, "Banco ficticio C74", DateTime.UtcNow.Ticks.ToString(), null, "MXN");
            db.CuentasBancarias.Add(cuenta);
            await db.SaveChangesAsync();
            cuentaId = cuenta.Id;
            _cuentas.Add(cuentaId);
        }
        var alta = await Post(client, $"{Tes}/movimientos", new
        {
            cuentaBancariaId = cuentaId, monto, fechaValor = Fecha,
            referenciaBancaria = $"SPEI-C74-{Guid.NewGuid():N}"[..20], beneficiarioTipo = (int)BeneficiarioTipo.Cliente,
        });
        alta.StatusCode.Should().Be(HttpStatusCode.Created, await alta.Content.ReadAsStringAsync());
        return (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<List<FacturaCartera>> TresFacturasPpd(Guid cliente)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var facturas = Totales.Select((total, i) => FacturaCartera.Crear(
            Empresa, Guid.NewGuid(), cliente, "AAA010101AAA", "Cliente ficticio C74", Guid.NewGuid().ToString(),
            $"C74-{i + 1}", total, "MXN", "PPD", DateTimeOffset.UtcNow.AddDays(-10 + i), DateTimeOffset.UtcNow.AddDays(20 + i))).ToList();
        db.FacturasCartera.AddRange(facturas);
        await db.SaveChangesAsync();
        _facturas.AddRange(facturas.Select(f => f.Id));
        return facturas;
    }

    private static CrearPropuestaAplicacionCommand Comando(Guid cliente, Guid movimientoId, List<FacturaCartera> facturas,
        string remittance = "REM-C74") =>
        new(cliente, "SPEI-C74", facturas.Sum(f => f.Total), "MXN", remittance,
            facturas.Select(f => new PropuestaFacturaLinea(f.Uuid, f.Total, null)).ToList(), movimientoId);

    private async Task<Guid> ProponerComoOtroUsuario(Guid cliente, Guid movimientoId, decimal monto,
        List<(string Uuid, Guid Id, decimal Importe)> lineas)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var propuesta = PropuestaAplicacionPago.Crear(Empresa, cliente, "SPEI-C74", monto, "MXN", "REM-C74",
            lineas.Select(l => (l.Uuid, l.Id, l.Importe, (int?)1)).ToList(), 0m, Guid.NewGuid(), movimientoId);
        db.PropuestasAplicacionPago.Add(propuesta);
        var cartera = await db.FacturasCartera.AsNoTracking().Where(f => lineas.Select(l => l.Id).Contains(f.Id)).ToListAsync();
        await db.SaveChangesAsync();
        // Entrega directa al listener de Tesorería del evento que CxC publicaría (sin Service Bus).
        var payload = new PropuestaAplicacionCreadaPayload(Empresa, DateTimeOffset.UtcNow, propuesta.Id, cliente,
            propuesta.DepositoRef, monto, "MXN", propuesta.AjusteNoFiscal, lineas.Count,
            propuesta.Facturas.Select(f => new PropuestaFacturaPayload(
                cartera.Single(c => c.Id == f.FacturaCarteraId).FacturaVentaId, "C74", f.ImporteAplicado)).ToList(),
            propuesta.PropuestoPor, propuesta.SaldoAFavorPorIdentificar, movimientoId);
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), payload));
        return propuesta.Id;
    }

    /// <summary>Entrega el propuesta-aplicacion.creada.v1 de CxC al listener de Tesorería (sin Service Bus).</summary>
    private async Task ProyectarEnTesoreria(Guid propuestaId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var entrada = (await cxc.OutboxEntries.AsNoTracking()
                .Where(e => e.EventType == PropuestaAplicacionCreadaPayload.EventType).ToListAsync())
            .Single(e => e.Payload.Contains(propuestaId.ToString()));
        var payload = JsonSerializer.Deserialize<PropuestaAplicacionCreadaPayload>(entrada.Payload, JsonListener)!;
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ProyectarPropuestaAplicacionCommand(entrada.Id, payload));
    }

    private async Task<DepositoConfirmacion> DepositoDe(Guid propuestaId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().DepositosConfirmacion.AsNoTracking()
            .SingleAsync(d => d.PropuestaCxcId == propuestaId);
    }

    private async Task<MovimientoBancario> Movimiento(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>().MovimientosBancarios.AsNoTracking()
            .SingleAsync(m => m.Id == id);
    }

    private static async Task<List<DepositoPorIdentificarResponse>> PorIdentificar(HttpClient client)
    {
        var r = await client.GetAsync($"{Cxc}/depositos-por-identificar");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<List<DepositoPorIdentificarResponse>>(Json))!;
    }

    private static async Task<(HttpClient, Guid)> Login(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "c74@dev.local", nombre = "Pruebas C74", empresaId = (Guid?)null });
        r.EnsureSuccessStatusCode();
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

    private sealed class PeriodoAbierto : Millet.Tesoreria.Domain.Ports.IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var idsMovimientos = await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).Select(m => m.Id).ToListAsync();
        var fac = scope.ServiceProvider.GetRequiredService<Millet.Facturacion.Infrastructure.Persistence.FacturacionDbContext>();
        await fac.ReppPendientes.Where(p => idsMovimientos.Contains(p.MovimientoBancarioId)).ExecuteDeleteAsync();
        var movimientos = tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).Select(m => m.Id);
        await tes.DepositosConfirmacion.Where(d => d.MovimientoId != null && movimientos.Contains(d.MovimientoId.Value)).ExecuteDeleteAsync();
        await tes.DepositosConfirmacion.Where(d => d.ClienteId != null && _clientes.Contains(d.ClienteId.Value)).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).ExecuteDeleteAsync();
        await tes.CuentasBancarias.Where(c => _cuentas.Contains(c.Id)).ExecuteDeleteAsync();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        await cxc.PropuestasAplicacionPago.Where(p => _clientes.Contains(p.ClienteId)).ExecuteDeleteAsync();
        await cxc.MovimientosCartera.Where(m => _facturas.Contains(m.FacturaCarteraId)).ExecuteDeleteAsync();
        await cxc.FacturasCartera.Where(f => _facturas.Contains(f.Id)).ExecuteDeleteAsync();
    }
}
