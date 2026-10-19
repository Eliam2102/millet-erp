using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Application.Oc.CancelarConRecepciones;
using Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;
using Millet.Compras.Application.Oc.DuplicarOrdenCompra;
using Millet.Compras.Application.Oc.Lineas.AgregarLineaDesdeRequisicion;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Migrations;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;
using Npgsql;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// Matriz de nivel de OC (menor cuantía): el umbral vigente decide si la firma N2 es necesaria.
// Se usa el umbral general de la empresa (DepartamentoId = Guid.Empty) porque la OC no nace de RQ.
public partial class OrdenesCompraEndpointsTests
{
    [Theory]
    [InlineData(5000, 1000, "Autorizada")]              // monto <= UmbralMonto: menor cuantía, basta N1
    [InlineData(500, 100, "EnAutorizacionDireccion")]   // monto > UmbralMonto: exige N1 + N2
    [InlineData(null, null, "EnAutorizacionDireccion")] // sin umbral configurado: fail-safe N1 + N2
    public async Task NivelOc_UmbralDecideSiN1Basta_TrasFirmarN1(int? umbralMonto, int? umbralMenor, string estadoEsperado)
    {
        var admin = await CreateSuperAdminClientAsync();
        await SembrarUmbralGeneralAsync(umbralMonto, umbralMenor);
        try
        {
            var id = await CrearOcConLineaEnAutorizacionAsync(admin);
            var firmante = await CreateClientConPermisosAsync("compras.ordenes.autorizar-nivel1", "compras.ordenes.gestionar-todas-sucursales");

            var firma = await firmante.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 });
            firma.EnsureSuccessStatusCode();

            var detalle = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{id}"));
            Assert.Equal((int)Enum.Parse<EstadoOrdenCompra>(estadoEsperado), detalle.GetProperty("estado").GetInt32());
        }
        finally
        {
            await LimpiarUmbralGeneralAsync();
        }
    }

    [Fact]
    public async Task NivelOc_MenorCuantia_N2RechazadaConCodigoPropio()
    {
        var admin = await CreateSuperAdminClientAsync();
        await SembrarUmbralGeneralAsync(5000m, 1000m);
        try
        {
            var id = await CrearOcConLineaEnAutorizacionAsync(admin);
            var firmante = await CreateClientConPermisosAsync("compras.ordenes.autorizar-nivel1", "compras.ordenes.autorizar-nivel2", "compras.ordenes.gestionar-todas-sucursales");
            (await firmante.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();

            var n2 = await firmante.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 2 });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, n2.StatusCode);
            Assert.Contains("OC_AUTORIZAR_NIVEL2_NO_REQUERIDO", await n2.Content.ReadAsStringAsync());
        }
        finally
        {
            await LimpiarUmbralGeneralAsync();
        }
    }

    [Fact]
    public async Task AutorizacionesMasivasOc_DosOk_UnError_YSinPermisoDa403()
    {
        var admin = await CreateSuperAdminClientAsync();
        await SembrarUmbralGeneralAsync(5000m, 1000m);
        try
        {
            var ok1 = await CrearOcConLineaEnAutorizacionAsync(admin);
            var ok2 = await CrearOcConLineaEnAutorizacionAsync(admin);
            var borrador = await CrearOcBorradorAsync(admin); // Borrador: la firma N1 falla por estado

            var firmante = await CreateClientConPermisosAsync("compras.ordenes.autorizar-nivel1", "compras.ordenes.gestionar-todas-sucursales");
            var respuesta = await firmante.PostAsJsonAsync($"{EndpointBase}/autorizaciones-masivas",
                new { Ids = new[] { ok1, ok2, borrador }, Nivel = 1, Notas = (string?)null });
            respuesta.EnsureSuccessStatusCode();

            var resultados = (await ReadJsonAsync(respuesta)).GetProperty("resultados").EnumerateArray().ToList();
            Assert.Equal(3, resultados.Count);
            Assert.Equal(2, resultados.Count(r => r.GetProperty("ok").GetBoolean()));
            var fallido = resultados.Single(r => r.GetProperty("id").GetGuid() == borrador);
            Assert.False(fallido.GetProperty("ok").GetBoolean());
            Assert.Equal("OC_AUTORIZAR_ESTADO_INCORRECTO", fallido.GetProperty("errorCodigo").GetString());

            var sinPermiso = await CreateClientConPermisosAsync("compras.ordenes.leer");
            var denegado = await sinPermiso.PostAsJsonAsync($"{EndpointBase}/autorizaciones-masivas",
                new { Ids = new[] { ok1 }, Nivel = 1, Notas = (string?)null });
            Assert.Equal(HttpStatusCode.Forbidden, denegado.StatusCode);
            Assert.Contains("AUTORIZAR_NIVEL_DENEGADO", await denegado.Content.ReadAsStringAsync());
        }
        finally
        {
            await LimpiarUmbralGeneralAsync();
        }
    }

    [Fact]
    public async Task PendientesAutorizacionOc_DevuelveCamposNuevosDeLaBandeja()
    {
        var admin = await CreateSuperAdminClientAsync();
        await SembrarUmbralGeneralAsync(5000m, 1000m);
        try
        {
            var id = await CrearOcConLineaEnAutorizacionAsync(admin);

            var respuesta = await admin.GetAsync($"{EndpointBase}/pendientes-autorizacion?nivel=1&pageSize=200");
            respuesta.EnsureSuccessStatusCode();
            var item = (await ReadJsonAsync(respuesta)).GetProperty("items").EnumerateArray()
                .Single(i => i.GetProperty("id").GetGuid() == id);

            Assert.Equal(JsonValueKind.String, item.GetProperty("proveedorNombre").ValueKind);
            Assert.Equal(10m, item.GetProperty("cantidadTotal").GetDecimal());
            Assert.Equal(1, item.GetProperty("numeroLineas").GetInt32());
            Assert.True(item.GetProperty("totalAPagar").GetDecimal() > 0m);
            Assert.False(item.GetProperty("requiereNivel2").GetBoolean()); // 1160 <= 5000
            Assert.True(item.TryGetProperty("esMenorCuantia", out _));
            Assert.True(item.TryGetProperty("esRecurrente", out _));
            Assert.True(item.TryGetProperty("proveedorConocido", out _));
        }
        finally
        {
            await LimpiarUmbralGeneralAsync();
        }
    }

    private static async Task<Guid> CrearOcBorradorAsync(HttpClient client)
    {
        var creada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { SinRequisicionPrevia = true, MotivoSinRequisicion = "Prueba matriz de nivel" });
        creada.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
    }

    // Crea la OC con una línea y la envía a autorización con la matriz de nivel real (evaluador),
    // directo en dominio como hacen las pruebas P2 (sin adjuntos de cotización).
    private async Task<Guid> CrearOcConLineaEnAutorizacionAsync(HttpClient client)
    {
        var id = await CrearOcBorradorAsync(client);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var evaluador = scope.ServiceProvider.GetRequiredService<EvaluadorNivelOc>();

        var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == id);
        oc.AgregarLineaManual(Guid.NewGuid(), Guid.Parse("00000007-0001-0000-0000-000000000001"), 10, "PZA", 100, Guid.NewGuid(), centroCostoId: CentroCostoSeedId);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow, await evaluador.RequiereNivel2Async(oc));
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SembrarUmbralGeneralAsync(decimal? umbralMonto, decimal? umbralMenor)
    {
        if (umbralMonto is null)
        {
            return;
        }

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        db.UmbralesAprobacionDepartamento.Add(new UmbralAprobacionDepartamento(
            empresaId: EmpresaInicialId,
            departamentoId: EvaluadorNivelOc.UmbralGeneralId,
            vigenteDesde: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            umbralMonto: umbralMonto.Value,
            moneda: "MXN",
            vigenteHasta: null,
            umbralMenorCuantia: umbralMenor));
        await db.SaveChangesAsync();
    }

    private async Task LimpiarUmbralGeneralAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var filas = await db.UmbralesAprobacionDepartamento
            .Where(u => u.EmpresaId == EmpresaInicialId && u.DepartamentoId == EvaluadorNivelOc.UmbralGeneralId
                // Conserva la fila general del fixture de tools/validate-integration-isolated.sh.
                && u.VigenteDesde != new DateOnly(2020, 1, 1))
            .ToListAsync();
        db.UmbralesAprobacionDepartamento.RemoveRange(filas);
        await db.SaveChangesAsync();
    }
}
