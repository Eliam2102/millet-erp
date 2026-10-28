using Millet.Almacen.Domain.Movimientos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Movimientos;

/// <summary>F1-MP-01 S2: variante y snapshot de medida van juntos o ninguno.</summary>
public sealed class LineaMovimientoVarianteTests
{
    private static LineaMovimiento Linea(Guid? variante, decimal? ancho, decimal? alto) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 10m, "M2", 5m,
            ubicacionId: Guid.NewGuid(), productoAwVarianteId: variante, anchoMm: ancho, altoMm: alto);

    [Fact]
    public void Sin_variante_conserva_los_tres_nulos()
    {
        var l = Linea(null, null, null);
        Assert.Null(l.ProductoAwVarianteId);
        Assert.Null(l.AnchoMm);
        Assert.Null(l.AltoMm);
    }

    [Fact]
    public void Con_variante_guarda_el_snapshot_de_medida()
    {
        var id = Guid.NewGuid();
        var l = Linea(id, 2440m, 3660m);
        Assert.Equal((id, 2440m, 3660m), (l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void Rechaza_variante_incompleta(bool variante, bool ancho, bool alto) =>
        Assert.Equal("LINEA_MOV_VARIANTE_INCOMPLETA", Assert.Throws<BusinessRuleException>(() =>
            Linea(variante ? Guid.NewGuid() : null, ancho ? 2440m : null, alto ? 3660m : null)).Code);

    [Fact]
    public void Rechaza_medida_no_positiva() =>
        Assert.Equal("LINEA_MOV_MEDIDA_INVALIDA",
            Assert.Throws<BusinessRuleException>(() => Linea(Guid.NewGuid(), 0m, 3660m)).Code);
}
