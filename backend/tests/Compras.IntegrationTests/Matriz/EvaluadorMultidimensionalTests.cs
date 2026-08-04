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
/// que el evaluator devuelve el nivel correcto según las 3 capas.
///
/// <para>
/// Para la capa 2 (monto vs umbral) los tests siembran un
/// <see cref="UmbralAprobacionDepartamento"/> directamente vía DbContext
/// con bypass del filtro de empresa. Cleanup explícito en finally.
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
    public async Task Estandar_Sin_Umbral_Configurado_Es_SoloN1()
    {
        // Sin fila vigente en umbrales_aprobacion_departamento → fail-open
        // capa 2; solo aplica capa 1.
        var rq = BuildRequisicion(deptoId: Guid.CreateVersion7());
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 1000m, "PZA",
            new Money(99999m, "MXN"));   // Monto alto pero sin umbral.

        var nivel = await EvaluarAsync(rq);

        Assert.Equal(RequiereNivel.SoloN1, nivel);
    }

    [Fact]
    public async Task Estandar_Con_Monto_Bajo_Umbral_Es_SoloN1()
    {
        var deptoId = Guid.CreateVersion7();
        var rq = BuildRequisicion(deptoId);
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 10m, "PZA",
            new Money(50m, "MXN"));   // Total 500, umbral 1000.

        await SeedUmbralAsync(deptoId, umbralMonto: 1000m);

        try
        {
            var nivel = await EvaluarAsync(rq);
            Assert.Equal(RequiereNivel.SoloN1, nivel);
        }
        finally
        {
            await CleanupUmbralAsync(deptoId);
        }
    }

    [Fact]
    public async Task Estandar_Con_Monto_Excede_Umbral_Dispara_N1YN2()
    {
        var deptoId = Guid.CreateVersion7();
        var rq = BuildRequisicion(deptoId);
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtEstandar, cantidad: 10m, "PZA",
            new Money(200m, "MXN"));   // Total 2000, umbral 1000.

        await SeedUmbralAsync(deptoId, umbralMonto: 1000m);

        try
        {
            var nivel = await EvaluarAsync(rq);
            Assert.Equal(RequiereNivel.N1YN2, nivel);
        }
        finally
        {
            await CleanupUmbralAsync(deptoId);
        }
    }

    [Fact]
    public async Task Servicio_Con_Monto_Excede_Umbral_Dispara_N1YN2()
    {
        // Servicio NO dispara capa 3 (no es Critico/Riesgo); aplica capa 2.
        var deptoId = Guid.CreateVersion7();
        var rq = BuildRequisicion(deptoId);
        rq.AgregarLinea(
            Guid.CreateVersion7(), ArtServicio, cantidad: 1m, "SVC",
            new Money(5000m, "MXN"));

        await SeedUmbralAsync(deptoId, umbralMonto: 1000m);

        try
        {
            var nivel = await EvaluarAsync(rq);
            Assert.Equal(RequiereNivel.N1YN2, nivel);
        }
        finally
        {
            await CleanupUmbralAsync(deptoId);
        }
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

    private async Task SeedUmbralAsync(Guid deptoId, decimal umbralMonto)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        db.UmbralesAprobacionDepartamento.Add(new UmbralAprobacionDepartamento(
            empresaId: EmpresaInicial,
            departamentoId: deptoId,
            vigenteDesde: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            umbralMonto: umbralMonto,
            moneda: "MXN"));
        await db.SaveChangesAsync();
    }

    private async Task CleanupUmbralAsync(Guid deptoId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var rows = await db.UmbralesAprobacionDepartamento
            .Where(u => u.EmpresaId == EmpresaInicial && u.DepartamentoId == deptoId)
            .ToListAsync();
        db.UmbralesAprobacionDepartamento.RemoveRange(rows);
        await db.SaveChangesAsync();
    }
}
