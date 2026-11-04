using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class ListarCfdisEstatusSatTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FechaCancel = new(2026, 5, 20, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Listar_proyecta_estatus_SAT_y_fecha_de_cancelacion()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"listar-estatus-sat-{Guid.NewGuid():N}").Options;
        await using var db = new CuentasPorPagarDbContext(options, new BypassedEmpresaContext(),
            new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        var empresa = Guid.NewGuid();
        db.AddRange(
            Nuevo(empresa, "VIGENTE", EstatusSatCfdi.Vigente, null),
            Nuevo(empresa, "CANCELADO", EstatusSatCfdi.Cancelado, FechaCancel),
            Nuevo(empresa, "SINESTATUS", null, null));
        await db.SaveChangesAsync();

        var resultado = await new ListarCfdisHandler(db).Handle(new ListarCfdisQuery(), CancellationToken.None);

        var porFolio = resultado.Items.ToDictionary(i => i.Folio!);
        porFolio["VIGENTE"].EstatusSat.Should().Be(EstatusSatCfdi.Vigente);
        porFolio["VIGENTE"].FechaCancelacionSat.Should().BeNull();
        porFolio["CANCELADO"].EstatusSat.Should().Be(EstatusSatCfdi.Cancelado);
        porFolio["CANCELADO"].FechaCancelacionSat.Should().Be(FechaCancel);
        porFolio["SINESTATUS"].EstatusSat.Should().BeNull();
        porFolio["SINESTATUS"].FechaCancelacionSat.Should().BeNull();
    }

    private static CfdiRecibido Nuevo(Guid empresa, string folio, EstatusSatCfdi? estatus, DateTimeOffset? fecha) =>
        CfdiRecibido.Ingresar(empresa, UuidCfdi.Parse(Guid.NewGuid().ToString()),
            RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"), TipoCfdi.Ingreso,
            folio, null, Ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, Ahora,
            "blob-prueba", null, "hash-prueba", estatusSat: estatus, fechaCancelacionSat: fecha);

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
