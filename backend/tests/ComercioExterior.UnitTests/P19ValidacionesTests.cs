using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;
public sealed class P19ValidacionesTests
{
    [Theory]
    [InlineData(EstadoPeriodo.Cerrado, true)]
    [InlineData(EstadoPeriodo.NoAbierto, false)]
    public async Task Rechaza_periodo_sin_movimientos_con_mes_en_espanol(EstadoPeriodo estado, bool existe)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ValidacionesFinancieras.PeriodoAbiertoAsync(new Periodo(estado, existe), new(2026, 10, 1), default));
        Assert.Equal("El periodo contable de octubre 2026 está cerrado", error.Message);
    }
    [Fact]
    public async Task Admite_periodo_abierto() => await ValidacionesFinancieras.PeriodoAbiertoAsync(new Periodo(EstadoPeriodo.Abierto, true), new(2026, 10, 1), default);
    [Fact]
    public void Factura_obligatoria_con_excepcion_solo_para_impuestos_peca()
    {
        var concepto = new ConceptoGasto(Guid.NewGuid(), "FLETE", "Flete");
        var g = new DatosGasto(concepto.Id, Guid.NewGuid(), "UUID", "MXN", 100, new(2026, 10, 1));
        Assert.Throws<BusinessRuleException>(() => ValidacionesFinancieras.FacturaRequerida(g, concepto));
        ValidacionesFinancieras.FacturaRequerida(g with { FacturaProveedorId = Guid.NewGuid() }, concepto);
        Assert.Throws<BusinessRuleException>(() => ValidacionesFinancieras.FacturaRequerida(g with { ImpuestoPeca = true, Pedimento = "PED" }, concepto));
        ValidacionesFinancieras.FacturaRequerida(g with { ImpuestoPeca = true, Pedimento = "PED" }, new(concepto.EmpresaId, "IGI", "Arancel"));
    }
    private sealed class Periodo(EstadoPeriodo estado, bool existe) : IPeriodoContableConsultaPort
    {
        public Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct) => ConsultarAsync(fecha.Year, fecha.Month, ct);
        public Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct) => Task.FromResult(new EstadoPeriodoContable(anio, numero, estado, existe));
    }
}
