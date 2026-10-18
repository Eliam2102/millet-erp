using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.IntegrationTests.Matriz;

/// <summary>
/// Tests integration de <see cref="EvaluadorMultidimensional"/> (F9-PR2,
/// §3.bis.2). Construye <see cref="Requisicion"/>s en memoria con líneas
/// que apuntan a artículos seedeados (ART-EST/SVC/CRT/RGO) y verifica
/// que el evaluator devuelve el nivel correcto según las capas vigentes.
///
/// <para>
/// Desde la minuta Millet del 12-oct la requisición no lleva monto: el monto
/// contra el umbral N1-N2 se valida en la OC. Por eso el monto ya no sube la
/// RQ a N2 (modo por defecto <c>SoloMonto</c>, sin criterio de disponibilidad).
/// </para>
/// </summary>
public class EvaluadorMultidimensionalTests : IClassFixture<StubsWebApplicationFactory>
{
    // Empresa inicial seedeada por BootstrapSuperAdminHostedService.
    private static readonly Guid EmpresaInicial = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // Artículos seedeados por CatalogosTestSeedHostedService:
    private static readonly Guid ArtEstandar = Guid.Parse("00000005-0002-0000-0000-000000000001"); // ART-EST-01
    private static readonly Guid ArtServicio = Guid.Parse("00000005-0002-0000-0000-000000000003"); // ART-SVC-01
    private static readonly Guid ArtCritico = Guid.Parse("00000005-0002-0000-0000-000000000005"); // ART-CRT-01
    private static readonly Guid ArtRiesgo = Guid.Parse("00000005-0002-0000-0000-000000000007"); // ART-RGO-01

    private readonly StubsWebApplicationFactory _factory;

    public EvaluadorMultidimensionalTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Linea_Critico_Disparara_N1Y_N2_Independiente_Del_Monto()
    {
        // Critico → capa 3 short-circuit, no consulta umbral.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtCritico, cantidad: 1m, "PZA",
            new Money(50m, "MXN"));   // Monto bajo, no importa.

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Linea_Riesgo_Disparara_N1Y_N2()
    {
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtRiesgo, cantidad: 1m, "PZA",
            new Money(10m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Mezcla_Estandar_Y_Critico_Toma_La_Mas_Restrictiva_Y_Dispara_N1YN2()
    {
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 5m, "PZA",
            new Money(20m, "MXN"));
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtCritico, cantidad: 1m, "PZA",
            new Money(30m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.N1YN2, nivel);
    }

    [Fact]
    public async Task Estandar_Monto_Alto_Es_SoloN1_La_RQ_No_Sube_A_N2_Por_Monto()
    {
        // Monto alto sin umbral: la RQ no lleva monto, solo naturaleza → N1.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 1000m, "PZA",
            new Money(99999m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Estandar_Con_Faltante_Y_Total_Excedido_No_Sube_A_N2()
    {
        // Stub de stock: ART-EST-01 tiene 100 en almacén. Cantidad 250 → faltante 150,
        // total 2500. Con el modo por defecto (SoloMonto) la RQ queda en N1.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 250m, "PZA",
            new Money(10m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Servicio_Con_Monto_Excedido_No_Sube_A_N2_En_La_Requisicion()
    {
        // Servicio no es Critico/Riesgo y la RQ ya no evalúa monto → N1.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtServicio, cantidad: 1m, "SVC",
            new Money(5000m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Linea_Sin_Precio_Estandar_Es_SoloN1()
    {
        // Precio 0 (RQ sin precio estimado): no bloquea ni eleva la evaluación.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 10m, "PZA",
            new Money(0m, "MXN"));

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    // --- Helpers ---

    private static Requisicion BuildRequisicion(Guid deptoId)
    {
        return new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaInicial,
            folio: Folio.Parse("MID2026-000001"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: deptoId,
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: "Test eval");
    }

    private async Task<RequiereNivel> EvaluarAsync(Requisicion rq)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IRequiereNivelEvaluator>();
        return await evaluator.EvaluarAsync(rq);
    }
}
