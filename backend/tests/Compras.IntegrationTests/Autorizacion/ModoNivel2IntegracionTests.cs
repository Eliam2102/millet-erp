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
/// Integración de <see cref="ComprasSettings.ModoNivel2"/> con el evaluador real
/// (<c>EvaluadorMultidimensional</c>) vía HTTP: se cambia el modo con
/// <c>PATCH /api/v1/compras/configuracion</c>, se transmiten RQs y se firma N1;
/// el estado resultante muestra si basta N1 (<c>Autorizada</c>) o si exige N1+N2
/// (<c>EnAutorizacion</c>, pendiente de N2).
///
/// <para>
/// Umbral de prueba: <c>UmbralMonto</c> = 100 MXN sembrado para
/// <see cref="TestComprasFixtures.DeptoCompras"/> y limpiado en <c>finally</c>.
/// Precio estimado de las líneas: 15 MXN por unidad. El umbral existe para
/// demostrar que la requisición <b>ya no</b> sube a N2 por monto (la minuta
/// Millet del 12-oct traslada la validación de monto a la OC).
/// </para>
///
/// <para>
/// Matriz (en la RQ solo cuenta la disponibilidad en almacén; el monto no aplica):
/// </para>
/// <list type="bullet">
///   <item>Servicio 10 u (150 MXN, excede el umbral pero no es inventario):
///         solo N1 en los tres modos.</item>
///   <item>Inventario sin stock 1 u (con faltante de existencia):
///         N2 en SoloDisponibilidad y Ambos; solo N1 en SoloMonto.</item>
///   <item>Inventario con stock suficiente 10 u:
///         solo N1 en los tres modos.</item>
/// </list>
/// </summary>
public class ModoNivel2IntegracionTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string EndpointConfiguracion = "/api/v1/compras/configuracion";
    private const decimal UmbralMonto = 100m;

    private readonly StubsWebApplicationFactory _factory;

    public ModoNivel2IntegracionTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("SoloMonto", false, false)]
    [InlineData("SoloDisponibilidad", false, true)]
    [InlineData("Ambos", false, true)]
    public async Task ModoNivel2_DeterminaSiN1SolaBasta_O_SiExigeN2(
        string modo, bool servicioExigeN2, bool inventarioExigeN2)
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        await SembrarUmbralAsync(TestComprasFixtures.DeptoCompras, UmbralMonto);
        try
        {
            await PatchModoAsync(admin, modo);

            var servicio = await CrearTransmitirAsync(admin, new[] { (ArticuloServicio, 10m) });
            var inventario = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });
            var estandar = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 10m) });

            await FirmarN1YVerificarAsync(admin, servicio, servicioExigeN2, modo, "servicio");
            await FirmarN1YVerificarAsync(admin, inventario, inventarioExigeN2, modo, "inventario sin stock");
            await FirmarN1YVerificarAsync(admin, estandar, false, modo, "inventario con stock suficiente");
        }
        finally
        {
            await PatchModoAsync(admin, "SoloMonto");
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

    private static async Task PatchModoAsync(HttpClient client, string modo)
    {
        var resp = await client.PatchAsJsonAsync(EndpointConfiguracion, new { ModoNivel2 = modo });
        resp.EnsureSuccessStatusCode();
    }

    private async Task SembrarUmbralAsync(Guid departamentoId, decimal umbralMonto)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        db.UmbralesAprobacionDepartamento.Add(new UmbralAprobacionDepartamento(
            empresaId: EmpresaInicialId,
            departamentoId: departamentoId,
            vigenteDesde: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            umbralMonto: umbralMonto,
            moneda: "MXN"));
        await db.SaveChangesAsync();
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
