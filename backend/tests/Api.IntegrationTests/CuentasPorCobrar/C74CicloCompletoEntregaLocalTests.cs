using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Series;
using Millet.Api.Infrastructure;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// F1-CXC-03 · C7.4 — ciclo completo con entrega local de eventos (C1.8, sin
/// Service Bus): depósito sin identificar de 50,000 → propuesta A (45,000 a 3
/// facturas PPD, 5,000 a favor) → confirmación por otro usuario → REP timbrado
/// (PAC simulado) → el sobrante reaparece → propuesta B (5,000) → confirmación →
/// segundo REP → las 3 facturas Pagadas en cartera y ambas confirmaciones con REP.
/// Los eventos viajan solos: cada paso solo hace un tick del
/// <c>OutboxPublisherWorker</c> de los módulos con el sender local (sondeo acotado).
/// </summary>
public sealed class C74CicloCompletoEntregaLocalTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Cxc = "/api/v1/cuentas-por-cobrar/propuestas-aplicacion";
    private static readonly decimal[] Totales = [20_000m, 20_000m, 10_000m];

    [Fact]
    public async Task Deposito_de_50000_identificado_en_dos_partes_salda_las_tres_facturas_con_dos_REP()
    {
        await using var app = HostConEntregaLocal();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        // CxC propone (solo permisos de CxC); Tesorería/Facturación con otro usuario (R12).
        var cxc = await ClienteConPermisosAsync(app, PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
            PermisosCanonicos.CuentasPorCobrarCarteraLeerTodasSucursales, PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales);
        var tesoreria = await ClienteConPermisosAsync(app, PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray());
        var cliente = Guid.NewGuid();
        var facturas = await SembrarFacturasPpdAsync(app, cliente, hoy);

        // 1. Tesorería registra el cobro de cliente sin identificar (CA7.9).
        var cuentaId = await CuentaAsync(app);
        var alta = await tesoreria.PostAsJsonAsync("/api/v1/tesoreria/movimientos", new
        {
            cuentaBancariaId = cuentaId, monto = 50_000m, fechaValor = hoy,
            referenciaBancaria = $"SPEI-CICLO-{Guid.NewGuid():N}"[..20], beneficiarioTipo = (int)BeneficiarioTipo.Cliente,
        });
        alta.StatusCode.Should().Be(HttpStatusCode.Created, await alta.Content.ReadAsStringAsync());
        var movimientoId = (await alta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await PorIdentificarAsync(cxc, movimientoId)).Should().Be(50_000m);

        // 2. Propuesta A: 45,000 a las tres facturas; 5,000 a favor por identificar.
        var propuestaA = await ProponerAsync(cxc, cliente, movimientoId, 50_000m,
            [(facturas[0].Uuid, 20_000m), (facturas[1].Uuid, 20_000m), (facturas[2].Uuid, 5_000m)]);
        await CicloConfirmacionYRepAsync(app, tesoreria, propuestaA, movimientoId);

        // 3. El sobrante reaparece en la bandeja y se identifica con la propuesta B.
        (await PorIdentificarAsync(cxc, movimientoId)).Should().Be(5_000m);
        var propuestaB = await ProponerAsync(cxc, cliente, movimientoId, 5_000m, [(facturas[2].Uuid, 5_000m)]);
        await CicloConfirmacionYRepAsync(app, tesoreria, propuestaB, movimientoId);

        // 4. Cartera saldada, ambas confirmaciones con REP y dos REP emitidos.
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cartera = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var ids = facturas.Select(f => f.CarteraId).ToList();
        (await cartera.FacturasCartera.AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync())
            .Should().HaveCount(3).And.OnlyContain(f => f.Estado == EstadoFacturaCartera.Pagada);
        (await cartera.PropuestasAplicacionPago.AsNoTracking().Where(p => p.Id == propuestaA || p.Id == propuestaB).ToListAsync())
            .Should().HaveCount(2).And.OnlyContain(p => p.Estado == EstadoPropuestaAplicacion.Confirmada && p.ReppTimbrado);
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await tes.DepositosConfirmacion.AsNoTracking().Where(d => d.PropuestaCxcId == propuestaA || d.PropuestaCxcId == propuestaB).ToListAsync())
            .Should().HaveCount(2).And.OnlyContain(d => d.Estado == EstadoDepositoConfirmacion.Confirmada && d.ReppTimbrado && d.MovimientoId == movimientoId);
        (await tes.MovimientosBancarios.AsNoTracking().SingleAsync(m => m.Id == movimientoId)).EstadoAplicacion
            .Should().Be(EstadoAplicacionMovimiento.Aplicado);
        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        (await fac.ReppPendientes.AsNoTracking().Where(p => p.MovimientoBancarioId == movimientoId).ToListAsync())
            .Should().HaveCount(2).And.OnlyContain(p => p.Estado == EstadoReppPendiente.Emitido && p.ReciboPagoId != null);
    }

    /// <summary>Proyección en Tesorería → confirmación por otro usuario → pendiente de REP → emisión → cartera.</summary>
    private static async Task CicloConfirmacionYRepAsync(WebApplicationFactory<Program> app, HttpClient tesoreria, Guid propuestaId, Guid movimientoId)
    {
        var deposito = await EsperarAsync(app, sp => sp.GetRequiredService<TesoreriaDbContext>().DepositosConfirmacion.AsNoTracking()
            .SingleOrDefaultAsync(d => d.PropuestaCxcId == propuestaId), "proyección del depósito en Tesorería");
        deposito.MovimientoId.Should().Be(movimientoId, "Tesorería preselecciona el movimiento identificado por CxC");

        using (var confirmar = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/tesoreria/depositos/{deposito.Id}/confirmar")
               { Content = JsonContent.Create(new { movimientoBancarioId = movimientoId }) })
        {
            confirmar.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            confirmar.Headers.Add("X-Expected-Version", deposito.Version.ToString());
            var r = await tesoreria.SendAsync(confirmar);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        }

        var pendiente = await EsperarAsync(app, sp => sp.GetRequiredService<FacturacionDbContext>().ReppPendientes.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PropuestaId == propuestaId), "pendiente de REP en Facturación");
        await EsperarAsync(app, async sp => await sp.GetRequiredService<CuentasPorCobrarDbContext>().PropuestasAplicacionPago.AsNoTracking()
            .AnyAsync(p => p.Id == propuestaId && p.Estado == EstadoPropuestaAplicacion.Confirmada) ? (object)true : null, "confirmación en CxC");

        using (var emitir = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/facturacion/repp/pendientes/{pendiente.Id}/emitir")
               { Content = JsonContent.Create(new { }) })
        {
            emitir.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var r = await tesoreria.SendAsync(emitir);
            var cuerpo = await r.Content.ReadAsStringAsync();
            r.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
            JsonDocument.Parse(cuerpo).RootElement.GetProperty("emitido").GetBoolean().Should().BeTrue(cuerpo);
        }

        await EsperarAsync(app, async sp => await sp.GetRequiredService<CuentasPorCobrarDbContext>().PropuestasAplicacionPago.AsNoTracking()
            .AnyAsync(p => p.Id == propuestaId && p.ReppTimbrado) ? (object)true : null, "REP timbrado aplicado en CxC");
        await EsperarAsync(app, async sp => await sp.GetRequiredService<TesoreriaDbContext>().DepositosConfirmacion.AsNoTracking()
            .AnyAsync(d => d.PropuestaCxcId == propuestaId && d.ReppTimbrado) ? (object)true : null, "REP timbrado marcado en Tesorería");
    }

    // ================= entrega local =================

    private WebApplicationFactory<Program> HostConEntregaLocal() => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting(EntregaLocalIntegrationEventsExtensions.ClaveHabilitada, "true");
        b.ConfigureTestServices(s =>
        {
            // Sin outbox de fondo (los ticks son explícitos) ni listeners de Service Bus.
            foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType is { } t
                         && ((t.IsGenericType && t.GetGenericTypeDefinition() == typeof(OutboxPublisherWorker<>))
                             || t.Name.EndsWith("EventListenerWorker", StringComparison.Ordinal))).ToList())
                s.Remove(d);
            // PAC, repositorio, folios y periodos simulados (mismos que ReppPendientesTests).
            s.RemoveAll<ICfdiTimbradoPort>(); s.AddSingleton<ICfdiTimbradoPort, Pac>();
            s.RemoveAll<ICfdiRepositorioPort>(); s.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            s.RemoveAll<Millet.Facturacion.Domain.Ports.IPeriodoContablePort>();
            s.AddSingleton<Millet.Facturacion.Domain.Ports.IPeriodoContablePort, PeriodoFacturacion>();
            s.RemoveAll<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>();
            s.AddSingleton<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort, PeriodoTesoreria>();
            s.RemoveAll<IReppBancarioReadPort>(); s.AddSingleton<IReppBancarioReadPort, Banco>();
            s.RemoveAll<IClientesReadPort>(); s.AddSingleton<IClientesReadPort, Clientes>();
            s.RemoveAll<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>>();
            s.AddSingleton<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>, Folio>();
        });
    });

    /// <summary>Hace ticks del outbox de CxC, Tesorería y Facturación hasta que la condición se cumple (máx. 15).</summary>
    private static async Task<T> EsperarAsync<T>(WebApplicationFactory<Program> app, Func<IServiceProvider, Task<T?>> condicion, string que)
        where T : class
    {
        for (var tick = 0; tick < 15; tick++)
        {
            using (var scope = app.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                if (await condicion(scope.ServiceProvider) is { } listo) return listo;
            }
            await TickAsync<CuentasPorCobrarDbContext>(app);
            await TickAsync<TesoreriaDbContext>(app);
            await TickAsync<FacturacionDbContext>(app);
        }
        throw new Xunit.Sdk.XunitException($"No llegó: {que} (15 ticks del outbox con entrega local).");
    }

    private static Task TickAsync<TContext>(WebApplicationFactory<Program> app) where TContext : DbContext =>
        new OutboxPublisherWorker<TContext>(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            app.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(),
            NullLogger<OutboxPublisherWorker<TContext>>.Instance).PollOnceAsync(CancellationToken.None);

    // ================= datos =================

    private sealed record FacturaSembrada(Guid FacturaVentaId, Guid CarteraId, string Uuid);

    /// <summary>Tres facturas PPD timbradas en Facturación y su proyección en cartera de CxC.</summary>
    private static async Task<List<FacturaSembrada>> SembrarFacturasPpdAsync(WebApplicationFactory<Program> app, Guid cliente, DateOnly hoy)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cxc = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var resultado = new List<FacturaSembrada>();
        for (var i = 0; i < Totales.Length; i++)
        {
            var f = FacturaVenta.CrearBorrador(Empresa, $"C74-{Guid.NewGuid():N}", 1, Guid.NewGuid(), null, null,
                new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio C7.4", "601", "97000", "G03", "MEX", false),
                new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio C7.4", "601", "97000"), "PPD", "99", "MXN", null,
                hoy.Year, hoy.Month, canalVentaId: 7, comportamientoFiscal: ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            f.AgregarLinea(null, "01010101", "Prueba C7.4", "E48", 1m, Totales[i], 0m, "01", null, null, null);
            f.RecalcularTotales(); f.MarcarTimbradoEnProceso();
            var uuid = Guid.NewGuid().ToString();
            f.MarcarTimbrado(uuid, null, null, null, DateTimeOffset.UtcNow, null, null);
            fac.FacturasVenta.Add(f);
            var cartera = FacturaCartera.Crear(Empresa, f.Id, cliente, "AAA010101AAA", "Cliente ficticio C7.4", uuid,
                $"C74-{i + 1}", f.Total, "MXN", "PPD", DateTimeOffset.UtcNow.AddDays(-10 + i), DateTimeOffset.UtcNow.AddDays(20 + i));
            cxc.FacturasCartera.Add(cartera);
            resultado.Add(new(f.Id, cartera.Id, uuid));
        }
        await fac.SaveChangesAsync();
        await cxc.SaveChangesAsync();
        return resultado;
    }

    private static async Task<Guid> CuentaAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var cuenta = new CuentaBancaria(Empresa, "Banco ficticio C74 ciclo", DateTime.UtcNow.Ticks.ToString(), null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        await db.SaveChangesAsync();
        return cuenta.Id;
    }

    private static async Task<decimal?> PorIdentificarAsync(HttpClient cxc, Guid movimientoId)
    {
        var r = await cxc.GetAsync($"{Cxc}/depositos-por-identificar");
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var lista = await r.Content.ReadFromJsonAsync<List<DepositoPorIdentificarResponse>>(JsonSerializerOptions.Web);
        return lista!.SingleOrDefault(d => d.MovimientoBancarioId == movimientoId)?.Disponible;
    }

    private static async Task<Guid> ProponerAsync(HttpClient cxc, Guid cliente, Guid movimientoId, decimal monto,
        List<(string Uuid, decimal Importe)> lineas)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Cxc)
        {
            Content = JsonContent.Create(new CrearPropuestaAplicacionCommand(cliente, "SPEI-CICLO", monto, "MXN", "REM-CICLO",
                lineas.Select(l => new PropuestaFacturaLinea(l.Uuid, l.Importe, null)).ToList(), movimientoId)),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var r = await cxc.SendAsync(request);
        var cuerpo = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(HttpStatusCode.Created, cuerpo);
        return JsonDocument.Parse(cuerpo).RootElement.GetProperty("id").GetGuid();
    }

    // ================= dobles =================

    private sealed class Folio : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
    {
        public Task<ReservarFolioResponse> Handle(ReservarFolioCommand request, CancellationToken cancellationToken) =>
            Task.FromResult(new ReservarFolioResponse($"C74-{Guid.NewGuid():N}", 1, ""));
    }

    private sealed class Banco : IReppBancarioReadPort
    {
        public Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) => Task.FromResult<decimal?>(20m);
        public Task<Guid> SucursalEmisoraAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.Parse("40000000-0000-0000-0000-000000000012"));
    }

    private sealed class PeriodoFacturacion : Millet.Facturacion.Domain.Ports.IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class PeriodoTesoreria : Millet.Tesoreria.Domain.Ports.IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Clientes : IClientesReadPort
    {
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ClienteFiscalLectura?>(new(id, "AAA010101AAA", "Cliente ficticio C7.4", "601", "97000", "G03", "03", "PPD", "MXN", false));
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken cancellationToken) =>
            ObtenerAsync(Guid.NewGuid(), cancellationToken);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? nombre, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClienteBusquedaItem>>([]);
    }

    private sealed class Pac : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken) => Task.FromResult(new TimbradoResultado(
            TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<prueba-ficticia/>", null, null));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Repositorio : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
    }
}
