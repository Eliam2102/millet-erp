using FluentAssertions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class SantanderExtractoParserTests
{
    private readonly SantanderExtractoParser _parser = new();

    [Fact]
    public void PerfilBanco_debe_ser_SANTANDER()
    {
        _parser.PerfilBanco.Should().Be("SANTANDER");
    }

    [Fact]
    public void Parse_extracto_santander_getnet_con_fechas_compactas_y_signos_cargo_abono()
    {
        string csv =
            "FECHA,CONCEPTO,REFERENCIA,CARGO/ABONO,SALDO\n" +
            "01102026,DEPOSITO TERMINAL GETNET SINTETICO,AUT123456,+15420.50,115420.50\n" +
            "02102026,COMISION GETNET MENSUAL SINTETICA,COM789012,-450.00,114970.50\n" +
            "03102026,VENTA TARJETA SUCURSAL MERIDA,LOTE8899,+25000.00,139970.50\n";

        var res = _parser.Parse(csv);

        res.TotalLineas.Should().Be(3);
        res.SaldoInicial.Should().Be(115420.50m);
        res.SaldoFinal.Should().Be(139970.50m);

        var linea1 = res.Lineas[0];
        linea1.NumeroLinea.Should().Be(1);
        linea1.Fecha.Should().Be(new DateOnly(2026, 10, 1));
        linea1.Descripcion.Should().Be("DEPOSITO TERMINAL GETNET SINTETICO");
        linea1.Referencia.Should().Be("AUT123456");
        linea1.Cargo.Should().BeNull();
        linea1.Abono.Should().Be(15420.50m);
        linea1.Saldo.Should().Be(115420.50m);
        linea1.LineaHash.Should().NotBeNullOrWhiteSpace();

        var linea2 = res.Lineas[1];
        linea2.NumeroLinea.Should().Be(2);
        linea2.Fecha.Should().Be(new DateOnly(2026, 10, 2));
        linea2.Descripcion.Should().Be("COMISION GETNET MENSUAL SINTETICA");
        linea2.Referencia.Should().Be("COM789012");
        linea2.Cargo.Should().Be(450.00m);
        linea2.Abono.Should().BeNull();
        linea2.Saldo.Should().Be(114970.50m);

        var linea3 = res.Lineas[2];
        linea3.NumeroLinea.Should().Be(3);
        linea3.Fecha.Should().Be(new DateOnly(2026, 10, 3));
        linea3.Cargo.Should().BeNull();
        linea3.Abono.Should().Be(25000.00m);
        linea3.Saldo.Should().Be(139970.50m);
    }

    [Fact]
    public void Parse_mismo_extracto_genera_mismo_hash_para_idempotencia()
    {
        string csv =
            "FECHA,CONCEPTO,REFERENCIA,CARGO/ABONO,SALDO\n" +
            "15102026,LIQUIDACION TERMINAL,OP-9988,+5000.00,55000.00\n";

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
        string csv = "FECHA,CONCEPTO,REFERENCIA,CARGO/ABONO,SALDO\n";
        var act = () => _parser.Parse(csv);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EXTRACTO_SIN_MOVIMIENTOS");
    }

    [Fact]
    public void Parse_con_delimitador_punto_y_coma_y_espacios_en_signos()
    {
        string csv =
            "FECHA;DESCRIPCION;REFERENCIA;CARGO/ABONO;SALDO\r\n" +
            "20102026;COBRO GETNET;REF-01;+ $1,200.50;$10,200.50\r\n" +
            "21102026;COMISION;COM-02;- $50.00;$10,150.50\r\n";

        var res = _parser.Parse(csv);
        res.TotalLineas.Should().Be(2);
        res.Lineas[0].Abono.Should().Be(1200.50m);
        res.Lineas[0].Cargo.Should().BeNull();
        res.Lineas[1].Cargo.Should().Be(50.00m);
        res.Lineas[1].Abono.Should().BeNull();
    }

    [Fact]
    public void Parse_extracto_con_columnas_separadas_cargo_y_abono()
    {
        string csv =
            "FECHA,CONCEPTO,REFERENCIA,CARGO,ABONO,SALDO\n" +
            "25102026,DEPOSITO,REF01,,3000.00,53000.00\n" +
            "26102026,RETIRO,REF02,1000.00,,52000.00\n";

        var res = _parser.Parse(csv);
        res.TotalLineas.Should().Be(2);
        res.Lineas[0].Abono.Should().Be(3000.00m);
        res.Lineas[1].Cargo.Should().Be(1000.00m);
    }
}
