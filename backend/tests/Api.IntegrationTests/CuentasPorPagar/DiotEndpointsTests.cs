using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>F1-CXP-10 · DIOT del mes y archivo de carga del SAT.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class DiotEndpointsTests : IClassFixture<DiotEndpointsTests.Servidor>
{
    private readonly Servidor factory;
    public DiotEndpointsTests(Servidor fixture) => factory = fixture;

    public sealed class Servidor : WebApplicationFactory<Program>
    {
        public AdjuntosProveedorAmbiente Ambiente { get; }
        public Servidor() => Ambiente = new AdjuntosProveedorAmbiente(this);
        public override async ValueTask DisposeAsync()
        {
            await Ambiente.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    // Validador (400) o regla del lector (422): nunca 500.
    private static readonly HttpStatusCode[] Rechazo = [HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity];
    private const string Ruta = "/api/v1/cuentas-por-pagar/reportes/diot";
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    [Fact]
    public async Task Pago_parcial_del_mes_sale_en_el_reporte_y_en_el_archivo_y_sin_permiso_es_403()
    {
        var amb = factory.Ambiente;
        // Mes propio para no cruzarse con otras pruebas de la base compartida.
        var anio = 2040 + Random.Shared.Next(0, 59);
        var mes = Random.Shared.Next(1, 13);
        var proveedorId = await amb.SeedProveedorAsync();

        string rfc;
        using (var scope = amb.Factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compartido = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            rfc = (await compartido.Proveedores.FindAsync(proveedorId))!.Rfc;
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var fecha = new DateTimeOffset(anio, mes, 1, 0, 0, 0, TimeSpan.Zero);
            var f = Factura.CapturarSinOc(Empresa, null, null, proveedorId, Guid.NewGuid(), "DIOT-1", null, fecha, fecha,
                DateOnly.FromDateTime(fecha.UtcDateTime).AddMonths(2), "MXN", null, 1000m, 0m, 160m, 0m, 1160m, "Prueba DIOT", fecha);
            f.AgregarLinea(null, "81112100", "Servicio de prueba", 1, "E48", null, 1000m, 1000m, null, null, null);
            db.FacturasProveedor.Add(f);
            db.PagosProveedorLocal.Add(new PagoProveedorLocal(Empresa, f.Id, Guid.NewGuid(), 580m, new DateOnly(anio, mes, 15)));
            await db.SaveChangesAsync();
        }

        var (cliente, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarReportesDiot);
        var r = await cliente.GetAsync($"{Ruta}?anio={anio}&mes={mes}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var fila = json.GetProperty("filas").EnumerateArray().Single(x => x.GetProperty("rfc").GetString() == rfc);
        Assert.Equal("04", fila.GetProperty("tipo_tercero").GetString());
        Assert.Equal("03", fila.GetProperty("tipo_operacion").GetString());
        Assert.Equal(500m, fila.GetProperty("actos_16").GetDecimal());
        Assert.Equal(80m, fila.GetProperty("iva_pagado").GetDecimal());

        var archivo = await cliente.GetAsync($"{Ruta}/archivo?anio={anio}&mes={mes}");
        Assert.Equal(HttpStatusCode.OK, archivo.StatusCode);
        Assert.Equal("text/plain", archivo.Content.Headers.ContentType?.MediaType);
        var texto = await archivo.Content.ReadAsStringAsync();
        var renglon = texto.Split("\r\n").Single(l => l.Contains(rfc, StringComparison.Ordinal));
        Assert.Equal(54, renglon.Split('|').Length);
        Assert.StartsWith($"04|03|{rfc}|||||||||500||0|", renglon, StringComparison.Ordinal);
        Assert.EndsWith("|01", renglon, StringComparison.Ordinal);

        var (sinPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarReportesCartera);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{Ruta}?anio={anio}&mes={mes}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{Ruta}/archivo?anio={anio}&mes={mes}")).StatusCode);
    }

    [Fact]
    public async Task Mes_invalido_se_rechaza()
    {
        var (cliente, _) = await factory.Ambiente.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarReportesDiot);
        Assert.Contains((await cliente.GetAsync($"{Ruta}?anio=2026&mes=13")).StatusCode, Rechazo);
        Assert.Contains((await cliente.GetAsync($"{Ruta}/archivo?anio=2026&mes=0")).StatusCode, Rechazo);
    }

    [Theory]
    [InlineData("?anio=dos&mes=11", "Año inválido.")]
    [InlineData("?mes=11", "Año inválido.")]
    [InlineData("/archivo?anio=2026&mes=noviembre", "Mes inválido.")]
    public async Task Anio_o_mes_no_numerico_se_rechaza_en_espanol(string consulta, string mensaje)
    {
        var (cliente, _) = await factory.Ambiente.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarReportesDiot);
        var respuesta = await cliente.GetAsync(Ruta + consulta);
        Assert.Contains(respuesta.StatusCode, Rechazo);
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var textos = Textos(json.RootElement).ToList();
        Assert.Contains(textos, t => t.Contains(mensaje, StringComparison.Ordinal));
        Assert.DoesNotContain(textos, t => t.Contains("Failed to bind", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Textos(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => [e.GetString()!],
        JsonValueKind.Object => e.EnumerateObject().SelectMany(p => Textos(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().SelectMany(Textos),
        _ => [],
    };
}
