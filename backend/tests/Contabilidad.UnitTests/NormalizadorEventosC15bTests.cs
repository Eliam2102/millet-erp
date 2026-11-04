using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// C1.5b (86e3vrh7y) · caja chica, viáticos, tarjetas corporativas y amortización del anticipo a proveedor: lectura de los eventos de
/// CxP y cuadre de las plantillas sembradas por la migración con los importes que arma el normalizador. Payloads como el outbox
/// (PascalCase). Datos FIX.
/// </summary>
public class NormalizadorEventosC15bTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static EventoContable Normalizar(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), Json(payload));

    private static object Gasto(string origen, decimal subtotal, decimal iva, decimal ivaNoAcreditable, Guid? sucursal = null,
        Guid? lineaId = null) => new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, Origen = origen, DocumentoId = Guid.NewGuid(), LineaId = lineaId,
        FacturaProveedorId = (Guid?)null, Fecha = new DateOnly(2026, 11, 2), ProveedorId = Proveedor, ProveedorRfc = "XAXX010101000",
        Uuid = "UUID-FIX-1", Deducible = ivaNoAcreditable == 0m, ClasificacionNoDeducible = (string?)null, Subtotal = subtotal,
        IvaTrasladado = iva, IvaNoAcreditable = ivaNoAcreditable, Total = subtotal + iva, Moneda = "MXN", TipoCambio = (decimal?)null,
        ConceptoContable = (string?)null, SucursalId = sucursal ?? Sucursal,
    };

    private static object Pasivo(string origenTipo, decimal monto, Guid origenId) => new
    {
        EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaProveedorId = Guid.Empty, ProveedorId = Guid.Empty, OrdenCompraId = (Guid?)null,
        MontoTotal = monto, SaldoPendiente = monto, Moneda = "MXN", TipoCambio = (decimal?)null, FechaVencimiento = new DateOnly(2026, 11, 3),
        UuidCfdi = (string?)null, FolioProveedor = (string?)null, OrigenTipo = origenTipo, OrigenId = origenId, SucursalId = Sucursal,
    };

    [Fact]
    public void Gasto_de_caja_chica_separa_gasto_e_IVA_acreditable_y_abona_al_fondo_por_su_origen()
    {
        var linea = Guid.NewGuid();
        var e = Normalizar(N.GastoRegistrado, Gasto("caja_chica", 1_000m, 160m, 0m, lineaId: linea));

        e.Tipo.Should().Be(N.GastoRegistrado);
        e.Importes["Gasto"].Should().Be(1_000m);
        e.Importes["IvaAcreditable"].Should().Be(160m);
        e.Importes["Total"].Should().Be(1_160m);
        e.Lineas.Should().ContainSingle().Which.Should().Match<LineaEventoContable>(l => l.Importe == 1_160m && l.Clasificador == "caja_chica");
        e.SucursalId.Should().Be(Sucursal);
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "cuentas_por_pagar" && d.Tipo == "gasto" && d.Id == linea && d.Uuid == "UUID-FIX-1");
        e.FechaContable.Should().Be(new DateOnly(2026, 11, 3));
    }

    [Fact]
    public void Gasto_no_deducible_manda_el_IVA_al_gasto()
    {
        // P8.6: con CFDI pero no deducible, el IVA no es acreditable y se queda en el gasto.
        var e = Normalizar(N.GastoRegistrado, Gasto("viaticos", 500m, 80m, 80m));

        e.Importes["Gasto"].Should().Be(580m);
        e.Importes["IvaAcreditable"].Should().Be(0m);
        e.Lineas.Single().Clasificador.Should().Be("viaticos");
    }

    [Fact]
    public void Captura_retroactiva_desde_el_estado_de_cuenta_es_un_gasto_de_tarjeta()
    {
        var e = Normalizar(N.GastoRegistrado, Gasto("estado_cuenta", 300m, 0m, 0m));

        e.Lineas.Single().Clasificador.Should().Be("tarjeta");
        e.Documento.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Gasto_sin_sucursal_queda_sin_ella_para_que_el_motor_lo_mande_a_la_bandeja()
    {
        var payload = Json(Gasto("tarjeta", 100m, 16m, 0m)).Replace($"\"SucursalId\":\"{Sucursal}\"", "\"SucursalId\":null");
        var e = N.Normalizar(N.GastoRegistrado, Guid.NewGuid(), payload);

        e.SucursalId.Should().BeNull();
    }

    [Theory]
    [InlineData("ReposicionCajaChica", "caja_chica")]
    [InlineData("PrestamoViaticos", "viaticos")]
    [InlineData("LiquidacionViaticos", "viaticos")]
    public void Pasivo_interno_carga_al_fondo_o_al_deudor_con_llave_por_origen(string origenTipo, string clasificador)
    {
        var origen = Guid.NewGuid();
        var e = Normalizar(N.PasivoAutorizadoParaPago, Pasivo(origenTipo, 2_500m, origen));

        e.Importes["Total"].Should().Be(2_500m);
        e.Lineas.Should().ContainSingle().Which.Should().Match<LineaEventoContable>(l => l.Importe == 2_500m && l.Clasificador == clasificador);
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Tipo == "pasivo_interno" && d.Id == origen);
        // El mismo pasivo publicado dos veces (otro id de evento) no duplica la póliza.
        e.Llave.Should().Be($"{N.PasivoAutorizadoParaPago}:{origenTipo}:{origen}");
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void Pasivo_de_una_factura_de_proveedor_no_genera_poliza()
    {
        // La factura ya tiene su póliza TT (#2); su pasivo hacia Tesorería no mueve contabilidad.
        var e = Normalizar(N.PasivoAutorizadoParaPago, Pasivo("Factura", 1_160m, Guid.NewGuid()));

        N.SinMovimiento(e).Should().BeTrue();
    }

    [Fact]
    public void Cierre_de_estado_de_cuenta_documenta_la_factura_agregada_con_el_banco_como_proveedor()
    {
        var factura = Guid.NewGuid();
        var banco = Guid.NewGuid();
        var e = Normalizar(N.EstadoCuentaTcCerrado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, EstadoCuentaTcId = Guid.NewGuid(), TarjetaId = Guid.NewGuid(), TotalBancoMxn = 4_640m,
            FacturaProveedorId = factura, DiferenciaCambiariaMxn = (decimal?)null, ProveedorId = banco, SucursalId = Sucursal,
        });

        e.Importes["Total"].Should().Be(4_640m);
        e.ProveedorId.Should().Be(banco);
        e.Moneda.Should().Be("MXN");
        // Mismo documento que la factura: su pago (#5) toma de aquí proveedor y sucursal, y su cancelación anula esta póliza.
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "cuentas_por_pagar" && d.Tipo == "factura_proveedor" && d.Id == factura);
    }

    [Fact]
    public void Amortizacion_sin_sucursal_la_espera_de_la_poliza_de_la_factura()
    {
        var aplicacion = Guid.NewGuid();
        var factura = Guid.NewGuid();
        var e = Normalizar(N.AnticipoProveedorAmortizado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, AplicacionId = aplicacion, AnticipoId = Guid.NewGuid(), FacturaProveedorId = factura,
            ProveedorId = Proveedor, Monto = 600m, Moneda = "USD", TipoCambio = 18.25m, SucursalId = (Guid?)null,
            UuidAnticipo = "UUID-ANT", UuidFactura = "UUID-FAC",
        });

        e.Importes["Total"].Should().Be(600m);
        e.ProveedorId.Should().Be(Proveedor);
        e.Moneda.Should().Be("USD");
        e.TipoCambio.Should().Be(18.25m);
        e.SucursalId.Should().BeNull();
        e.FacturaOrigenId.Should().Be(factura);
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Tipo == "amortizacion_anticipo" && d.Id == aplicacion && d.Uuid == "UUID-FAC");
    }

    [Fact]
    public void Amortizacion_con_sucursal_no_depende_de_otra_poliza()
    {
        var e = Normalizar(N.AnticipoProveedorAmortizado, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, AplicacionId = Guid.NewGuid(), AnticipoId = Guid.NewGuid(),
            FacturaProveedorId = Guid.NewGuid(), ProveedorId = Proveedor, Monto = 600m, Moneda = "MXN", SucursalId = Sucursal,
        });

        e.SucursalId.Should().Be(Sucursal);
        e.FacturaOrigenId.Should().BeNull();
    }

    // ---------------------------------------------------------------- Cuadre de las plantillas sembradas

    public static TheoryData<string, string> EventosQueMuevenDinero => new()
    {
        { N.GastoRegistrado, Json(Gasto("caja_chica", 1_000m, 160m, 0m)) },
        { N.GastoRegistrado, Json(Gasto("tarjeta", 862.07m, 137.93m, 137.93m)) },
        { N.PasivoAutorizadoParaPago, Json(Pasivo("ReposicionCajaChica", 3_480m, Guid.NewGuid())) },
        {
            N.EstadoCuentaTcCerrado, Json(new
            {
                EmpresaId = Empresa, OcurridoEn = Ocurrido, TotalBancoMxn = 4_640m, FacturaProveedorId = Guid.NewGuid(),
                ProveedorId = Proveedor, SucursalId = Sucursal,
            })
        },
        {
            N.AnticipoProveedorAmortizado, Json(new
            {
                EmpresaId = Empresa, OcurridoEn = Ocurrido, AplicacionId = Guid.NewGuid(), FacturaProveedorId = Guid.NewGuid(),
                ProveedorId = Proveedor, Monto = 600m, Moneda = "MXN", SucursalId = Sucursal,
            })
        },
    };

    [Theory]
    [MemberData(nameof(EventosQueMuevenDinero))]
    public void La_plantilla_sembrada_cuadra_con_los_importes_del_evento(string tipo, string payload)
    {
        var e = N.Normalizar(tipo, Guid.NewGuid(), payload);
        var plantilla = PlantillasSembradas()[tipo];

        decimal Importe(LineaPlantillaContable l) => l.PorLinea && e.Lineas.Count > 0 ? e.Lineas.Sum(x => x.Importe) : e.Importes[l.Campo]!.Value;
        var cargos = plantilla.Where(l => l.Lado == LadoPartida.Cargo).Sum(Importe);
        var abonos = plantilla.Where(l => l.Lado == LadoPartida.Abono).Sum(Importe);

        cargos.Should().BeGreaterThan(0m);
        cargos.Should().Be(abonos);
    }

    [Fact]
    public void Las_contrapartidas_por_origen_usan_el_rol_por_clasificador()
    {
        var plantillas = PlantillasSembradas();

        plantillas[N.GastoRegistrado].Single(l => l.Lado == LadoPartida.Abono)
            .Should().Match<LineaPlantillaContable>(l => l.Rol == "CONTRAPARTIDA_GASTO" && l.RolPorClasificador && l.PorLinea);
        plantillas[N.PasivoAutorizadoParaPago].Single(l => l.Lado == LadoPartida.Cargo)
            .Should().Match<LineaPlantillaContable>(l => l.Rol == "CONTRAPARTIDA_GASTO" && l.RolPorClasificador && l.PorLinea);
        // El sufijo que arma el motor coincide con los roles sembrados.
        foreach (var origen in new[] { N.OrigenCajaChica, N.OrigenViaticos, N.OrigenTarjeta })
            Migracion().Should().Contain($"'CONTRAPARTIDA_GASTO_{GenerarPolizaDesdeEventoHandler.Clasificador(origen)}'");
        plantillas[N.EstadoCuentaTcCerrado].Single(l => l.Lado == LadoPartida.Abono).Auxiliar.Should().Be(AuxiliarPlantilla.Proveedor);
        plantillas[N.AnticipoProveedorAmortizado].Single(l => l.Lado == LadoPartida.Cargo).Auxiliar.Should().Be(AuxiliarPlantilla.Proveedor);
    }

    private static string Migracion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        var ruta = Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_C15bPlantillasGastosTarjetas.cs").Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        return File.ReadAllText(ruta);
    }

    /// <summary>Tipo de evento → líneas de la plantilla, leídas del SQL de la migración (lo que de verdad se siembra).</summary>
    private static Dictionary<string, List<LineaPlantillaContable>> PlantillasSembradas()
    {
        var opciones = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        return Regex.Matches(Migracion(), @"\('(?<tipo>cuentas_por_pagar\.[^']+)', '[A-Z]{2}', '[^']*', '[^']*', '(?<lineas>\[[^']*\])'\)")
            .ToDictionary(m => m.Groups["tipo"].Value,
                m => JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, opciones)!);
    }
}
