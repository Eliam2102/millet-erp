using FluentAssertions;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.SaldosIniciales;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>M-166 · validación de la balanza de apertura, póliza de apertura y marca de la apertura (huella).</summary>
public sealed class AperturaSaldosInicialesTests
{
    private static readonly CuentaApertura Bancos = new(Guid.NewGuid(), "102.01.01.00", "Bancos nacionales", true, TipoCuenta.Afectable, CuentaControl.Ninguna);
    private static readonly CuentaApertura Clientes = new(Guid.NewGuid(), "105.01.00.00", "CLIENTES NACIONALES", true, TipoCuenta.Afectable, CuentaControl.Clientes);
    private static readonly CuentaApertura Proveedores = new(Guid.NewGuid(), "201.01.00.00", "Proveedores nacionales", true, TipoCuenta.Afectable, CuentaControl.Proveedores);
    private static readonly CuentaApertura Capital = new(Guid.NewGuid(), "301.01.00.00", "Capital social", true, TipoCuenta.Afectable, CuentaControl.Ninguna);
    private static readonly CuentaApertura Titulo = new(Guid.NewGuid(), "105.00.00.00", "Clientes", true, TipoCuenta.Titulo, CuentaControl.Ninguna);
    private static readonly CuentaApertura Inactiva = new(Guid.NewGuid(), "102.09.00.00", "Banco cerrado", false, TipoCuenta.Afectable, CuentaControl.Ninguna);

    private static readonly IReadOnlyDictionary<string, CuentaApertura> Catalogo =
        new[] { Bancos, Clientes, Proveedores, Capital, Titulo, Inactiva }.ToDictionary(c => c.Codigo);

    private static readonly SucursalContable Merida = new(Guid.NewGuid(), "MID", "Mérida", true);
    private static readonly IReadOnlyDictionary<string, SucursalContable> Sucursales = new Dictionary<string, SucursalContable> { ["MID"] = Merida };

    private static readonly Dictionary<(TipoAuxiliar, string), AuxiliarContable> Auxiliares = new()
    {
        [(TipoAuxiliar.Cliente, "C001")] = new(Guid.NewGuid(), "C001", "Cliente uno", true),
        [(TipoAuxiliar.Cliente, "C002")] = new(Guid.NewGuid(), "C002", "Cliente dos", true),
        [(TipoAuxiliar.Cliente, "C003")] = new(Guid.NewGuid(), "C003", "Cliente tres", true),
        [(TipoAuxiliar.Proveedor, "P001")] = new(Guid.NewGuid(), "P001", "Proveedor uno", true),
        [(TipoAuxiliar.Banco, "0123456789")] = new(Guid.NewGuid(), "0123456789", "Banco MXN", true),
    };

    private static AuxiliarContable? Buscar(TipoAuxiliar t, string clave) => Auxiliares.GetValueOrDefault((t, clave.ToUpperInvariant()));

    private static ReglasBalanzaApertura.Resultado Revisar(params FilaBalanzaApertura[] filas) =>
        ReglasBalanzaApertura.Revisar(filas, Catalogo, Buscar, Sucursales, "MID");

    /// <summary>Balanza cuadrada: bancos 1,000,000; clientes 850,000 con tres clientes; proveedores 300,000; capital 1,550,000.</summary>
    private static FilaBalanzaApertura[] Cuadrada() =>
    [
        new(2, "102.01.01.00", 1_000_000m, 0m),
        new(3, "105.01.00.00", 850_000m, 0m),
        new(4, "105.01.00.00", 500_000m, 0m, "Cliente", "C001"),
        new(5, "105.01.00.00", 300_000m, 0m, "Cliente", "C002"),
        new(6, "105.01.00.00", 50_000m, 0m, "cliente", "c003"),
        new(7, "201.01.00.00", 0m, 300_000m, "Proveedor", "P001"),
        new(8, "301.01.00.00", 0m, 1_550_000m),
    ];

    [Fact]
    public void Una_balanza_cuadrada_da_un_movimiento_por_cuenta_y_uno_por_auxiliar_en_las_cuentas_de_control()
    {
        var r = Revisar(Cuadrada());

        r.Errores.Should().BeEmpty();
        r.TotalCargos.Should().Be(1_850_000m);
        r.TotalAbonos.Should().Be(1_850_000m);
        r.Partidas.Select(p => p.Fila).Should().Equal(2, 4, 5, 6, 7, 8);
        r.Auxiliares.Should().Be(4);
        r.Cuentas.Should().Be(4);
        var clientes = r.Partidas.Where(p => p.Partida.CuentaId == Clientes.Id).Select(p => p.Partida).ToList();
        clientes.Should().HaveCount(3).And.OnlyContain(p => p.ClienteId != null && p.Origen == OrigenMovimiento.AuxiliarCxC && p.SucursalId == Merida.Id);
        var proveedor = r.Partidas.Single(p => p.Fila == 7).Partida;
        (proveedor.ProveedorId, proveedor.Abono, proveedor.Origen)
            .Should().Be((Auxiliares[(TipoAuxiliar.Proveedor, "P001")].Id, 300_000m, OrigenMovimiento.AuxiliarCxP));
        r.Partidas.Single(p => p.Fila == 2).Partida.Origen.Should().Be(OrigenMovimiento.Automatica);
    }

    [Fact]
    public void CA1_la_balanza_que_no_cuadra_se_rechaza_con_la_diferencia_exacta()
    {
        var r = Revisar(new(2, "102.01.01.00", 12_450_300m, 0m), new(3, "301.01.00.00", 0m, 12_450_000m));

        r.Diferencia.Should().Be(300m);
        r.Errores.Should().ContainSingle(e => e.Fila == null && e.Codigo == "AP_NO_CUADRA")
            .Which.Mensaje.Should().StartWith("No cuadra por 300.00");
    }

    [Fact]
    public void CA2_la_cuenta_inexistente_se_reporta_en_su_fila()
    {
        var r = Revisar([.. Cuadrada(), new(17, "105.09.00.00", 0m, 0m)]);

        r.Errores.Should().ContainSingle(e => e.Fila == 17 && e.Codigo == "AP_CUENTA_NO_EXISTE")
            .Which.Mensaje.Should().StartWith("Cuenta no existe en el catálogo");
    }

    [Fact]
    public void CA3_el_detalle_de_clientes_que_no_iguala_el_renglon_da_la_diferencia()
    {
        var filas = Cuadrada();
        filas[5] = filas[5] with { Cargo = 49_000m }; // 500,000 + 300,000 + 49,000 = 849,000 contra 850,000

        var r = Revisar(filas);

        r.Errores.Should().ContainSingle(e => e.Fila == 3 && e.Codigo == "AP_DETALLE_NO_IGUALA")
            .Which.Mensaje.Should().Be("El detalle de clientes no iguala el renglón (diferencia 1,000.00).");
        r.Partidas.Should().NotContain(p => p.Partida.CuentaId == Clientes.Id);
    }

    [Fact]
    public void La_cuenta_de_clientes_sin_detalle_se_rechaza()
    {
        var r = Revisar(new(2, "105.01.00.00", 100m, 0m), new(3, "301.01.00.00", 0m, 100m));

        r.Errores.Should().ContainSingle(e => e.Fila == 2 && e.Codigo == "AP_DETALLE_REQUERIDO");
    }

    [Fact]
    public void Sin_renglon_de_la_cuenta_el_detalle_cuenta_para_el_cuadre()
    {
        var r = Revisar(new(2, "105.01.00.00", 100m, 0m, "Cliente", "C001"), new(3, "301.01.00.00", 0m, 100m));

        r.Errores.Should().BeEmpty();
        r.TotalCargos.Should().Be(100m);
    }

    [Theory]
    [InlineData("105.00.00.00", 1, 0, null, null, "AP_CUENTA_ACUMULATIVA")]
    [InlineData("102.09.00.00", 1, 0, null, null, "AP_CUENTA_INACTIVA")]
    [InlineData("102.01.01.00", 1, 1, null, null, "AP_CARGO_Y_ABONO")]
    [InlineData("102.01.01.00", -1, 0, null, null, "AP_IMPORTE_NEGATIVO")]
    [InlineData("102.01.01.00", 1.001, 0, null, null, "AP_IMPORTE_DECIMALES")]
    [InlineData("105.01.00.00", 1, 0, "Cliente", "C999", "AP_AUXILIAR_NO_EXISTE")]
    [InlineData("105.01.00.00", 1, 0, "Proveedor", "P001", "AP_AUXILIAR_NO_CORRESPONDE")]
    [InlineData("301.01.00.00", 1, 0, "Cliente", "C001", "AP_AUXILIAR_EN_CUENTA_SIN_CONTROL")]
    [InlineData("105.01.00.00", 1, 0, "Empleado", "E1", "AP_TIPO_AUXILIAR_INVALIDO")]
    [InlineData("105.01.00.00", 1, 0, "Cliente", " ", "AP_AUXILIAR_REQUERIDO")]
    public void Los_errores_de_renglon_quedan_en_su_fila(string cuenta, double cargo, double abono, string? tipo, string? clave, string codigo)
    {
        var r = Revisar(new FilaBalanzaApertura(9, cuenta, (decimal)cargo, (decimal)abono, tipo, clave));

        r.Errores.Should().Contain(e => e.Fila == 9 && e.Codigo == codigo);
    }

    [Fact]
    public void La_cuenta_bancaria_va_como_detalle_de_una_cuenta_sin_control()
    {
        var r = Revisar(new(2, "102.01.01.00", 100m, 0m, "CuentaBancaria", "0123456789"), new(3, "301.01.00.00", 0m, 100m));

        r.Errores.Should().BeEmpty();
        r.Partidas.Single(p => p.Fila == 2).Partida.CuentaBancariaId.Should().Be(Auxiliares[(TipoAuxiliar.Banco, "0123456789")].Id);
    }

    [Fact]
    public void La_sucursal_del_renglon_manda_y_sin_ella_va_la_del_lote()
    {
        var sinOmision = ReglasBalanzaApertura.Revisar(
            [new(2, "102.01.01.00", 100m, 0m), new(3, "301.01.00.00", 0m, 100m, Sucursal: "XXX")], Catalogo, Buscar, Sucursales, null);

        sinOmision.Errores.Select(e => (e.Fila, e.Codigo)).Should().Equal((2, "AP_SUCURSAL_REQUERIDA"), (3, "AP_SUCURSAL_NO_EXISTE"));
    }

    [Fact]
    public void La_misma_cuenta_dos_veces_y_el_mismo_cliente_dos_veces_se_rechazan()
    {
        var r = Revisar(new(2, "102.01.01.00", 100m, 0m), new(3, "102.01.01.00", 100m, 0m),
            new(4, "105.01.00.00", 50m, 0m, "Cliente", "C001"), new(5, "105.01.00.00", 50m, 0m, "Cliente", "C001"),
            new(6, "301.01.00.00", 0m, 300m));

        r.Errores.Select(e => (e.Fila, e.Codigo)).Should().Contain([(3, "AP_CUENTA_REPETIDA"), (5, "AP_AUXILIAR_REPETIDO")]);
    }

    [Theory]
    [InlineData("2026-12-31", true)]
    [InlineData("2026-10-31", true)]
    [InlineData("2028-02-29", true)]
    [InlineData("2026-12-30", false)]
    public void La_fecha_de_corte_es_fin_de_mes(string fecha, bool esperado) =>
        ReglasBalanzaApertura.EsFinDeMes(DateOnly.Parse(fecha, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(esperado);

    [Fact]
    public void CA4_la_poliza_de_apertura_nace_autorizada_con_origen_apertura_y_quien_subio_y_autorizo()
    {
        var tipo = new TipoDocumentoContable(Guid.NewGuid(), "APE", "Apertura", false, TipoPolizaSat.Diario);
        var sucursal = Guid.NewGuid();
        DatosPartida P(Guid cuenta, decimal cargo, decimal abono, Guid? cliente = null) =>
            new(cuenta, "X", "Saldo inicial", cargo, abono, "MXN", 1m, null, sucursal, null, null, null, null, cliente, null, null);
        var subio = Guid.NewGuid();
        var autorizo = Guid.NewGuid();
        var ahora = new DateTimeOffset(2027, 1, 5, 10, 0, 0, TimeSpan.Zero);

        var p = Poliza.CrearApertura(Guid.NewGuid(), tipo, new DateOnly(2026, 12, 31), 2026, 12, "Apertura",
            new DocumentoOrigenPoliza("contabilidad", "apertura", Guid.NewGuid(), "Corte 2026-12-31", null), "apertura:x",
            [P(Bancos.Id, 100m, 0m), P(Clientes.Id, 50m, 0m, Guid.NewGuid()), P(Capital.Id, 0m, 150m)], 1, subio, "Analista", autorizo, "Contadora", ahora);

        p.Origen.Should().Be(OrigenPoliza.Apertura);
        p.Estado.Should().Be(EstadoPoliza.Autorizada);
        p.Folio.Should().Be("APE-000001");
        (p.CapturadaPorId, p.AutorizadaPorId, p.AutorizadaPor, p.AutorizadaEn).Should().Be((subio, autorizo, "Contadora", ahora));
        (p.PeriodoAnio, p.PeriodoNumero).Should().Be((2026, 12));
    }

    [Fact]
    public void La_poliza_de_apertura_que_no_cuadra_no_se_crea()
    {
        var tipo = new TipoDocumentoContable(Guid.NewGuid(), "APE", "Apertura", false, TipoPolizaSat.Diario);
        var d = new DatosPartida(Bancos.Id, "X", null, 100m, 0m, "MXN", 1m, null, Guid.NewGuid(), null, null, null, null, null, null, null);

        var crear = () => Poliza.CrearApertura(Guid.NewGuid(), tipo, new DateOnly(2026, 12, 31), 2026, 12, "Apertura",
            new DocumentoOrigenPoliza("contabilidad", "apertura", Guid.NewGuid(), null, null), "apertura:y", [d], 1,
            Guid.NewGuid(), "A", Guid.NewGuid(), "B", DateTimeOffset.UtcNow);

        crear.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void CA5_la_marca_reconoce_el_mismo_archivo_por_huella_y_corte()
    {
        var huella = new string('a', 64);
        var marca = new AperturaContable(Guid.NewGuid(), Guid.NewGuid(), huella.ToUpperInvariant(), new DateOnly(2026, 12, 31), Guid.NewGuid(),
            "APE-000001", Guid.NewGuid(), Guid.NewGuid(), "Contadora", DateTimeOffset.UtcNow);

        marca.Huella.Should().Be(huella);
        marca.EsElMismoArchivo(huella, new DateOnly(2026, 12, 31)).Should().BeTrue();
        marca.EsElMismoArchivo(new string('b', 64), new DateOnly(2026, 12, 31)).Should().BeFalse();
        marca.EsElMismoArchivo(huella, new DateOnly(2026, 11, 30)).Should().BeFalse();
        var invalida = () => AperturaContable.NormalizarHuella("abc");
        invalida.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("AP_HUELLA_INVALIDA");
    }

    [Fact]
    public async Task CA6_quien_subio_el_lote_no_puede_ser_quien_lo_autorizo()
    {
        var mismo = Guid.NewGuid();
        var handler = new AplicarSaldosInicialesHandler(null!, null!, null!, null!);

        var aplicar = () => handler.Handle(new AplicarSaldosInicialesCommand(Guid.NewGuid(), new string('a', 64), new DateOnly(2026, 12, 31),
            mismo, "Analista", mismo, "Analista", [new(2, "102.01.01.00", 1m, 0m)]), CancellationToken.None);

        (await aplicar.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be("Debe autorizar otra persona.");
    }

    [Fact]
    public void Las_validaciones_del_comando_piden_huella_sha256_y_las_dos_personas()
    {
        var v = new AplicarSaldosInicialesValidator().Validate(new AplicarSaldosInicialesCommand(Guid.Empty, "xyz", new DateOnly(2026, 12, 31),
            Guid.Empty, "", Guid.NewGuid(), "B", []));

        v.Errors.Select(e => e.PropertyName).Should().Contain(["LoteId", "Huella", "SubidoPorId", "SubidoPor", "Filas"]);
    }
}
