using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using static Millet.Integraciones.Aw.Infrastructure.Pedidos.AwSolicitudesSqlReader;

namespace Millet.Integraciones.Aw.UnitTests.Pedidos;

/// <summary>
/// Tests del mapeo filas-de-vistas → contrato de Facturación
/// (<c>AwSolicitudesSqlReader.Construir</c>, flujo 2 ADR-0048). El SQL en sí
/// se valida en el E2E contra MILLET_INTEGRACION_DEV (runbook); aquí se
/// cubre la lógica de mapeos/faltantes que decide qué cae a la bandeja.
/// Sucursal y canal de venta llegan ya resueltos por el caller (catálogos
/// clave_aw — rediseño 2026-07-07 + FAC-ING-PR2); su resolución se prueba
/// vía <c>ISucursalPorClaveAwResolver</c> / <c>ICanalVentaPorClaveAwResolver</c>.
/// </summary>
public class AwSolicitudesSqlReaderTests
{
    private static readonly Guid SucursalId =
        Guid.Parse("00000000-0000-0000-0000-00000000c1c0");

    // Canal ya resuelto por el caller (2 = "Tienda Circuito" del seed).
    private const short CanalVentaId = 2;

    // Override operativo de clase (escape hatch); el camino por defecto es
    // que la vista ya entregue el nombre del enum (tests de passthrough abajo).
    private static AwPedidosOptions OpcionesCompletas() => new()
    {
        MapeoComportamiento = { ["MOSTRADOR"] = ComportamientoFiscal.MostradorInmediato },
    };

    private static CabeceraRow Cabecera(
        string numeroSucursal = "CIRCUITO",
        string? canalVentas = "Ventas Circuito",
        string? clase = "MOSTRADOR",
        decimal? ivaPorcentaje = null,
        decimal? ranura = null) => new(
        NumeroPedido: "10432218",
        NumeroSucursal: numeroSucursal,
        ClienteRef: "56380",
        ClienteNombre: "GLOBAL CONSTRUCCIONES SA DE CV",
        RfcCliente: "GCO123456AB9",
        UsoCfdi: "G03",
        MetodoPago: "PUE",
        FormaPago: "03",
        CondicionPago: "CONTADO",
        Divisa: "MXN",
        ObraId: 5000483,
        ObraNombre: "HOTEL CIELO",
        NotasPedido: "entregar en obra",
        CanalVentas: canalVentas,
        Clase: clase,
        FechaTransaccion: new DateOnly(2026, 7, 1),
        PedidoSustituidoNumero: null,
        EstadoOrigen: "15",
        // Totales de control null por default: la validación PR5 solo corre
        // cuando la vista los aporta (tests explícitos abajo).
        TotalCantidad: null,
        TotalM2: null,
        ImporteTotal: null,
        IvaPorcentaje: ivaPorcentaje,
        Ranura: ranura);

    private static LineaRow Linea(int posicion = 1, bool? requierePedimento = null) => new(
        NumeroPosicion: posicion,
        ProductoRef: "5137",
        Descripcion: "VIDRIO CLARO 6MM",
        DetalleProcesos: "TEMPLADO / BISEL",
        Cantidad: 3m,
        UnidadMedida: "M2",
        ImportePieza: 1743.68m,
        DescuentoPorcentaje: 5m,
        Descuento: 87.18m,
        AlmacenNivel1: "CIRCUITO",
        AlmacenNivel2: "PISO",
        AlmacenNivel3: null,
        AlmacenNivel4: null,
        AlmacenIdUbicacion: 42,
        RequierePedimento: requierePedimento);

    /// <summary>
    /// Shim sobre <see cref="AwSolicitudesSqlReader.Construir"/> (que desde
    /// FAC-ING-PR3 devuelve <see cref="LecturaPedidoAw"/>): los tests de mapeo
    /// validan <c>Datos</c>; motivo y flag de config-fixable se prueban en los
    /// tests de "Lectura" al final.
    /// </summary>
    private static DatosPedidoAw? Construir(
        CabeceraRow cab, IReadOnlyList<LineaRow> lineas, IReadOnlyList<ComponenteRow> componentes,
        Guid sucursalId, short canalVentaId, AwPedidosOptions opts, ILogger logger) =>
        AwSolicitudesSqlReader.Construir(cab, lineas, componentes, sucursalId, canalVentaId, opts, logger).Datos;

    private static ComponenteRow Componente(int posicion = 1) => new(
        NumeroPosicion: posicion,
        ProductoRef: "9001",
        Descripcion: "TEMPLADO",
        AltoMm: 1200m,
        AnchoMm: 800m,
        M2PorPieza: 0.96m,
        Importe: 500m);

    [Fact]
    public void Construir_MapeaCabeceraLineasYBom()
    {
        var datos = Construir(
            Cabecera(), new[] { Linea(1), Linea(2) }, new[] { Componente(1) },
            SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal("10432218", datos!.NumeroPedido);
        Assert.Equal(SucursalId, datos.SucursalId);
        Assert.Equal("56380", datos.ClienteRef);
        Assert.Equal(CanalVentaId, datos.CanalVenta);
        Assert.Equal(ComportamientoFiscal.MostradorInmediato, datos.ComportamientoFiscal);
        Assert.Equal("MXN", datos.Moneda);
        Assert.Equal(5000483, datos.ObraId);
        Assert.Equal("15", datos.EstadoOrigen);
        Assert.Equal(2, datos.Lineas.Count);

        // Línea 1 lleva el BOM serializado; línea 2 (sin componentes) null.
        var l1 = datos.Lineas[0];
        Assert.Equal("5137", l1.ProductoRef);
        Assert.Equal(1743.68m, l1.Precio);
        Assert.Equal(87.18m, l1.Descuento);
        Assert.False(l1.RequierePedimento);
        // Claves SAT NO vienen de A+W — las aporta el master (producto_aw).
        Assert.Null(l1.ClaveProdServSat);
        Assert.Null(l1.ClaveUnidadSat);
        Assert.NotNull(l1.BomJson);
        using var bom = JsonDocument.Parse(l1.BomJson!);
        Assert.Equal(1, bom.RootElement.GetArrayLength());
        Assert.Null(datos.Lineas[1].BomJson);

        // El snapshot lleva TODO lo leído, incluidos los campos sin columna
        // en el ERP (detalle_procesos, rfc, totales de control).
        using var payload = JsonDocument.Parse(datos.PayloadCrudo);
        Assert.True(payload.RootElement.TryGetProperty("cabecera", out var cab));
        Assert.Equal("GCO123456AB9", cab.GetProperty("rfcCliente").GetString());
        Assert.Equal("TEMPLADO / BISEL",
            payload.RootElement.GetProperty("lineas")[0].GetProperty("detalleProcesos").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("componentes").GetArrayLength());
    }

    [Fact]
    public void Construir_RequierePedimentoTrue_SePropaga()
    {
        var datos = Construir(
            Cabecera(), new[] { Linea(1, requierePedimento: true) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.True(datos!.Lineas[0].RequierePedimento);
    }

    // ------------------------------------------------------------------
    // IVA de documento + bruto→neto (FAC-DET-PR2)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("16.00")] // A+W entrega porcentaje
    [InlineData("0.16")]  // tolerancia a fracción si la vista cambia
    public void Construir_ConIvaDocumento_CalculaNetoYTransfiereTasa(string ivaCrudo)
    {
        var iva = decimal.Parse(ivaCrudo, System.Globalization.CultureInfo.InvariantCulture);
        var datos = Construir(
            Cabecera(ivaPorcentaje: iva), new[] { Linea(1), Linea(2) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        foreach (var linea in datos!.Lineas)
        {
            Assert.Equal(0.16m, linea.TasaIva);
            // Neto hacia atrás desde el bruto de la vista (1743.68 / 1.16),
            // redondeado a 6 decimales (límite CFDI de ValorUnitario).
            Assert.Equal(1503.172414m, linea.Precio);
            Assert.Equal(75.155172m, linea.Descuento);
        }

        // El snapshot conserva los brutos originales de la vista.
        using var payload = JsonDocument.Parse(datos.PayloadCrudo);
        Assert.Equal(1743.68m,
            payload.RootElement.GetProperty("lineas")[0].GetProperty("importePieza").GetDecimal());
    }

    [Fact]
    public void Construir_ConIvaCero_ExportacionMantienePrecioYTasaCero()
    {
        var datos = Construir(
            Cabecera(ivaPorcentaje: 0m), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal(0m, datos!.Lineas[0].TasaIva);
        Assert.Equal(1743.68m, datos.Lineas[0].Precio);
        Assert.Equal(87.18m, datos.Lineas[0].Descuento);
    }

    [Fact]
    public void Construir_SinIvaEnVista_ComportamientoPrevioIntacto()
    {
        var datos = Construir(
            Cabecera(), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Null(datos!.Lineas[0].TasaIva);
        Assert.Equal(1743.68m, datos.Lineas[0].Precio);
        Assert.Equal(87.18m, datos.Lineas[0].Descuento);
    }

    [Fact]
    public void Construir_IvaFueraDeRango_DevuelveNull()
    {
        // 250 → 2.5 tras normalizar: sigue fuera de [0,1] → bandeja.
        var datos = Construir(
            Cabecera(ivaPorcentaje: 250m), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(datos);
    }

    [Fact]
    public void Construir_ConciliacionDeTotales_CorreSobreBrutos()
    {
        // importe_total de cabecera es BRUTO: debe cuadrar contra las líneas
        // ANTES de convertir a neto (2 líneas: 3×1743.68 − 87.18 = 5143.86).
        var cab = Cabecera(ivaPorcentaje: 16m) with { ImporteTotal = 2 * 5143.86m };
        var datos = Construir(
            cab, new[] { Linea(1), Linea(2) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal(1503.172414m, datos!.Lineas[0].Precio);
    }

    [Fact]
    public void Construir_ClaseComoNombreDeEnum_ParseaSinOverride()
    {
        // Camino por defecto post-rediseño: la vista entrega el nombre del
        // enum ComportamientoFiscal y no hace falta ningún diccionario. El
        // canal ya viene resuelto por el caller (catálogo, FAC-ING-PR2) —
        // Construir lo transfiere tal cual al contrato.
        var datos = Construir(
            Cabecera(clase: "ExportacionConCce"),
            new[] { Linea() }, Array.Empty<ComponenteRow>(),
            SucursalId, CanalVentaId, new AwPedidosOptions(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal(CanalVentaId, datos!.CanalVenta);
        Assert.Equal(ComportamientoFiscal.ExportacionConCce, datos.ComportamientoFiscal);
    }

    [Fact]
    public void Construir_ClaseSinTraduccion_DevuelveNull()
    {
        // clase cruda pendiente de regla fiscal (gap G14) → bandeja.
        var datos = Construir(
            Cabecera(clase: "Ventas Cancun"), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(datos);
    }

    [Fact]
    public void Construir_SinLineas_DevuelveNull()
    {
        var datos = Construir(
            Cabecera(), Array.Empty<LineaRow>(),
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(datos);
    }

    [Fact]
    public void Construir_TotalCantidadNoCuadra_DevuelveNull()
    {
        // Cabecera dice 3 piezas pero las dos líneas suman 6 → null
        // (semántica TotalesNoCuadran, PR5).
        var cab = Cabecera() with { TotalCantidad = 3m };
        var datos = Construir(
            cab, new[] { Linea(1), Linea(2) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(datos);
    }

    [Fact]
    public void Construir_ImporteTotalNoCuadra_DevuelveNull()
    {
        // total_cantidad cuadra (3), pero importe_total de cabecera difiere
        // del de líneas (3×1743.68−87.18 = 5143.86 ≠ 9999).
        var cab = Cabecera() with { TotalCantidad = 3m, ImporteTotal = 9999m };
        var datos = Construir(
            cab, new[] { Linea(1) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(datos);
    }

    [Fact]
    public void Construir_TotalesQueCuadran_Construye()
    {
        // 1 línea: cantidad 3, importe 3×1743.68−87.18 = 5143.86 → cuadra.
        var cab = Cabecera() with { TotalCantidad = 3m, ImporteTotal = 5143.86m };
        var datos = Construir(
            cab, new[] { Linea(1) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
    }

    // ------------------------------------------------------------------
    // Contrato LecturaPedidoAw (FAC-ING-PR3): motivo + config-fixable
    // ------------------------------------------------------------------

    [Fact]
    public void Lectura_ClaseSinTraduccion_EsConfigFixable()
    {
        var lectura = AwSolicitudesSqlReader.Construir(
            Cabecera(clase: "CC Mérida"), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(lectura.Datos);
        Assert.True(lectura.EsperaConfiguracion);
        Assert.Equal(MotivoExcepcion.ComportamientoSinRegla, lectura.Motivo);
        Assert.Contains("CC Mérida", lectura.Detalle);
    }

    [Fact]
    public void Lectura_SinLineas_EsDatoInvalido_NoConfig()
    {
        var lectura = AwSolicitudesSqlReader.Construir(
            Cabecera(), Array.Empty<LineaRow>(),
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(lectura.Datos);
        Assert.False(lectura.EsperaConfiguracion);
        Assert.Equal(MotivoExcepcion.Otro, lectura.Motivo);
    }

    [Fact]
    public void Lectura_TotalesNoCuadran_LlevaMotivoEspecifico()
    {
        var cab = Cabecera() with { TotalCantidad = 3m };
        var lectura = AwSolicitudesSqlReader.Construir(
            cab, new[] { Linea(1), Linea(2) },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(lectura.Datos);
        Assert.False(lectura.EsperaConfiguracion);
        Assert.Equal(MotivoExcepcion.TotalesNoCuadran, lectura.Motivo);
        Assert.Contains("total_cantidad", lectura.Detalle);
    }

    // ------------------------------------------------------------------
    // Ranura (RANURA-PR1): descuento de cabecera KO_FALZ
    // ------------------------------------------------------------------

    [Fact]
    public void Construir_ConRanura_LaTransfiereBrutaAlContrato()
    {
        // La ranura NO se netea (a diferencia de precio/descuento por línea):
        // la NC que la documenta se desglosa con la tasa al emitirla.
        var datos = Construir(
            Cabecera(ivaPorcentaje: 16m, ranura: 250.50m), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal(250.50m, datos!.Ranura);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void Construir_SinRanuraOCero_DejaNull(string? crudo)
    {
        var ranura = crudo is null
            ? (decimal?)null
            : decimal.Parse(crudo, System.Globalization.CultureInfo.InvariantCulture);
        var datos = Construir(
            Cabecera(ranura: ranura), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Null(datos!.Ranura);
    }

    [Fact]
    public void Lectura_RanuraNegativa_EsDatoInvalido_NoConfig()
    {
        var lectura = AwSolicitudesSqlReader.Construir(
            Cabecera(ranura: -10m), new[] { Linea() },
            Array.Empty<ComponenteRow>(), SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.Null(lectura.Datos);
        Assert.False(lectura.EsperaConfiguracion);
        Assert.Contains("ranura", lectura.Detalle);
    }

    [Fact]
    public void Construir_MapeosCaseInsensitive()
    {
        // Cubre tanto el override del diccionario (llave en otro casing) como
        // el Enum.TryParse ignoreCase (clase con nombre de enum en otro casing).
        var datos = Construir(
            Cabecera(clase: "mostradorinmediato"),
            new[] { Linea() }, Array.Empty<ComponenteRow>(),
            SucursalId, CanalVentaId, OpcionesCompletas(), NullLogger.Instance);

        Assert.NotNull(datos);
        Assert.Equal(ComportamientoFiscal.MostradorInmediato, datos!.ComportamientoFiscal);
    }
}
