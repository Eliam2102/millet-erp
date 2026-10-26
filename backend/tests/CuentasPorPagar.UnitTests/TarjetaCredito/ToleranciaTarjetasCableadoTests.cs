using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

/// <summary>
/// G1.5: el valor configurado del puerto llega a la regla (handler, adaptador y servicio de match).
/// </summary>
public sealed class ToleranciaTarjetasCableadoTests
{
    private sealed class Puerto(decimal monto) : IToleranciaTarjetasReadPort
    {
        public Task<decimal> ObtenerMontoMxnAsync(CancellationToken cancellationToken) => Task.FromResult(monto);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Alcance();
        private sealed class Alcance : IDisposable { public void Dispose() { } }
    }

    private static Tarjeta CrearTarjeta() =>
        Tarjeta.Crear(Guid.NewGuid(), "Amex", "AMEX_MX", NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"),
            "Amex Corp", Guid.NewGuid(), Guid.NewGuid(), 100000m, "MXN", 15, 20, new DateOnly(2026, 1, 1));

    private static EstadoCuentaTc CrearEc(Guid tarjetaId, Guid empresaId) =>
        EstadoCuentaTc.Crear(empresaId, tarjetaId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31),
            new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 25));

    private static async Task<(MarcarEstadoCuentaConciliadoHandler Handler, Guid Id)> PrepararHandlerAsync(
        decimal diferencia, decimal tolerancia)
    {
        var empresa = new Empresa();
        var db = new CuentasPorPagarDbContext(
            new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            empresa, new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        var ec = CrearEc(Guid.NewGuid(), empresa.Current!.Value);
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(), "AMEX_MX", 1000m, DateTimeOffset.UtcNow);
        ec.ActualizarTotalConciliado(1000m - diferencia);
        db.EstadosCuentaTc.Add(ec);
        await db.SaveChangesAsync();
        return (new MarcarEstadoCuentaConciliadoHandler(db, new Puerto(tolerancia)), ec.Id);
    }

    [Theory]
    [InlineData(0.80, 1.00)]
    [InlineData(1.20, 2.00)]
    public async Task Handler_usa_la_tolerancia_del_puerto_y_concilia(double diferencia, double tolerancia)
    {
        var (handler, id) = await PrepararHandlerAsync((decimal)diferencia, (decimal)tolerancia);
        var r = await handler.Handle(new MarcarEstadoCuentaConciliadoCommand(id, 0), default);
        Assert.Equal(EstadoCuentaTcStatus.Conciliado, r.Estado);
    }

    [Fact]
    public async Task Handler_usa_la_tolerancia_del_puerto_y_rechaza_fuera_de_ella()
    {
        var (handler, id) = await PrepararHandlerAsync(1.20m, 1.00m);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(new MarcarEstadoCuentaConciliadoCommand(id, 0), default));
        Assert.Equal("EC_DIFERENCIA_NO_CERO", ex.Code);
    }

    [Theory]
    [InlineData(null, 1.00)]
    [InlineData("1.50", 1.50)]
    public async Task Adaptador_lee_el_parametro_o_usa_1_00(string? valor, double esperado)
    {
        var maestros = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Empresa());
        if (valor is not null)
        {
            maestros.ParametrosGlobales.Add(ParametroGlobal.CrearDeModulo(Guid.NewGuid(), ToleranciaTarjetasParametro.Clave,
                valor, TipoParametro.Numero, "cxp", "Tolerancia tarjetas"));
            await maestros.SaveChangesAsync();
        }
        var monto = await new ToleranciaTarjetasReadPortAdapter(maestros).ObtenerMontoMxnAsync(default);
        Assert.Equal((decimal)esperado, monto);
    }

    [Theory]
    [InlineData(2.00, EstadoMatchLineaBanco.Matched, 100)]
    [InlineData(1.00, EstadoMatchLineaBanco.NoConciliado, 0)]
    public async Task ConciliarAsync_aplica_la_tolerancia_del_puerto(double tolerancia, EstadoMatchLineaBanco estado, int score)
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(t.EmpresaId, t, t.TitularId, new DateOnly(2026, 5, 10),
            1000m, "MXN", null, "AMAZON", null, "Test", null);
        var ec = CrearEc(t.Id, t.EmpresaId);
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 10), 1001.20m, "MXN", 1001.20m, "AMAZON", null, "Compra");

        await new ConciliacionAutomaticaService(NullLogger<ConciliacionAutomaticaService>.Instance,
            new Puerto((decimal)tolerancia)).ConciliarAsync(ec, [mov], default);

        Assert.Equal(estado, linea.EstadoMatch);
        if (score > 0) Assert.Equal((decimal)score, linea.ScoreMatch);
    }
}
