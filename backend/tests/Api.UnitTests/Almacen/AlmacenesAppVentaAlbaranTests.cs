using System.Text.Json;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Api.Endpoints.Almacen;
using Millet.Almacen.Application.Integration;
using Millet.Contabilidad.Application.Motor;

namespace Millet.Api.UnitTests.Almacen;

/// <summary>
/// VENTA_ALBARAN (contrato v10-v11, p.5): la venta de un pedido de A+W baja la materia prima del almacén del que salió cada hoja, al
/// costo promedio del ERP (el mensaje no trae costo), y carga costo de ventas. El ejemplo es el del contrato.
/// </summary>
public sealed class AlmacenesAppVentaAlbaranTests
{
    private const string Sobre = """
        {
          "mensajeId": "venta-1", "tipo": "VENTA_ALBARAN", "version": 1, "fecha": "2026-11-13T10:00:00-06:00",
          "origen": "AlmacenRQ_MP", "referencia": "MV-0006455",
          "datos": {
            "movimiento": "MV-0006455", "pedidoAw": 90064863, "albaran": 90054120, "sucursal": "CONKAL", "piezas": 3,
            "almacenes": [
              { "almacenGeneral": { "id": 4, "nombre": "Conkal obras", "tipo": "OBRAS", "linea": "HEGLA 1" }, "piezas": 3,
                "grupos": [ { "articuloAw": 100006, "ancho": 1200.00, "alto": 2600.00, "lote": "L-1", "pedimento": null,
                              "etiqueta": null, "piezas": 3, "m2": 9.3600 } ] } ]
          }
        }
        """;

    private static JsonElement Datos(string sobre = Sobre) =>
        JsonDocument.Parse(sobre).RootElement.GetProperty("datos").Clone();

    [Fact]
    public async Task Baja_la_existencia_del_almacen_del_que_salio_y_deja_el_pedido_y_el_albaran_en_el_motivo()
    {
        var (r, error) = await AlmacenesAppSalida.ManejarAsync(
            AlmacenesAppSalida.VentaAlbaran, Datos(), new Resolver(), new UsuarioServicio(), default);

        Assert.Null(error);
        var s = Assert.Single(r!.Salidas);
        Assert.Equal(TipoMovimiento.SalidaPorVenta, s.Tipo);
        // El pedido y el albarán quedan en el movimiento: con ellos se rastrea la venta y su devolución.
        Assert.Equal("App Almacenes VENTA_ALBARAN MV-0006455 pedido 90064863 albarán 90054120", s.Motivo);
        Assert.Equal(new LlaveAlmacenApp("OBRAS", "HEGLA 1"), s.Almacen);
        Assert.Equal("L-1", s.Lote);
        // 3 hojas de 1200 × 2600 = 9.36 m²; el mensaje no trae costo, así que sale al promedio del ERP.
        Assert.Equal(9.36m, s.Cantidad);
        Assert.Null(s.CostoUnitarioMxn);
        Assert.Null(s.CentroCostoId);
    }

    [Fact]
    public async Task Sin_almacenes_da_ERROR_sin_mover_nada()
    {
        var (r, error) = await AlmacenesAppSalida.ManejarAsync(
            AlmacenesAppSalida.VentaAlbaran, Datos(Sobre.Replace("\"almacenes\"", "\"otro\"")),
            new Resolver(), new UsuarioServicio(), default);

        Assert.Null(r);
        Assert.Contains("almacenes", error);
    }

    [Fact]
    public async Task Un_m2_que_no_cuadra_con_las_hojas_da_ERROR()
    {
        var (r, error) = await AlmacenesAppSalida.ManejarAsync(
            AlmacenesAppSalida.VentaAlbaran, Datos(Sobre.Replace("\"m2\": 9.3600", "\"m2\": 12.5")),
            new Resolver(), new UsuarioServicio(), default);

        Assert.Null(r);
        Assert.Contains("no coincide con piezas", error);
    }

    [Fact]
    public void La_venta_carga_costo_de_ventas_y_la_devolucion_lo_reversa()
    {
        // La causa que publica el movimiento de venta...
        Assert.Equal("venta", SalidaInventarioRegistradaIntegrationEvent.CausaPorDefecto(TipoMovimiento.SalidaPorVenta));
        // ...cae en el campo CostoVentas de la plantilla v4, no en Consumo (que es el default de las demás salidas).
        Assert.Equal("CostoVentas", NormalizadorEventos.CampoSalidaInventario("venta"));
        Assert.Equal("Consumo", NormalizadorEventos.CampoSalidaInventario("consumo"));
    }

    private sealed class Resolver : IAppSalidaResolverPort
    {
        public Task<ResolverSalidaResultado> ResolverAsync(string sucursalCodigo, IReadOnlyList<GrupoSalidaApp> grupos, CancellationToken ct) =>
            Task.FromResult(new ResolverSalidaResultado(null, Guid.NewGuid(), grupos.Select(g => g.AnchoMm is null
                ? new GrupoSalidaResuelto(Guid.NewGuid(), "PZA", null, null, null)
                : new GrupoSalidaResuelto(Guid.NewGuid(), "M2", Guid.NewGuid(), g.AnchoMm, g.AltoMm, AreaHoja.M2(g.AnchoMm.Value, g.AltoMm!.Value))).ToList()));
    }

    private sealed class UsuarioServicio : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<UsuarioServicioLectura?>(new(Guid.NewGuid(), Guid.NewGuid(), true));
    }
}
