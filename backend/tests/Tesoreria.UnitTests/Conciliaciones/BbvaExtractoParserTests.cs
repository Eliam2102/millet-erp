using FluentAssertions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class BbvaExtractoParserTests
{
    private readonly BbvaExtractoParser _parser = new();

    [Fact]
    public void Parse_extracto_bbva_csv_estandar_extrae_lineas_correctamente()
    {
        string csv =
            "FECHA,CONCEPTO,REFERENCIA,CARGO,ABONO,SALDO\n" +
            "01/09/2026,PAGO PROVEEDOR VIDRIOS,REF1001,5000.00,,45000.00\n" +
            "02/09/2026,DEPOSITO CLIENTE MATRIZ,DEP2002,,12500.50,57500.50\n" +
            "03/09/2026,COMISION POR TRANSFERENCIA,COM3003,350.00,,57150.50\n";

        var res = _parser.Parse(csv);

        res.TotalLineas.Should().Be(3);
        res.SaldoFinal.Should().Be(57150.50m);

        var linea1 = res.Lineas[0];
        linea1.NumeroLinea.Should().Be(1);
        linea1.Fecha.Should().Be(new DateOnly(2026, 9, 1));
        linea1.Descripcion.Should().Be("PAGO PROVEEDOR VIDRIOS");
        linea1.Referencia.Should().Be("REF1001");
        linea1.Cargo.Should().Be(5000.00m);
        linea1.Abono.Should().BeNull();
        linea1.LineaHash.Should().NotBeNullOrWhiteSpace();

        var linea2 = res.Lineas[1];
        linea2.Fecha.Should().Be(new DateOnly(2026, 9, 2));
        linea2.Abono.Should().Be(12500.50m);
        linea2.Cargo.Should().BeNull();

        var linea3 = res.Lineas[2];
        linea3.Cargo.Should().Be(350.00m);
    }

    [Fact]
    public void Parse_mismo_extracto_genera_mismo_hash_para_deduplicacion_CA9_5()
    {
        string csv = "01/09/2026,PAGO PROVEEDOR,REF1001,5000.00,,45000.00";

        var res1 = _parser.Parse(csv);
        var res2 = _parser.Parse(csv);

        res1.Lineas[0].LineaHash.Should().Be(res2.Lineas[0].LineaHash);
    }

    [Fact]
    public void Parse_extracto_vacio_lanza_excepcion()
    {
        var act = () => _parser.Parse("   ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EXTRACTO_VACIO");
    }

    [Fact]
    public void Parse_extracto_sin_movimientos_validos_lanza_excepcion()
    {
        string csv = "FECHA,CONCEPTO,REFERENCIA,CARGO,ABONO,SALDO\n";
        var act = () => _parser.Parse(csv);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EXTRACTO_SIN_MOVIMIENTOS");
    }

    [Fact]
    public void Parse_con_delimitador_punto_y_coma_y_simbolos_moneda()
    {
        string csv =
            "FECHA;DESCRIPCION;REFERENCIA;CARGO;ABONO;SALDO\r\n" +
            "15/09/2026;COBRO FACTURA;F-9988;;$1,500.00;$15,000.00\r\n";

        var res = _parser.Parse(csv);
        res.TotalLineas.Should().Be(1);
        res.Lineas[0].Abono.Should().Be(1500.00m);
    }
}
