using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// El <c>Total</c> de los eventos de factura (venta y proveedor) es el total del CFDI, que ya viene neto de retenciones. La póliza con
/// retención cuadra contra la plantilla sembrada: cliente o proveedor por el total neto y la retención aparte. Datos FIX.
/// </summary>
public class RetencionesTotalNetoTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Cliente = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Opciones = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static EventoContable Normalizar(string tipo, object payload) => N.Normalizar(tipo, Guid.NewGuid(), JsonSerializer.Serialize(payload));

    [Fact]
    public void La_venta_con_retencion_carga_al_cliente_el_total_neto_del_CFDI_y_cuadra()
    {
        // Flete: 1,000 + IVA 160 − retención de IVA 40 = 1,120 (Total del CFDI).
        var e = Normalizar(N.FacturaVentaTimbrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaVentaId = Guid.NewGuid(), Uuid = "UUID-FIX", Total = 1_120m, Moneda = "MXN",
            Folio = "FV-FIX-2", Subtotal = 1_000m, Descuento = 0m, Iva = 160m, RetencionesTotal = 40m, SucursalId = Sucursal, ClienteId = Cliente,
            Lineas = new object[] { new { TipoProducto = "Servicio", Importe = 1_000m, Descuento = 0m } },
        });

        (e.Importes["TotalMenosRetenciones"], e.Importes["RetencionesTotal"]).Should().Be((1_120m, 40m));
        Cuadra(e, Plantilla("C15ListenersContables", N.FacturaVentaTimbrada)).Should().Be((1_160m, 1_160m),
            "CLIENTES 1,120 + RETENCIONES_CLIENTES 40 = VENTAS 1,000 + IVA 160");
    }

    [Fact]
    public void La_factura_de_proveedor_con_retencion_abona_al_proveedor_el_total_neto_del_CFDI_y_cuadra()
    {
        var e = Normalizar(N.FacturaProveedorRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaProveedorId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid(), TotalFactura = 1_120m,
            Lineas = new object[] { new { LineaOcId = Guid.NewGuid(), Importe = 1_000m, EsServicio = false } },
            ProveedorId = Proveedor, Uuid = "UUID-FIX", Subtotal = 1_000m, Iva = 160m, RetencionesTotal = 40m, Moneda = "MXN", SucursalId = Sucursal,
        });

        e.Importes["TotalMenosRetenciones"].Should().Be(1_120m);
        Cuadra(e, Plantilla("C14MotorPolizas", N.FacturaProveedorRegistrada)).Should().Be((1_160m, 1_160m),
            "puente 1,000 + IVA 160 = PROVEEDORES 1,120 + RETENCIONES_POR_PAGAR 40");
    }

    [Fact]
    public void La_nota_de_credito_de_proveedor_con_retencion_carga_al_proveedor_el_total_neto_y_cuadra()
    {
        var e = Normalizar(N.NotaCreditoProveedorRegistrada, new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, NotaCreditoId = Guid.NewGuid(), ProveedorId = Proveedor, FacturaOrigenId = Guid.NewGuid(),
            Total = 560m, Monto = 560m, Subtotal = 500m, Iva = 80m, RetencionesTotal = 20m, SucursalId = Sucursal,
        });

        e.Importes["TotalMenosRetenciones"].Should().Be(560m);
        Cuadra(e, Plantilla("C15ListenersContables", N.NotaCreditoProveedorRegistrada)).Should().Be((580m, 580m));
    }

    /// <summary>Cargos y abonos que arma la plantilla con los importes del evento (PorLinea: líneas de servicio o no, según la plantilla).</summary>
    private static (decimal Cargos, decimal Abonos) Cuadra(EventoContable e, List<LineaPlantillaContable> plantilla)
    {
        decimal Importe(LineaPlantillaContable l) => l.PorLinea && e.Lineas.Count > 0
            ? e.Lineas.Where(x => l.SoloServicios is null || x.EsServicio == l.SoloServicios).Sum(x => x.Importe)
            : e.Importes.GetValueOrDefault(l.Campo) ?? 0m;
        return (plantilla.Where(l => l.Lado == LadoPartida.Cargo).Sum(Importe), plantilla.Where(l => l.Lado == LadoPartida.Abono).Sum(Importe));
    }

    /// <summary>Líneas de la plantilla tal como las siembra la migración (primer arreglo JSON después del tipo de evento).</summary>
    private static List<LineaPlantillaContable> Plantilla(string migracion, string tipo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        var ruta = Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            $"*_{migracion}.cs").Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var sql = File.ReadAllText(ruta);
        var desde = sql.IndexOf($"'{tipo}'", StringComparison.Ordinal);
        desde.Should().BeGreaterThan(-1, $"la migración {migracion} siembra {tipo}");
        var inicio = sql.IndexOf("'[", desde, StringComparison.Ordinal) + 1;
        var fin = sql.IndexOf("]'", inicio, StringComparison.Ordinal) + 1;
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(sql[inicio..fin], Opciones)!;
    }
}
