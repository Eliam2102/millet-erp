using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

// 86e3vr1tj · escrita, corre en CI (PostgreSQL desechable). Autorizar / rechazar la diferencia de un corte vía API.
[Collection(ApiCompartida.Nombre)]
public sealed class DiferenciasCorteTests(WebApplicationFactory<Program> factory) : IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Ruta = "/api/v1/tesoreria/diferencias-corte";
    private readonly List<Guid> _difs = [];

    [Fact]
    public async Task Tesoreria_autoriza_y_publica_el_evento_el_cajero_dueno_no_puede()
    {
        var (client, user) = await Login();
        var propia = await Sembrar(cajero: user, importe: -50m);
        var ajena = await Sembrar(cajero: Guid.NewGuid(), importe: -50m);

        var rechazo = await Post(client, $"{Ruta}/{propia.Id}/autorizar", null, propia.Version);
        rechazo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await rechazo.Content.ReadAsStringAsync()).Should().Contain("DIF_CAJERO_MISMO_USUARIO");

        var ok = await Post(client, $"{Ruta}/{ajena.Id}/autorizar", null, ajena.Version);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await tes.DiferenciasCorte.SingleAsync(d => d.Id == ajena.Id)).Estado.Should().Be(EstadoDiferenciaCorte.Autorizada);
        // Payload es jsonb: se filtra en memoria.
        (await tes.OutboxEntries.Where(e => e.EventType == "tesoreria.diferencia-corte.autorizada.v1").ToListAsync())
            .Count(e => e.Payload.Contains(ajena.Id.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task Rechazo_sin_motivo_no_se_permite_y_con_motivo_queda_por_aclarar()
    {
        var (client, _) = await Login();
        var d = await Sembrar(Guid.NewGuid(), -50m);

        (await Post(client, $"{Ruta}/{d.Id}/rechazar", new { motivo = " " }, d.Version)).StatusCode
            .Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        var ok = await Post(client, $"{Ruta}/{d.Id}/rechazar", new { motivo = "Revisar arqueo" }, d.Version);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("estado").ToString().Should().NotBeEmpty();

        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        (await tes.DiferenciasCorte.SingleAsync(x => x.Id == d.Id)).Estado.Should().Be(EstadoDiferenciaCorte.PorAclarar);
    }

    private async Task<DiferenciaCorte> Sembrar(Guid cajero, decimal importe)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var d = DiferenciaCorte.Crear(Empresa, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), cajero, new DateOnly(2026, 10, 28), importe);
        db.DiferenciasCorte.Add(d);
        await db.SaveChangesAsync();
        _difs.Add(d.Id);
        return d;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        await tes.DiferenciasCorte.Where(d => _difs.Contains(d.Id)).ExecuteDeleteAsync();
    }

    private async Task<(HttpClient, Guid)> Login()
    {
        var client = factory.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "difcorte@dev.local", nombre = "Pruebas diferencias", empresaId = (Guid?)null }); r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, body.GetProperty("usuario").GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object? body, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }
}
