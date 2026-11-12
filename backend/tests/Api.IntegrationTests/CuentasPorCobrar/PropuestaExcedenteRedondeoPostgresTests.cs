using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// M-326b · regresión con PostgreSQL real: un depósito de 11,600.40 contra una factura de 11,600.00 guarda un ajuste POSITIVO de 0.40
/// (excedente de redondeo). Las pruebas unitarias usan el proveedor en memoria, que no aplica los CHECK de la base, por eso no vieron que
/// <c>ck_propuesta_aplicacion_ajuste_no_positivo</c> (ajuste &lt;= 0) lo rechazaba con 23514.
/// </summary>
public sealed class PropuestaExcedenteRedondeoPostgresTests
{
    private static string Conexion => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? throw new InvalidOperationException("Ejecuta tools/validate-integration-isolated.sh.");

    private sealed class Contexto : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private static PropuestaAplicacionPago Propuesta(Guid empresa, decimal deposito, decimal aplicado, TipoAjusteCobro? tipo = null) =>
        PropuestaAplicacionPago.Crear(empresa, Guid.NewGuid(), "DEP-M326B", deposito, "MXN", "REM-M326B",
            [("UUID-M326B", Guid.NewGuid(), aplicado, null)], 0.99m, Guid.NewGuid(), tipoAjuste: tipo,
            motivoAjuste: tipo is null or TipoAjusteCobro.Redondeo ? null : "Comisión");

    [Theory]
    [InlineData(11_600.40, 11_600.00)] // excedente de redondeo: ajuste +0.40
    [InlineData(11_599.60, 11_600.00)] // faltante de redondeo: ajuste -0.40
    [InlineData(11_600.00, 11_600.00)] // exacto
    public async Task Propuesta_con_diferencia_de_centavos_se_guarda_en_postgres(decimal deposito, decimal aplicado)
    {
        var contexto = new Contexto();
        await using var db = new CuentasPorCobrarDbContext(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseNpgsql(Conexion).UseSnakeCaseNamingConvention().Options, contexto);
        var propuesta = Propuesta(contexto.Current!.Value, deposito, aplicado);
        db.PropuestasAplicacionPago.Add(propuesta);
        try
        {
            await db.SaveChangesAsync();
            (await db.PropuestasAplicacionPago.AsNoTracking().SingleAsync(p => p.Id == propuesta.Id)).AjusteNoFiscal
                .Should().Be(deposito - aplicado);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.PropuestasAplicacionPago.Where(p => p.Id == propuesta.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Un_ajuste_positivo_que_no_es_redondeo_sigue_rechazado_por_la_base()
    {
        var contexto = new Contexto();
        await using var db = new CuentasPorCobrarDbContext(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseNpgsql(Conexion).UseSnakeCaseNamingConvention().Options, contexto);
        var propuesta = Propuesta(contexto.Current!.Value, 11_600.40m, 11_600m);
        db.PropuestasAplicacionPago.Add(propuesta);
        await db.SaveChangesAsync();
        try
        {
            // Mismo registro sin tipo de redondeo: el check debe seguir impidiendo un ajuste positivo de otro tipo.
            var act = () => db.Database.ExecuteSqlRawAsync(
                "UPDATE cuentas_por_cobrar.propuesta_aplicacion_pago SET tipo_ajuste = 1 WHERE id = {0}", propuesta.Id);
            await act.Should().ThrowAsync<Exception>();
            var excede = () => db.Database.ExecuteSqlRawAsync(
                "UPDATE cuentas_por_cobrar.propuesta_aplicacion_pago SET ajuste_no_fiscal = 1.50 WHERE id = {0}", propuesta.Id);
            await excede.Should().ThrowAsync<Exception>();
        }
        finally { await db.PropuestasAplicacionPago.Where(p => p.Id == propuesta.Id).ExecuteDeleteAsync(); }
    }
}
