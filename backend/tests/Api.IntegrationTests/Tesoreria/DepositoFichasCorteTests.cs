using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

// 86e3vr1tr · escrita, corre en CI (PostgreSQL desechable). Corte de caja cubierto con 2 fichas vía API.
[Collection(ApiCompartida.Nombre)]
public sealed class DepositoFichasCorteTests(WebApplicationFactory<Program> factory) : IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DateOnly DiaCorte = new(2026, 11, 2);
    private const string Tes = "/api/v1/tesoreria";
    private readonly List<Guid> _cuentas = [];
    private readonly List<Guid> _depositos = [];

    [Fact]
    public async Task Dos_fichas_con_vale_dejan_parcial_y_luego_confirmado_con_un_evento_por_ficha()
    {
        var (client, user) = await Login();
        Guid f1, f2; DepositoConfirmacion corte;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var cuenta = new CuentaBancaria(Empresa, "Banco ficticio fichas", DateTime.UtcNow.Ticks.ToString(), null, "MXN");
            var m1 = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 2500, DiaCorte, "F-1", null, null, null, user, DateTimeOffset.UtcNow);
            var m2 = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1700, DiaCorte.AddDays(1), "F-2", null, null, null, user, DateTimeOffset.UtcNow);
            corte = DepositoConfirmacion.CrearExpectativaCaja(Empresa, Guid.NewGuid(), 4200, DiaCorte, Guid.NewGuid(), Guid.NewGuid());
            db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.AddRange(m1, m2); db.DepositosConfirmacion.Add(corte);
            await db.SaveChangesAsync();
            _cuentas.Add(cuenta.Id); _depositos.Add(corte.Id); f1 = m1.Id; f2 = m2.Id;
        }

        var sinSoporte = await Post(client, $"{Tes}/depositos/{corte.Id}/confirmar", new { movimientoBancarioId = f1 }, corte.Version);
        sinSoporte.StatusCode.Should().Be(HttpStatusCode.OK);
        var r1 = await sinSoporte.Content.ReadFromJsonAsync<JsonElement>();
        r1.GetProperty("saldoPorDepositar").GetDecimal().Should().Be(1700);

        var rechazada = await Post(client, $"{Tes}/depositos/{corte.Id}/confirmar", new { movimientoBancarioId = f2 }, r1.GetProperty("version").GetInt32());
        rechazada.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await rechazada.Content.ReadAsStringAsync()).Should().Contain("DEP_FICHA_SOPORTE_REQUERIDO");

        var ok = await Post(client, $"{Tes}/depositos/{corte.Id}/confirmar",
            new { movimientoBancarioId = f2, soporteTipo = 1, soporteFolio = "VC-0001" }, r1.GetProperty("version").GetInt32());
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verificar = factory.Services.CreateScope();
        using var bypass2 = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await tes.DepositosConfirmacion.SingleAsync(d => d.Id == corte.Id)).Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        (await tes.DepositoFichas.CountAsync(f => f.DepositoConfirmacionId == corte.Id)).Should().Be(2);
        // Payload es jsonb: se filtra en memoria (LIKE no existe sobre jsonb).
        var eventos = (await tes.OutboxEntries.Where(e => e.EventType == "tesoreria.deposito-caja.confirmado.v1").ToListAsync())
            .Where(e => e.Payload.Contains(corte.Id.ToString())).ToList();
        eventos.Should().HaveCount(2);
        eventos.Should().OnlyContain(e => e.Payload.Contains("2026-11-02"));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        await tes.DepositosConfirmacion.Where(d => _depositos.Contains(d.Id)).ExecuteDeleteAsync();
        await tes.MovimientosBancarios.Where(m => _cuentas.Contains(m.CuentaBancariaId)).ExecuteDeleteAsync();
        await tes.CuentasBancarias.Where(c => _cuentas.Contains(c.Id)).ExecuteDeleteAsync();
    }

    private async Task<(HttpClient, Guid)> Login()
    {
        var client = factory.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "fichas@dev.local", nombre = "Pruebas fichas", empresaId = (Guid?)null }); r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, body.GetProperty("usuario").GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }
}
