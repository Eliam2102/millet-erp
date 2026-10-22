using System.Net.Http.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    [Fact]
    public async Task P9_Excel_confirma_resuelve_nombres_avisa_responsable_y_es_idempotente_sin_poliza()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        await Preparar(e, false);
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        string sucursal;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            sucursal = (await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales.SingleAsync(x => x.Id == e.SucursalId)).Clave;
        }
        var bytes = ExcelP9(sucursal);
        using var previa = await SubirP9(cliente, bytes, false);
        previa.EnsureSuccessStatusCode();
        var reporte = (await previa.Content.ReadFromJsonAsync<ResultadoCarga>())!;
        Assert.True(reporte.Valida); Assert.Contains(reporte.Avisos!, x => x.Renglon == 4 && x.Columna == "responsable");
        Assert.Equal(500000, reporte.Cuadre.Single().Valor); Assert.Equal(100000, reporte.Cuadre.Single().Acumulada);
        using var confirmar = await SubirP9(cliente, bytes, true); confirmar.EnsureSuccessStatusCode();
        Assert.True((await confirmar.Content.ReadFromJsonAsync<ResultadoCarga>())!.Valida);
        using var repetir = await SubirP9(cliente, bytes, true, "SAP"); repetir.EnsureSuccessStatusCode();
        Assert.True((await repetir.Content.ReadFromJsonAsync<ResultadoCarga>())!.Idempotente);
        using var ver = e.Scope(); using var lectura = ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var activo = await db.Activos.SingleAsync(x => x.Numero == "000-SAP-P9");
        Assert.Equal("153", (await db.Clases.SingleAsync(x => x.Id == activo.ClaseId)).Clave);
        Assert.Equal(new DateOnly(2024, 3, 5), activo.FechaAdquisicion); Assert.Equal(activo.FechaAdquisicion, activo.InicioUso);
        Assert.Equal(400000, activo.ValorEnLibros); Assert.Equal(Guid.Empty, activo.ResponsableId); Assert.Null(activo.PolizaAltaId);
        Assert.Empty(await ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Where(x => x.OrigenModulo == "ActivosFijos").ToListAsync());
    }

    [Theory] [InlineData(true, false)] [InlineData(false, true)]
    public async Task P9_Excel_errores_de_columna_o_clase_no_cargan_nada(bool faltaValor, bool claseInvalida)
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); await Preparar(e, false);
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var bytes = ExcelP9(e.SucursalId.ToString(), faltaValor, claseInvalida);
        foreach (var confirmar in new[] { false, true })
        {
            using var respuesta = await SubirP9(cliente, bytes, confirmar); respuesta.EnsureSuccessStatusCode();
            var reporte = (await respuesta.Content.ReadFromJsonAsync<ResultadoCarga>())!;
            Assert.False(reporte.Valida);
            Assert.Contains(reporte.Errores, x => faltaValor ? x.Columna == "valor" && x.Mensaje.Contains("APC") : x.Renglon == 4 && x.Columna == "clase");
        }
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.AnyAsync(x => x.Numero == "000-SAP-P9"));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task P9_Responsable_por_correo_o_nombre_ambiguo_y_fecha_dia_mes(bool ambiguo)
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); await Preparar(e, false);
        var responsable = await e.ResponsablePruebaAsync(true, true);
        await e.ResponsablePruebaAsync(true, true);
        using var cliente = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        using var stream = new MemoryStream(ExcelP9(e.SucursalId.ToString())); using var libro = new XLWorkbook(stream);
        libro.Worksheet("SAP").Cell(4, 9).Value = ambiguo ? "FIX-CE responsable" : $"ce-{responsable:N}@example.invalid";
        libro.Worksheet("SAP").Cell(4, 4).Value = "05/03/2024";
        using var salida = new MemoryStream(); libro.SaveAs(salida);
        using var respuesta = await SubirP9(cliente, salida.ToArray(), true); respuesta.EnsureSuccessStatusCode();
        var resultado = (await respuesta.Content.ReadFromJsonAsync<ResultadoCarga>())!;
        Assert.Equal(!ambiguo, resultado.Valida);
        if (ambiguo)
        {
            var error = Assert.Single(resultado.Errores); Assert.Equal("responsable", error.Columna);
            Assert.Contains("varias coincidencias", error.Mensaje); Assert.Contains($"ce-{responsable:N}", error.Mensaje);
        }
        else
        {
            using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var activo = await scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.SingleAsync(x => x.Numero == "000-SAP-P9");
            Assert.Equal(responsable, activo.ResponsableId); Assert.Equal(new DateOnly(2024, 3, 5), activo.FechaAdquisicion);
        }
    }

    private static byte[] ExcelP9(string sucursal, bool faltaValor = false, bool claseInvalida = false)
    {
        using var libro = new XLWorkbook(); libro.AddWorksheet("Vacía"); var hoja = libro.AddWorksheet("SAP");
        string[] headers = ["Núm. activo", "Tipo de activo", "Descripción", "Fecha de compra", faltaValor ? "Otra columna" : "Valor", "Dep. acumulada", "Sucursal", "Ubicación", "Responsable"];
        for (var i = 0; i < headers.Length; i++) hoja.Cell(2, i + 1).Value = headers[i];
        hoja.Cell(4, 1).Value = "000-SAP-P9"; hoja.Cell(4, 2).Value = claseInvalida ? "Vehículos" : "Maquinaria y equipo";
        hoja.Cell(4, 3).Value = "Equipo ficticio P9"; hoja.Cell(4, 4).Value = new DateTime(2024, 3, 5);
        hoja.Cell(4, 5).Value = "$500,000.00"; hoja.Cell(4, 6).Value = 100000;
        hoja.Cell(4, 7).Value = sucursal; hoja.Cell(4, 8).Value = "Planta"; hoja.Cell(4, 9).Value = "inexistente-p9@example.invalid";
        using var stream = new MemoryStream(); libro.SaveAs(stream); return stream.ToArray();
    }
    private static async Task<HttpResponseMessage> SubirP9(HttpClient cliente, byte[] contenido, bool confirmar, string? hoja = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(contenido), "archivo", "sap.xlsx");
        form.Add(new StringContent("1"), "versionArchivo"); form.Add(new StringContent("2027-02-28"), "corte");
        form.Add(new StringContent("[{\"clase\":\"153\",\"valor\":500000,\"acumulada\":100000}]"), "totales");
        if (hoja != null) form.Add(new StringContent(hoja), "hoja");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/carga/{(confirmar ? "confirmar" : "previa")}") { Content = form };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await cliente.SendAsync(req);
    }
}
