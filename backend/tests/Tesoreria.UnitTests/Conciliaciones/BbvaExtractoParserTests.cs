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

    [Fact]
    public void Parse_formato_posicional_10_columnas_sin_encabezados_bbva_procesa_correctamente()
    {
        // Layout BBVA Net Cash 10 columnas sin encabezados:
        // [0: Fecha Oper, 1: Fecha Liq, 2: Codigo, 3: Concepto, 4: Referencia, 5: Cargo, 6: Abono, 7: Saldo, 8: Sucursal, 9: Moneda]
        // Importes con ceros a la izquierda
        string csv =
            "01/10/2026,01/10/2026,Y16,DEPOSITO CLIENTE MATRIZ SINTETICO,REF9876543,0000000000.00,0000031093.60,0000035919.57,2773,MXP\n" +
            "02/10/2026,02/10/2026,W02,PAGO PROVEEDOR CRISTALES SUR,FAC-11002,0000015000.00,0000000000.00,0000020919.57,2773,MXP\n";

        var res = _parser.Parse(csv);

        res.TotalLineas.Should().Be(2);
        res.SaldoInicial.Should().Be(35919.57m);
        res.SaldoFinal.Should().Be(20919.57m);

        var linea1 = res.Lineas[0];
        linea1.NumeroLinea.Should().Be(1);
        linea1.Fecha.Should().Be(new DateOnly(2026, 10, 1));
        linea1.Descripcion.Should().Be("DEPOSITO CLIENTE MATRIZ SINTETICO");
        linea1.Referencia.Should().Be("REF9876543");
        linea1.Cargo.Should().BeNull();
        linea1.Abono.Should().Be(31093.60m);
        linea1.Saldo.Should().Be(35919.57m);
        linea1.LineaHash.Should().NotBeNullOrWhiteSpace();

        var linea2 = res.Lineas[1];
        linea2.NumeroLinea.Should().Be(2);
        linea2.Fecha.Should().Be(new DateOnly(2026, 10, 2));
        linea2.Descripcion.Should().Be("PAGO PROVEEDOR CRISTALES SUR");
        linea2.Referencia.Should().Be("FAC-11002");
        linea2.Cargo.Should().Be(15000.00m);
        linea2.Abono.Should().BeNull();
        linea2.Saldo.Should().Be(20919.57m);
    }

    [Fact]
    public void Parse_importes_con_ceros_a_la_izquierda_limpia_y_parsea_decimal()
    {
        string csv =
            "05/10/2026,05/10/2026,T20,TRANSFERENCIA RECIBIDA,0154566684,000000000000,000000004825.97,000000050000.00,2773,MXP\n";

        var res = _parser.Parse(csv);

        res.TotalLineas.Should().Be(1);
        res.Lineas[0].Abono.Should().Be(4825.97m);
        res.Lineas[0].Cargo.Should().BeNull();
        res.Lineas[0].Saldo.Should().Be(50000.00m);
    }
}
