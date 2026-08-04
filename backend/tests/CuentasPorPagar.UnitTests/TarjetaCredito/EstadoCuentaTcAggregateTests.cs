using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

public sealed class EstadoCuentaTcAggregateTests
{
    private static EstadoCuentaTc Crear() =>
        EstadoCuentaTc.Crear(
            empresaId: Guid.NewGuid(),
            tarjetaId: Guid.NewGuid(),
            periodoDesde: new DateOnly(2026, 5, 1),
            periodoHasta: new DateOnly(2026, 5, 31),
            fechaCorte: new DateOnly(2026, 5, 15),
            fechaLimitePago: new DateOnly(2026, 5, 25));

    [Fact]
    public void Crear_nace_EnConciliacion_sin_archivo()
    {
        var ec = Crear();
        ec.Estado.Should().Be(EstadoCuentaTcStatus.EnConciliacion);
        ec.ArchivoBancoBlobRef.Should().BeNull();
        ec.ArchivoBancoHash.Should().BeNull();
        ec.Lineas.Should().BeEmpty();
    }

    [Fact]
    public void Crear_rechaza_periodo_invertido()
    {
        var act = () => EstadoCuentaTc.Crear(
            empresaId: Guid.NewGuid(),
            tarjetaId: Guid.NewGuid(),
            periodoDesde: new DateOnly(2026, 5, 31),
            periodoHasta: new DateOnly(2026, 5, 1),
            fechaCorte: new DateOnly(2026, 5, 15),
            fechaLimitePago: new DateOnly(2026, 5, 25));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_PERIODO_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_fecha_limite_antes_de_corte()
    {
        var act = () => EstadoCuentaTc.Crear(
            empresaId: Guid.NewGuid(),
            tarjetaId: Guid.NewGuid(),
            periodoDesde: new DateOnly(2026, 5, 1),
            periodoHasta: new DateOnly(2026, 5, 31),
            fechaCorte: new DateOnly(2026, 5, 25),
            fechaLimitePago: new DateOnly(2026, 5, 15));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_FECHA_LIMITE_INVALIDA");
    }

    [Fact]
    public void RegistrarArchivoBanco_persiste_hash_y_blob()
    {
        var ec = Crear();
        var hash = new string('a', 64);

        ec.RegistrarArchivoBanco(
            blobRef: "estados-cuenta-tc/foo.xlsx",
            sha256Hex: hash,
            cargadoBy: Guid.NewGuid(),
            perfilParserUsado: "AMEX_MX",
            totalDeclaradoMxn: 12500m,
            ahora: DateTimeOffset.UtcNow);

        ec.ArchivoBancoBlobRef.Should().Be("estados-cuenta-tc/foo.xlsx");
        ec.ArchivoBancoHash.Should().Be(hash);
        ec.PerfilParserUsado.Should().Be("AMEX_MX");
        ec.TotalBancoMxn.Should().Be(12500m);
    }

    [Fact]
    public void RegistrarArchivoBanco_hash_invalido_rechaza()
    {
        var ec = Crear();
        var act = () => ec.RegistrarArchivoBanco(
            blobRef: "foo", sha256Hex: "tooshort",
            cargadoBy: Guid.NewGuid(), perfilParserUsado: "AMEX_MX",
            totalDeclaradoMxn: null, ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_HASH_INVALIDO");
    }

    [Fact]
    public void AgregarLinea_acumula_y_posiciona()
    {
        var ec = Crear();
        var l1 = ec.AgregarLinea(1, new DateOnly(2026, 5, 5), 500m, "MXN", 500m, "PEMEX", null, "Compra");
        var l2 = ec.AgregarLinea(2, new DateOnly(2026, 5, 7), 1200m, "MXN", 1200m, "AMAZON.COM", null, "Compra");

        ec.Lineas.Should().HaveCount(2);
        l1.PosicionArchivo.Should().Be(1);
        l2.MerchantNormalizado.Should().Be("AMAZON.COM");
    }

    [Fact]
    public void ActualizarTotalConciliado_calcula_diferencia()
    {
        var ec = Crear();
        ec.RegistrarArchivoBanco(
            "foo", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 1700m, ahora: DateTimeOffset.UtcNow);

        ec.ActualizarTotalConciliado(1500m);
        ec.TotalConciliadoMxn.Should().Be(1500m);
        ec.DiferenciaMxn.Should().Be(200m); // 1700 - 1500
    }
}
