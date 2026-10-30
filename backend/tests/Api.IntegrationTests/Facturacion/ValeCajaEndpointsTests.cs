using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using UglyToad.PdfPig;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// Vale de Caja (86e3ww6tw): el retiro toma un folio consecutivo por caja al
/// registrarse y el PDF se genera desde el retiro guardado (reimpresión con el
/// mismo folio y datos). Endpoint real + PostgreSQL desechable; datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed partial class ValeCajaEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const int TipoDeposito = 2;
    private const int TipoRetiro = 3;
    private const string Motivo = "Pago de flete ficticio de prueba";

    [Fact]
    public async Task Dos_retiros_en_la_misma_caja_llevan_folios_consecutivos_y_otra_caja_empieza_en_1()
    {
        await using var escenario = await EscenarioAsync();
        await using var otra = await EscenarioAsync();

        var primero = await RetiroAsync(escenario, "01", 3000m);
        var segundo = await RetiroAsync(escenario, "01", 150.5m);
        var deOtraCaja = await RetiroAsync(otra, "01", 10m);

        Assert.Equal(1, primero.FolioVale);
        Assert.Equal(2, segundo.FolioVale);
        Assert.Equal(1, deOtraCaja.FolioVale);

        var detalle = await escenario.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/facturacion/cajas/sesiones/{escenario.SesionId}");
        var folios = detalle.GetProperty("movimientos").EnumerateArray()
            .Where(m => m.GetProperty("tipo").GetString() == "Retiro")
            .Select(m => m.GetProperty("folioVale").GetInt32()).Order().ToList();
        Assert.Equal([1, 2], folios);
    }

    [Fact]
    public async Task Vale_imprime_folio_cajero_importe_en_letra_y_motivo_y_la_reimpresion_sale_igual()
    {
        await using var escenario = await EscenarioAsync();
        var retiro = await RetiroAsync(escenario, "01", 3000m);

        var impresion = await escenario.Client.GetAsync(Url(retiro.Id));
        var reimpresion = await escenario.Client.GetAsync(Url(retiro.Id));

        Assert.Equal(HttpStatusCode.OK, impresion.StatusCode);
        Assert.Equal("application/pdf", impresion.Content.Headers.ContentType?.MediaType);
        Assert.Equal("vale-de-caja-1.pdf", impresion.Content.Headers.ContentDisposition?.FileName);
        var texto = await TextoAsync(impresion);
        Assert.Contains("VALEDECAJA", texto);
        Assert.Contains("Folio:1", texto);
        Assert.Contains($"Recibíde{escenario.Cajero.Replace(" ", "")}lacantidadde$3,000.00M.N.", texto);
        Assert.Contains("(TRESMILPESOS00/100M.N.)", texto);
        Assert.Contains($"porconceptode{Motivo.Replace(" ", "")}", texto);
        Assert.Contains("Elaboró", texto);
        Assert.Contains("Nombreyfirma", texto);
        Assert.Contains("Vo.Bo.deTesorería", texto);
        Assert.Contains("Liquidadoeldía:", texto);
        Assert.Contains("Estedocumentoesuncomprobanteinternoydeberáconservarseconelsoportecorrespondiente.", texto);

        Assert.Equal("vale-de-caja-1.pdf", reimpresion.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(SinFechaImpresion(texto), SinFechaImpresion(await TextoAsync(reimpresion)));
    }

    [Fact]
    public async Task Retiro_con_cheque_marca_la_casilla_Cheque()
    {
        await using var escenario = await EscenarioAsync();
        var retiro = await RetiroAsync(escenario, "02", 500m);

        using var pdf = PdfDocument.Open(await (await escenario.Client.GetAsync(Url(retiro.Id))).Content.ReadAsByteArrayAsync());
        var palabras = pdf.GetPage(1).GetWords().ToList();
        var marca = Assert.Single(palabras, w => w.Text == "X");
        var cheque = Assert.Single(palabras, w => w.Text == "Cheque");
        var efectivo = Assert.Single(palabras, w => w.Text == "Efectivo");

        // La X queda dentro de la casilla inmediatamente a la izquierda de «Cheque».
        Assert.InRange(cheque.BoundingBox.Left - marca.BoundingBox.Right, 0, 12);
        Assert.True(marca.BoundingBox.Left > efectivo.BoundingBox.Right);
    }

    [Fact]
    public async Task Un_deposito_no_tiene_vale()
    {
        await using var escenario = await EscenarioAsync();
        var response = await escenario.Client.PostAsJsonAsync(
            $"/api/v1/facturacion/cajas/sesiones/{escenario.SesionId}/movimientos",
            new { Tipo = TipoDeposito, FormaPago = "01", Importe = 100m, Descripcion = "Depósito ficticio" });
        response.EnsureSuccessStatusCode();
        var deposito = (await response.Content.ReadFromJsonAsync<Movimiento>())!;

        Assert.Null(deposito.FolioVale);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await escenario.Client.GetAsync(Url(deposito.Id))).StatusCode);
    }

    private static string Url(Guid movimientoId) => $"/api/v1/facturacion/cajas/movimientos/{movimientoId}/vale.pdf";

    private static async Task<Movimiento> RetiroAsync(Escenario escenario, string formaPago, decimal importe)
    {
        var response = await escenario.Client.PostAsJsonAsync(
            $"/api/v1/facturacion/cajas/sesiones/{escenario.SesionId}/movimientos",
            new { Tipo = TipoRetiro, FormaPago = formaPago, Importe = importe, Descripcion = Motivo });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Movimiento>())!;
    }

    // Texto del PDF sin espacios: QuestPDF coloca cada glifo y PdfPig no siempre recupera los espacios.
    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        using var doc = PdfDocument.Open(await respuesta.Content.ReadAsByteArrayAsync());
        return Espacios().Replace(string.Join(" ", doc.GetPages().Select(p => p.Text)), "");
    }

    private static string SinFechaImpresion(string texto) => FechaImpresion().Replace(texto, "Fechadeimpresión:");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"Fechadeimpresión:\d{2}/\d{2}/\d{4}")]
    private static partial Regex FechaImpresion();

    /// <summary>
    /// Caja nueva con una sesión abierta hoy por el super admin de desarrollo; al
    /// terminar borra movimientos, sesión y caja para no alterar otras suites.
    /// </summary>
    private async Task<Escenario> EscenarioAsync()
    {
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());

        var scope = factory.Services.CreateScope();
        var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var cajero = await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Usuarios
            .AsNoTracking().FirstAsync(u => u.EntraOid == "dev-superadmin");
        var empresaId = json.GetProperty("empresas").EnumerateArray()
            .Single(e => e.GetProperty("esLaActual").GetBoolean()).GetProperty("id").GetGuid();

        var ahora = DateTimeOffset.UtcNow;
        var caja = Caja.Crear(empresaId, $"VALE-{Guid.NewGuid():N}", null);
        var sesion = CajaSesion.Abrir(empresaId, caja.Id, TestComprasFixtures.SucursalMid,
            cajero.Id, 1000m, DiaOperacion.HoyLocal(ahora, null), ahora, null);
        db.AddRange(caja, sesion);
        await db.SaveChangesAsync();
        return new Escenario(client, scope, bypass, db, caja.Id, sesion.Id, cajero.Nombre);
    }

    private sealed record Movimiento(Guid Id, Guid SesionId, decimal Importe, int? FolioVale);

    private sealed record Escenario(
        HttpClient Client, IServiceScope Scope, IDisposable Bypass, FacturacionDbContext Db,
        Guid CajaId, Guid SesionId, string Cajero) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.CajaMovimientos.Where(m => m.CajaSesionId == SesionId).ExecuteDeleteAsync();
            await Db.CajaSesiones.Where(s => s.Id == SesionId).ExecuteDeleteAsync();
            await Db.Cajas.Where(c => c.Id == CajaId).ExecuteDeleteAsync();
            Bypass.Dispose();
            Scope.Dispose();
            Client.Dispose();
        }
    }
}
