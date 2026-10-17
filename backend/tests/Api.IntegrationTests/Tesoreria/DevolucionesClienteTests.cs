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
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
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
    private Guid? _cliente;
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
        Guid clienteId;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var cliente = new Cliente(Guid.CreateVersion7(), $"DV{DateTime.UtcNow.Ticks % 100000000}", "CLIENTE DEVOLUCION PRUEBA");
            compartido.Clientes.Add(cliente); await compartido.SaveChangesAsync();
            _cliente = cliente.Id; clienteId = cliente.Id;
        }
        var deposito = await Deposito();
        var solicitud = await Post(client, $"{Tes}/movimientos/{deposito.Id}/solicitar-devolucion",
            new { clienteId, importe = 1000m, motivo = "Depósito erróneo P-15", banco = "BBVA", clabe = "032180000118359719", titular = "Cliente SA" });
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
        (await pagada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").GetInt32().Should().Be((int)EstadoDevolucionCliente.Pagada);
        await Post(client, $"{Tes}/devoluciones/{devolucionId}/pagar", cuerpo, version, llave);

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

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var movimientos = tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).Select(m => m.Id);
        await tes.DevolucionesCliente.Where(d => movimientos.Contains(d.MovimientoOrigenId!.Value)).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId) && m.ContramovimientoDe != null).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).ExecuteDeleteAsync();
        await tes.CuentasBancarias.Where(c => _cuentas.Contains(c.Id)).ExecuteDeleteAsync();
        if (_cliente is Guid cid)
            await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Clientes.Where(c => c.Id == cid).ExecuteDeleteAsync();
    }

    private static async Task<(HttpClient, Guid)> Login(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "dev@dev.local", nombre = "Pruebas DEV", empresaId = (Guid?)null }); r.EnsureSuccessStatusCode();
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
