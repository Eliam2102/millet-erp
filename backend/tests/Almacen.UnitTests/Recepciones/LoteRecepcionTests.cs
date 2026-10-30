using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>F1-MP-01 S4 (Eliam E3): el lote del proveedor se normaliza y es obligatorio solo para artículos ligados a A+W.</summary>
public sealed class LoteRecepcionTests
{
    private static readonly VarianteVidrioLectura Variante = new(Guid.NewGuid(), "2440x3660", 2440m, 3660m, 6m);

    private static LineaMovimiento Linea(string? lote) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 10m, "M2", 5m, ubicacionId: Guid.NewGuid(), lote: lote);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  L-2610-01 ", "L-2610-01")]
    public void Linea_y_saldo_normalizan_el_lote(string? entrada, string? esperado)
    {
        Linea(entrada).Lote.Should().Be(esperado);
        new SaldoInventario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, 1m, lote: entrada).Lote.Should().Be(esperado);
    }

    [Fact]
    public void Linea_rechaza_lote_de_mas_de_50_caracteres() =>
        Assert.Equal("LINEA_MOV_LOTE_LARGO", Assert.Throws<BusinessRuleException>(() => Linea(new string('L', 51))).Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Articulo_ligado_a_AW_sin_lote_se_rechaza(string? lote)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => RecepcionVariante.ExigirLote(Variante, lote));
        ex.Code.Should().Be("LOTE_REQUERIDO");
        ex.Message.Should().Be("Captura el lote del proveedor.");
    }

    [Fact]
    public void Articulo_ligado_a_AW_con_lote_lo_devuelve_recortado() =>
        RecepcionVariante.ExigirLote(Variante, " L-1 ").Should().Be("L-1");

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("L-9", "L-9")]
    public void Articulo_sin_liga_a_AW_acepta_lote_opcional(string? lote, string? esperado) =>
        RecepcionVariante.ExigirLote(null, lote).Should().Be(esperado);

    [Fact]
    public void Validator_rechaza_lote_de_mas_de_50_caracteres()
    {
        var linea = new RegistrarRecepcionLineaInput(Guid.NewGuid(), Guid.NewGuid(), 1m, null, null, Guid.NewGuid(), Lote: new string('L', 51));
        var cmd = new RegistrarRecepcionConFacturaCommand(Guid.NewGuid(), new DateOnly(2026, 10, 26), Guid.NewGuid(), null, null, [linea]);
        var result = new RegistrarRecepcionConFacturaValidator().Validate(cmd);
        result.Errors.Should().Contain(e => e.ErrorMessage == "El lote no puede exceder 50 caracteres.");
    }
}
