using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.Reportes;
using Millet.Contabilidad.Domain;
using H = Millet.Contabilidad.Application.Reportes.IntegracionHandler;

namespace Millet.Contabilidad.UnitTests;

/// <summary>F1-CON-08 · K10.10 (R24): integraciones del auditor por cuenta y tercero desde la contabilidad.</summary>
public sealed class IntegracionCuentasTests
{
    private const string Balance = "Estado de Posicion Financiera (Balance)";
    private const string Resultado = "Estado de Pérdidas y Ganancias (Resultado)";

    private static CuentaContable Rubro(string codigo, string grupo) =>
        new(Guid.NewGuid(), codigo, "Rubro " + codigo, null, 1, null, TipoCuenta.Titulo, CuentaControl.Ninguna, null, grupo, ClaseCuenta.Rubro);

    private static CuentaContable Mayor(string codigo, CuentaContable rubro, string grupo)
    {
        var c = new CuentaContable(Guid.NewGuid(), codigo, "Mayor " + codigo, null, 1, NaturalezaCuenta.Deudora, TipoCuenta.Titulo,
            CuentaControl.Ninguna, null, grupo);
        c.AsignarRubro(rubro.Id);
        return c;
    }

    private static CuentaContable Hoja(string codigo, CuentaContable padre, CuentaControl control = CuentaControl.Ninguna) =>
        new(Guid.NewGuid(), codigo, "Cuenta " + codigo, padre.Id, 2, NaturalezaCuenta.Deudora, TipoCuenta.Afectable, control, null,
            padre.GrupoReporte);

    private static readonly CuentaContable RPasivo = Rubro("200.00.00.00", Balance);
    private static readonly CuentaContable RIngresos = Rubro("400.00.00.00", Resultado);
    private static readonly CuentaContable RGastos = Rubro("600.00.00.00", Resultado);
    private static readonly CuentaContable MProveedores = Mayor("201.00.00.00", RPasivo, Balance);
    private static readonly CuentaContable Proveedores = Hoja("201.01.00.00", MProveedores, CuentaControl.Proveedores);
    private static readonly CuentaContable MVentas = Mayor("401.00.00.00", RIngresos, Resultado);
    private static readonly CuentaContable Ventas = Hoja("401.01.00.00", MVentas);
    private static readonly CuentaContable MGastos = Mayor("601.00.00.00", RGastos, Resultado);
    private static readonly CuentaContable Papeleria = Hoja("601.05.00.00", MGastos);
    private static readonly CuentaContable[] Catalogo = [RPasivo, RIngresos, RGastos, MProveedores, Proveedores, MVentas, Ventas, MGastos, Papeleria];

    private static readonly Guid ProvA = Guid.NewGuid(), Cli1 = Guid.NewGuid();
    private static readonly Dictionary<Guid, AuxiliarContable> Proveedores_ = new() { [ProvA] = new(ProvA, "P-001", "Papelera del Sureste", true) };
    private static readonly Dictionary<Guid, AuxiliarContable> Clientes = new() { [Cli1] = new(Cli1, "C-001", "Vidrios del Golfo", true) };

    [Fact]
    public void Las_cuentas_de_cada_integracion_salen_del_rubro_o_de_la_cuenta_colectiva()
    {
        H.Cuentas(IntegracionContable.Gastos, Catalogo).Keys.Should().BeEquivalentTo([Papeleria.Id, MGastos.Id]);
        H.Cuentas(IntegracionContable.Ingresos, Catalogo).Keys.Should().BeEquivalentTo([Ventas.Id, MVentas.Id]);
        H.Cuentas(IntegracionContable.Compras, Catalogo).Keys.Should().BeEquivalentTo([Proveedores.Id]);
        H.Cuentas(IntegracionContable.Gastos, Catalogo)[Papeleria.Id].Mayor.Codigo.Should().Be("601.00.00.00");
    }

    [Fact]
    public void Un_gasto_sin_proveedor_en_la_partida_toma_el_proveedor_de_su_poliza()
    {
        // Factura de papelería: la partida de gasto no trae proveedor; la de proveedores sí (misma póliza).
        var poliza1 = Guid.NewGuid();
        var poliza2 = Guid.NewGuid();
        var agregados = H.Combinar([],
        [
            (Papeleria.Id, poliza1, null, ProvA, 1_000m, 0m),
            (Papeleria.Id, poliza2, null, ProvA, 500m, 0m),
            (Papeleria.Id, Guid.NewGuid(), null, null, 80m, 0m),
        ]);
        var filas = H.Armar(IntegracionContable.Gastos, agregados, H.Cuentas(IntegracionContable.Gastos, Catalogo), Clientes, Proveedores_);

        filas.Should().HaveCount(2);
        var prov = filas[0];
        (prov.TerceroNombre, prov.TerceroClave, prov.TerceroTipo, prov.Importe, prov.Polizas)
            .Should().Be(("Papelera del Sureste", "P-001", "Proveedor", 1_500m, 2));
        filas[1].TerceroNombre.Should().Be("Sin tercero");
        H.Totales(filas).Should().Be(new TotalesIntegracion(1_580m, 0m, 1_580m, 80m, 1, 1));
    }

    [Fact]
    public void Ingresos_en_positivo_cuando_son_acreedores_y_se_juntan_tercero_propio_y_de_poliza()
    {
        var propios = new[] { new H.Agregado(Ventas.Id, Cli1, null, 100m, 5_000m, 3) };
        var agregados = H.Combinar(propios, [(Ventas.Id, Guid.NewGuid(), Cli1, null, 0m, 1_000m)]);
        var fila = H.Armar(IntegracionContable.Ingresos, agregados, H.Cuentas(IntegracionContable.Ingresos, Catalogo), Clientes, Proveedores_)
            .Single();

        (fila.TerceroNombre, fila.Cargos, fila.Abonos, fila.Importe, fila.Polizas).Should().Be(("Vidrios del Golfo", 100m, 6_000m, 5_900m, 4));
    }

    [Fact]
    public void Compras_son_abonos_menos_cargos_a_proveedores()
    {
        var agregados = new List<H.Agregado> { new(Proveedores.Id, null, ProvA, 400m, 2_320m, 3) };
        var fila = H.Armar(IntegracionContable.Compras, agregados, H.Cuentas(IntegracionContable.Compras, Catalogo), Clientes, Proveedores_)
            .Single();
        fila.Importe.Should().Be(1_920m);
        fila.MayorCodigo.Should().Be("201.00.00.00");
    }

    [Fact]
    public void Filas_sin_movimiento_no_salen() =>
        H.Armar(IntegracionContable.Gastos, [new H.Agregado(Papeleria.Id, null, ProvA, 0m, 0m, 1)],
            H.Cuentas(IntegracionContable.Gastos, Catalogo), Clientes, Proveedores_).Should().BeEmpty();

    [Theory]
    [InlineData(null, null, null, "tipo")]
    [InlineData("Bancos", null, null, "tipo")]
    [InlineData("3", null, null, "tipo")]
    [InlineData("Gastos", "dos", null, "anio")]
    [InlineData("Gastos", null, "MID", "sucursalId")]
    public void Un_filtro_mal_formado_responde_400_en_espanol(string? tipo, string? anio, string? sucursal, string campo)
    {
        var error = FluentActions.Invoking(() => FiltrosIntegracion.Leer(tipo, anio, null, null, sucursal))
            .Should().Throw<Millet.SharedKernel.Application.Exceptions.ValidationException>().Which.Errors.Should().ContainSingle().Subject;
        error.Field.Should().Be(campo);
        error.Message.Should().NotContain("Failed").And.NotContain("must");
    }

    [Fact]
    public void Los_filtros_toman_del_periodo_1_al_mes_de_hoy()
    {
        var q = FiltrosIntegracion.Leer("compras", null, null, null, null, new DateOnly(2026, 11, 7));
        (q.Tipo, q.Anio, q.Desde, q.Hasta).Should().Be((IntegracionContable.Compras, 2026, 1, 11));
    }

    [Fact]
    public void El_validador_rechaza_un_rango_invertido_en_espanol() =>
        new IntegracionValidator().Validate(new IntegracionQuery(IntegracionContable.Gastos, 2026, 11, 3)).Errors
            .Should().ContainSingle(e => e.ErrorMessage == "El periodo final no puede ser anterior al inicial.");
}
