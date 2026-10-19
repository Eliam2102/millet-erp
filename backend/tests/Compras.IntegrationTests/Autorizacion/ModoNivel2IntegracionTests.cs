using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;
using static Millet.Compras.IntegrationTests.Autorizacion.AutorizacionTestSeeding;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// Integración del modo de nivel 2 por departamento (<see cref="UmbralAprobacionDepartamento.ModoNivel2"/>)
/// con el evaluador real (<c>EvaluadorMultidimensional</c>) vía HTTP: el modo se fija con
/// <c>PUT /api/v1/compras/umbrales-aprobacion</c>, se transmiten RQs y se firma N1;
/// el estado resultante muestra si basta N1 (<c>Autorizada</c>) o si exige N1+N2
/// (<c>EnAutorizacion</c>, pendiente de N2).
///
/// <para>
/// Umbral de prueba: <c>UmbralMonto</c> = 100 MXN para
/// <see cref="TestComprasFixtures.DeptoCompras"/>, limpiado en <c>finally</c>.
/// Precio estimado de las líneas: 15 MXN por unidad.
/// </para>
///
/// <list type="bullet">
///   <item>Servicio 10 u (150 MXN, excede el umbral; no es inventario):
///         N2 en SoloMonto y Ambos; N1 en SoloDisponibilidad.</item>
///   <item>Inventario sin stock 1 u (15 MXN, faltante de existencia):
///         N2 en SoloDisponibilidad y Ambos; N1 en SoloMonto.</item>
///   <item>Inventario con stock 10 u (150 MXN, existencia suficiente):
///         N2 en SoloMonto y Ambos; N1 en SoloDisponibilidad.</item>
/// </list>
/// </summary>
public class ModoNivel2IntegracionTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string EndpointUmbrales = "/api/v1/compras/umbrales-aprobacion";
    private const decimal UmbralMonto = 100m;

    private readonly StubsWebApplicationFactory _factory;

    public ModoNivel2IntegracionTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("SoloMonto", true, false, true)]
    [InlineData("SoloDisponibilidad", false, true, false)]
    [InlineData("Ambos", true, true, true)]
    public async Task ModoNivel2_DeterminaSiN1SolaBasta_O_SiExigeN2(
        string modo, bool servicioExigeN2, bool sinStockExigeN2, bool estandarExigeN2)
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        try
        {
            await PutUmbralAsync(admin, modo, montoMinimo: null);

            var servicio = await CrearTransmitirAsync(admin, new[] { (ArticuloServicio, 10m) });
            var sinStock = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });
            var estandar = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 10m) });

            await FirmarN1YVerificarAsync(admin, servicio, servicioExigeN2, modo, "servicio");
            await FirmarN1YVerificarAsync(admin, sinStock, sinStockExigeN2, modo, "inventario sin stock");
            await FirmarN1YVerificarAsync(admin, estandar, estandarExigeN2, modo, "inventario con stock suficiente");
        }
        finally
        {
            await LimpiarUmbralAsync(TestComprasFixtures.DeptoCompras);
        }
    }

    [Fact]
    public async Task Minimo_Por_Disponibilidad_Evita_N2_Si_El_Importe_No_Lo_Supera()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        try
        {
            await PutUmbralAsync(admin, "SoloDisponibilidad", montoMinimo: 50m);

            var sinStock = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });

            await FirmarN1YVerificarAsync(admin, sinStock, false, "SoloDisponibilidad (mínimo 50)", "inventario sin stock, importe 15");
        }
        finally
        {
            await LimpiarUmbralAsync(TestComprasFixtures.DeptoCompras);
        }
    }

    private static async Task FirmarN1YVerificarAsync(
        HttpClient client, Guid rqId, bool exigeN2, string modo, string etiqueta)
    {
        var firma = await AutorizarAsync(client, rqId, nivel: 1);
        Assert.True(
            firma.IsSuccessStatusCode,
            $"modo {modo}, {etiqueta}: firma N1 devolvió {(int)firma.StatusCode}");

        // Con N1 basta, la RQ sale de EnAutorizacion: Autorizada, o EnSurtido si el
        // almacén ya cubre todo (stock suficiente). Con N2 pendiente se queda en EnAutorizacion.
        var estado = await EstadoAsync(client, rqId);
        var sinN2 = new[] { (int)EstadoRequisicion.Autorizada, (int)EstadoRequisicion.EnSurtido };
        var cumple = exigeN2
            ? estado == (int)EstadoRequisicion.EnAutorizacion
            : sinN2.Contains(estado);
        Assert.True(
            cumple,
            $"modo {modo}, {etiqueta}: estado {estado}, se esperaba " +
            $"{(exigeN2 ? "EnAutorizacion (pendiente N2)" : "Autorizada o EnSurtido (basta N1)")}");
    }

    private static async Task PutUmbralAsync(HttpClient client, string modo, decimal? montoMinimo)
    {
        var resp = await client.PutAsJsonAsync(EndpointUmbrales, new
        {
            DepartamentoId = TestComprasFixtures.DeptoCompras,
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto,
            Moneda = "MXN",
            ModoNivel2 = modo,
            MontoMinimoDisponibilidad = montoMinimo,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task LimpiarUmbralAsync(Guid departamentoId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var filas = await db.UmbralesAprobacionDepartamento
            .Where(u => u.EmpresaId == EmpresaInicialId && u.DepartamentoId == departamentoId)
            .ToListAsync();
        db.UmbralesAprobacionDepartamento.RemoveRange(filas);
        await db.SaveChangesAsync();
    }
}
