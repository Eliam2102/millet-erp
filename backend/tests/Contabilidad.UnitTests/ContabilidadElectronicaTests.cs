using System.Text;
using System.Xml.Linq;
using Millet.Contabilidad.Application.ContabilidadElectronica;
using Millet.Contabilidad.Domain;
using Xunit;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// K10.8 (F1-CON-13): XML de contabilidad electrónica 1.3 (catálogo, balanza y pólizas) validados contra los esquemas oficiales del
/// SAT embebidos, y la revisión previa de cuentas sin código agrupador (K10.8-a).
/// </summary>
public sealed class ContabilidadElectronicaTests
{
    private const string Rfc = "MIL010101AB1";

    // 100 Activo (título) → 102 Bancos (título) → 102-01 Banco nacional; 201 Proveedores (título) → 201-01 Proveedor nacional; 401 Ingresos.
    private static readonly CuentaContable Activo = Cuenta("100", null, 1, "100");
    private static readonly CuentaContable Bancos = Cuenta("102", Activo, 2, "102");
    private static readonly CuentaContable Banco = Cuenta("102-01", Bancos, 3, "102.01");
    private static readonly CuentaContable Proveedores = Cuenta("201", null, 1, "201", NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Proveedor = Cuenta("201-01", Proveedores, 2, "201.01", NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Ingresos = Cuenta("401", null, 1, "401.01", NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Rubro = new(Guid.NewGuid(), "R-1", "Rubro de reporte", null, 1, null, TipoCuenta.Afectable,
        CuentaControl.Ninguna, null, null, ClaseCuenta.Rubro);

    private static readonly CuentaContable[] Catalogo = [Activo, Bancos, Banco, Proveedores, Proveedor, Ingresos, Rubro];

    [Fact]
    public void CA10_14_el_catalogo_sale_con_codigo_agrupador_nivel_naturaleza_y_subcuenta_y_cumple_el_esquema_del_SAT()
    {
        Assert.Empty(GeneradorContabilidadElectronica.Revisar(Catalogo));

        var xml = GeneradorContabilidadElectronica.Catalogo(Rfc, 2027, 1, Catalogo);

        Assert.Empty(EsquemasSat.Validar(xml));
        XNamespace ns = EsquemasSat.NsCatalogo;
        Assert.Equal(("1.3", Rfc, "01", "2027"), ((string)xml.Root!.Attribute("Version")!, (string)xml.Root.Attribute("RFC")!,
            (string)xml.Root.Attribute("Mes")!, (string)xml.Root.Attribute("Anio")!));
        var ctas = xml.Root.Elements(ns + "Ctas").ToList();
        Assert.Equal(["100", "102", "102-01", "201", "201-01", "401"], ctas.Select(c => (string)c.Attribute("NumCta")!));
        var banco = ctas.Single(c => (string)c.Attribute("NumCta")! == "102-01");
        Assert.Equal(("102.01", "102", "3", "D"), ((string)banco.Attribute("CodAgrup")!, (string)banco.Attribute("SubCtaDe")!,
            (string)banco.Attribute("Nivel")!, (string)banco.Attribute("Natur")!));
        Assert.Null(ctas.Single(c => (string)c.Attribute("NumCta")! == "100").Attribute("SubCtaDe"));
        Assert.Equal("A", (string)ctas.Single(c => (string)c.Attribute("NumCta")! == "201-01").Attribute("Natur")!);
    }

    [Fact]
    public void K10_8_a_las_cuentas_sin_codigo_agrupador_valido_o_sin_naturaleza_se_listan_antes_de_generar()
    {
        var sinCodigo = Cuenta("102-02", Bancos, 3, null);
        var codigoInventado = Cuenta("102-03", Bancos, 3, "999.99");
        var sinNaturaleza = new CuentaContable(Guid.NewGuid(), "102-04", "Banco sin naturaleza", Bancos.Id, 3, null, TipoCuenta.Afectable,
            CuentaControl.Ninguna, "102.01", null);
        var inactiva = Cuenta("102-05", Bancos, 3, null);
        inactiva.Desactivar();

        var problemas = GeneradorContabilidadElectronica.Revisar([.. Catalogo, sinCodigo, codigoInventado, sinNaturaleza, inactiva]);

        Assert.Equal(
            [("102-02", GeneradorContabilidadElectronica.SinCodigoAgrupador), ("102-03", GeneradorContabilidadElectronica.CodigoAgrupadorInvalido),
             ("102-04", GeneradorContabilidadElectronica.SinNaturaleza)],
            problemas.Select(p => (p.Codigo, p.Problema)));
        Assert.Equal("999.99", problemas.Single(p => p.Codigo == "102-03").CodigoAgrupador);
    }

    [Fact]
    public void CA10_14_la_balanza_acumula_las_subcuentas_en_su_mayor_y_da_los_saldos_segun_la_naturaleza()
    {
        // Saldo inicial: banco 1,000 contra proveedor. Mes: proveedor se paga 400 (cargo) contra banco; ingreso de 250 al banco.
        var iniciales = new Dictionary<Guid, decimal> { [Banco.Id] = 1_000m, [Proveedor.Id] = -1_000m };
        var periodo = new Dictionary<Guid, (decimal, decimal)>
        {
            [Banco.Id] = (250m, 400m), [Proveedor.Id] = (400m, 0m), [Ingresos.Id] = (0m, 250m),
        };

        var filas = GeneradorContabilidadElectronica.FilasBalanza(Catalogo, iniciales, periodo);

        Assert.Equal(["100", "102", "102-01", "201", "201-01", "401"], filas.Select(f => f.Codigo));
        Assert.Equal(new FilaBalanzaCe("102-01", 1_000m, 250m, 400m, 850m), filas.Single(f => f.Codigo == "102-01"));
        Assert.Equal(new FilaBalanzaCe("100", 1_000m, 250m, 400m, 850m), filas.Single(f => f.Codigo == "100"));
        Assert.Equal(new FilaBalanzaCe("201", 1_000m, 400m, 0m, 600m), filas.Single(f => f.Codigo == "201"));
        Assert.Equal(new FilaBalanzaCe("401", 0m, 0m, 250m, 250m), filas.Single(f => f.Codigo == "401"));

        var xml = GeneradorContabilidadElectronica.Balanza(Rfc, 2027, 1, TipoEnvioBalanza.Normal, null, filas);
        Assert.Empty(EsquemasSat.Validar(xml));
        Assert.Equal("N", (string)xml.Root!.Attribute("TipoEnvio")!);
        var banco = xml.Root.Elements(XName.Get("Ctas", EsquemasSat.NsBalanza)).Single(c => (string)c.Attribute("NumCta")! == "102-01");
        Assert.Equal(("1000.00", "250.00", "400.00", "850.00"), ((string)banco.Attribute("SaldoIni")!, (string)banco.Attribute("Debe")!,
            (string)banco.Attribute("Haber")!, (string)banco.Attribute("SaldoFin")!));
    }

    [Fact]
    public void La_balanza_complementaria_lleva_la_fecha_de_modificacion_y_la_de_cierre_es_el_mes_13()
    {
        var filas = new[] { new FilaBalanzaCe("102-01", 10m, 0m, 0m, 10m) };

        var complementaria = GeneradorContabilidadElectronica.Balanza(Rfc, 2027, 1, TipoEnvioBalanza.Complementaria, new DateOnly(2027, 3, 5), filas);
        var cierre = GeneradorContabilidadElectronica.Balanza(Rfc, 2027, 13, TipoEnvioBalanza.Normal, null, filas);

        Assert.Empty(EsquemasSat.Validar(complementaria));
        Assert.Equal(("C", "2027-03-05"), ((string)complementaria.Root!.Attribute("TipoEnvio")!, (string)complementaria.Root.Attribute("FechaModBal")!));
        Assert.Empty(EsquemasSat.Validar(cierre));
        Assert.Equal("13", (string)cierre.Root!.Attribute("Mes")!);
        Assert.Equal("MIL010101AB1202701BC", GeneradorContabilidadElectronica.NombreArchivo(Rfc, 2027, 1, "BC"));
    }

    [Fact]
    public void CA10_14_las_polizas_llevan_sus_transacciones_y_el_CFDI_de_origen_y_cumplen_el_esquema()
    {
        const string uuid = "6f1a2b3c-4d5e-4f60-8a7b-9c0d1e2f3a4b";
        var conCfdi = new PolizaCe("FP-000001", new DateOnly(2027, 1, 15), "Factura proveedor A-15", uuid,
        [
            new("102-01", "Banco nacional", null, 0m, 1_160m),
            new("201-01", "Proveedor nacional", "Pago de la factura A-15", 1_160m, 0m),
        ]);
        var sinDatos = new PolizaCe("FP-000002", new DateOnly(2027, 1, 20), "Factura sin CFDI en el buzón", "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE",
            [new("102-01", "Banco nacional", null, 10m, 0m), new("201-01", "Proveedor nacional", null, 0m, 10m)]);
        var comprobantes = new Dictionary<string, ComprobanteNacionalContable>
        {
            [uuid.ToUpperInvariant()] = new(uuid.ToUpperInvariant(), "PRO850101AB1", 62.996m, "USD", 18.4123456m),
        };
        var avisos = new List<string>();

        var xml = GeneradorContabilidadElectronica.Polizas(Rfc, 2027, 1, TipoSolicitudPolizas.AF, "ABC1234567/12", null,
            [conCfdi, sinDatos], comprobantes, avisos);

        Assert.Empty(EsquemasSat.Validar(xml));
        XNamespace ns = EsquemasSat.NsPolizas;
        Assert.Equal(("AF", "ABC1234567/12"), ((string)xml.Root!.Attribute("TipoSolicitud")!, (string)xml.Root.Attribute("NumOrden")!));
        var polizas = xml.Root.Elements(ns + "Poliza").ToList();
        Assert.Equal(["FP-000001", "FP-000002"], polizas.Select(p => (string)p.Attribute("NumUnIdenPol")!));
        var transacciones = polizas[0].Elements(ns + "Transaccion").ToList();
        Assert.Equal(["Factura proveedor A-15", "Pago de la factura A-15"], transacciones.Select(t => (string)t.Attribute("Concepto")!));
        var comp = transacciones.Select(t => t.Element(ns + "CompNal")!).ToList();
        Assert.All(comp, c => Assert.Equal((uuid.ToUpperInvariant(), "PRO850101AB1", "63.00", "USD", "18.41235"),
            ((string)c.Attribute("UUID_CFDI")!, (string)c.Attribute("RFC")!, (string)c.Attribute("MontoTotal")!,
             (string)c.Attribute("Moneda")!, (string)c.Attribute("TipCamb")!)));
        Assert.Empty(polizas[1].Descendants(ns + "CompNal"));
        Assert.Contains(avisos, a => a.Contains("FP-000002") && a.Contains("sale sin comprobante"));
    }

    [Fact]
    public void Las_polizas_para_devolucion_llevan_numero_de_tramite_y_los_textos_se_ajustan_al_largo_del_SAT()
    {
        var poliza = new PolizaCe("DI-000001", new DateOnly(2027, 2, 1), new string('x', 350), null,
            [new("102-01", new string('n', 150), "  concepto   con\nsaltos ", 1m, 0m), new("401", "Ingresos", null, 0m, 1m)]);

        var xml = GeneradorContabilidadElectronica.Polizas(Rfc, 2027, 2, TipoSolicitudPolizas.DE, null, "AB123456789012", [poliza],
            new Dictionary<string, ComprobanteNacionalContable>(), []);

        Assert.Empty(EsquemasSat.Validar(xml));
        XNamespace ns = EsquemasSat.NsPolizas;
        Assert.Equal("AB123456789012", (string)xml.Root!.Attribute("NumTramite")!);
        Assert.Null(xml.Root.Attribute("NumOrden"));
        var p = xml.Root.Element(ns + "Poliza")!;
        Assert.Equal(300, ((string)p.Attribute("Concepto")!).Length);
        var t = p.Elements(ns + "Transaccion").ToList();
        Assert.Equal(("concepto con saltos", 100), ((string)t[0].Attribute("Concepto")!, ((string)t[0].Attribute("DesCta")!).Length));
        Assert.Equal(200, ((string)t[1].Attribute("Concepto")!).Length);
    }

    [Fact]
    public void El_esquema_del_SAT_rechaza_un_RFC_mal_formado()
    {
        var xml = GeneradorContabilidadElectronica.Catalogo("NO-ES-RFC", 2027, 1, Catalogo);

        Assert.NotEmpty(EsquemasSat.Validar(xml));
        Assert.False(GeneradorContabilidadElectronica.EsRfcValido("NO-ES-RFC"));
        Assert.True(GeneradorContabilidadElectronica.EsRfcValido(Rfc));
    }

    [Fact]
    public void El_archivo_es_UTF8_sin_BOM()
    {
        var bytes = GenerarContabilidadElectronicaHandler.Bytes(GeneradorContabilidadElectronica.Catalogo(Rfc, 2027, 1, Catalogo));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Theory]
    [InlineData("balanza", 1, "C", null, null, null, null, "fechaModificacion")]
    [InlineData("polizas", 1, null, null, null, null, null, "solicitud")]
    [InlineData("polizas", 1, null, null, "AF", null, null, "numOrden")]
    [InlineData("polizas", 1, null, null, "CO", null, "123", "numTramite")]
    [InlineData("catalogo", 13, null, null, null, null, null, "mes")]
    public void La_generacion_valida_lo_que_pide_cada_archivo(string tipo, int mes, string? envio, string? fecha, string? solicitud,
        string? orden, string? tramite, string campo)
    {
        var query = ParametrosContabilidadElectronica.Leer(tipo, 2027, mes, envio, fecha, solicitud, orden, tramite, null);

        var resultado = new GenerarContabilidadElectronicaValidator().Validate(query);

        Assert.False(resultado.IsValid);
        Assert.Contains(resultado.Errors, e => e.PropertyName.Equals(campo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Los_parametros_mal_escritos_responden_en_espanol()
    {
        var ex = Assert.Throws<Millet.SharedKernel.Application.Exceptions.ValidationException>(() =>
            ParametrosContabilidadElectronica.Leer("diot", 2027, 1, "X", "05/03/2027", "ZZ", null, null, "pdf"));

        Assert.Equal(["tipo", "tipoEnvio", "fechaModificacion", "solicitud", "formato"], ex.Errors.Select(e => e.Field));
        var ok = ParametrosContabilidadElectronica.Leer("Balanza", 2027, 13, "c", "2027-03-05", null, null, null, "xml");
        Assert.Equal((TipoArchivoContabilidadElectronica.Balanza, TipoEnvioBalanza.Complementaria, new DateOnly(2027, 3, 5), false),
            (ok.Tipo, ok.TipoEnvio, ok.FechaModificacion, ok.Zip));
        Assert.True(new GenerarContabilidadElectronicaValidator().Validate(ok).IsValid);
    }

    private static CuentaContable Cuenta(string codigo, CuentaContable? padre, int nivel, string? agrupador,
        NaturalezaCuenta naturaleza = NaturalezaCuenta.Deudora) =>
        new(Guid.NewGuid(), codigo, $"Cuenta {codigo}", padre?.Id, nivel, naturaleza, TipoCuenta.Afectable, CuentaControl.Ninguna, agrupador, null);
}
