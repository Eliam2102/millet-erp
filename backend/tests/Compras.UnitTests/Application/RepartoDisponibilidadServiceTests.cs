using Millet.Compras.Application.Disponibilidad;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Application;

/// <summary>
/// Pruebas del reparto por línea compartido (<see cref="RepartoDisponibilidadService"/>):
/// cobertura total de almacén, descuento acumulado entre líneas del mismo
/// artículo y regla de servicio (sin consultar stock).
/// </summary>
public sealed class RepartoDisponibilidadServiceTests
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000002");
    private static readonly Guid Sucursal = Guid.CreateVersion7();
    private static readonly Guid Articulo = Guid.CreateVersion7();
    private static readonly Guid Servicio = Guid.CreateVersion7();

    [Fact]
    public async Task Demo19Oct_10_TreintaSolicitadas_DoceDisponibles_DieciochoPorComprar()
    {
        var rq = CrearRequisicion(Clasificacion.MateriaPrima, (Articulo, 30m, 100m));
        var r = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 12m)), new ConversionIdentidad(), Articulos(),
            Clasificacion.MateriaPrima, Sucursal, rq.Lineas, default);
        Assert.Equal(12m, r[0].Disponible);
        Assert.Equal(12m, r[0].DeAlmacen);
        Assert.Equal(18m, r[0].DeCompra);
    }

    [Fact]
    public async Task Almacen_Cubre_Todo_Deja_DeCompra_En_Cero()
    {
        var requisicion = CrearRequisicion(
            Clasificacion.MateriaPrima,
            (Articulo, 2m, 100m));

        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 10m)), new ConversionIdentidad(), Articulos(), Clasificacion.MateriaPrima,
            Sucursal, requisicion.Lineas, default);

        Assert.Equal(2m, repartos[0].DeAlmacen);
        Assert.Equal(0m, repartos[0].DeCompra);
        Assert.False(repartos[0].EsServicio);
    }

    [Fact]
    public async Task Descuenta_Stock_Acumulado_Entre_Lineas_Del_Mismo_Articulo()
    {
        var requisicion = CrearRequisicion(
            Clasificacion.MateriaPrima,
            (Articulo, 2m, 100m),
            (Articulo, 2m, 100m));

        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 3m)), new ConversionIdentidad(), Articulos(), Clasificacion.MateriaPrima,
            Sucursal, requisicion.Lineas, default);

        Assert.Equal((2m, 0m), (repartos[0].DeAlmacen, repartos[0].DeCompra));
        Assert.Equal((1m, 1m), (repartos[1].DeAlmacen, repartos[1].DeCompra));
    }

    [Fact]
    public async Task Linea_De_Articulo_Servicio_Va_Completa_A_Compra_Sin_Consultar_Stock()
    {
        var requisicion = CrearRequisicion(
            Clasificacion.MateriaPrima,
            (Servicio, 10m, 100m));
        var stock = new StockFalso((Servicio, 100m));

        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            stock, new ConversionIdentidad(), Articulos(Servicio), Clasificacion.MateriaPrima,
            Sucursal, requisicion.Lineas, default);

        Assert.Empty(stock.Consultas);
        Assert.Equal((0m, 10m, 0m), (repartos[0].DeAlmacen, repartos[0].DeCompra, repartos[0].Disponible));
        Assert.True(repartos[0].EsServicio);
    }

    [Fact]
    public async Task Rq_De_Clasificacion_Servicio_Va_Completa_A_Compra_Sin_Consultar_Stock()
    {
        var requisicion = CrearRequisicion(
            Clasificacion.Servicio,
            (Articulo, 10m, 100m));
        var stock = new StockFalso((Articulo, 100m));

        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            stock, new ConversionIdentidad(), Articulos(), Clasificacion.Servicio,
            Sucursal, requisicion.Lineas, default);

        Assert.Empty(stock.Consultas);
        Assert.Equal((0m, 10m), (repartos[0].DeAlmacen, repartos[0].DeCompra));
        Assert.True(repartos[0].EsServicio);
    }

    [Fact]
    public async Task Consulta_Stock_Una_Sola_Vez_Por_Requisicion()
    {
        var otro = Guid.CreateVersion7();
        var requisicion = CrearRequisicion(
            Clasificacion.MateriaPrima,
            (Articulo, 1m, 100m),
            (Articulo, 1m, 100m),
            (otro, 1m, 100m));
        var stock = new StockLoteContador();

        await RepartoDisponibilidadService.RepartirAsync(
            stock, new ConversionIdentidad(), Articulos(), Clasificacion.MateriaPrima,
            Sucursal, requisicion.Lineas, default);

        Assert.Equal(1, stock.LlamadasLote);
        Assert.Equal(0, stock.LlamadasUnitarias);
        Assert.Equal(new[] { Articulo, otro }.OrderBy(x => x), stock.IdsDelLote.OrderBy(x => x));
    }

    [Fact]
    public async Task Rq_Solo_De_Servicios_No_Consulta_Stock()
    {
        var requisicion = CrearRequisicion(Clasificacion.Servicio, (Articulo, 1m, 100m));
        var stock = new StockLoteContador();

        await RepartoDisponibilidadService.RepartirAsync(
            stock, new ConversionIdentidad(), Articulos(), Clasificacion.Servicio,
            Sucursal, requisicion.Lineas, default);

        Assert.Equal(0, stock.LlamadasLote);
        Assert.Equal(0, stock.LlamadasUnitarias);
    }

    [Fact]
    public async Task Compra_mixta_reserva_silicon_y_compra_montacargas_sin_consumir_stock()
    {
        var rq = CrearRequisicion(Clasificacion.MateriaPrima, (Articulo, 10m, 100m));
        var clase = Guid.NewGuid();
        var activo = rq.AgregarLinea(Guid.NewGuid(), Servicio, 1, "PZA", new Money(500000, "MXN"), esActivoFijo: true, claseActivoId: clase);
        var stock = new StockFalso((Articulo, 10m), (Servicio, 5m));
        var r = await RepartoDisponibilidadService.RepartirAsync(stock, new ConversionIdentidad(), Articulos(), Clasificacion.MateriaPrima, Sucursal, rq.Lineas, default);
        Assert.Equal(10, r[0].DeAlmacen);
        Assert.Equal(0, r[1].DeAlmacen);
        Assert.Equal(1, r[1].DeCompra);
        Assert.False(r[1].EsServicio);
        Assert.DoesNotContain(Servicio, stock.Consultas);
        Assert.True(activo.EsActivoFijo); Assert.Equal(clase, activo.ClaseActivoId);
        Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => rq.AgregarLinea(Guid.NewGuid(), Servicio, 0.5m, "PZA", new Money(10, "MXN"), esActivoFijo: true, claseActivoId: clase));
    }

    [Theory]
    [InlineData(5, 3, false, 3, 2)]
    [InlineData(5, 3, true, 0, 5)]
    [InlineData(3, 5, true, 3, 0)]
    [InlineData(5, 5, true, 5, 0)]
    public async Task Surtido_completo_reparte_por_linea(int pide, int hay, bool completo, int deAlmacen, int deCompra)
    {
        var rq = CrearRequisicion(Clasificacion.MateriaPrima, (Articulo, pide, 100m));
        var r = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, hay)), new ConversionIdentidad(), Articulos(),
            Clasificacion.MateriaPrima, Sucursal, rq.Lineas, default, soloCompleto: completo);
        Assert.Equal(deAlmacen, r[0].DeAlmacen);
        Assert.Equal(deCompra, r[0].DeCompra);
    }

    [Fact]
    public async Task Surtido_completo_decide_por_linea_y_no_descuenta_la_linea_rechazada()
    {
        var rq = CrearRequisicion(Clasificacion.MateriaPrima, (Articulo, 5m, 100m), (Articulo, 3m, 100m));
        var r = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 3m)), new ConversionIdentidad(), Articulos(),
            Clasificacion.MateriaPrima, Sucursal, rq.Lineas, default, soloCompleto: true);
        Assert.Equal(0m, r[0].DeAlmacen);
        Assert.Equal(5m, r[0].DeCompra);
        Assert.Equal(3m, r[1].DeAlmacen);
        Assert.Equal(0m, r[1].DeCompra);
    }

    [Fact]
    public async Task Surtido_completo_no_afecta_lineas_de_servicio()
    {
        var rq = CrearRequisicion(Clasificacion.Servicio, (Articulo, 2m, 100m));
        var r = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 9m)), new ConversionIdentidad(), Articulos(),
            Clasificacion.Servicio, Sucursal, rq.Lineas, default, soloCompleto: true);
        Assert.Equal(0m, r[0].DeAlmacen);
        Assert.Equal(2m, r[0].DeCompra);
        Assert.True(r[0].EsServicio);
    }

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(2, 2, 0)]
    public async Task Surtido_completo_con_factor_de_conversion_compara_en_unidad_de_documento(int cajas, int deAlmacen, int deCompra)
    {
        // Línea en cajas de 12 piezas; 30 piezas base = 2.5 cajas: no alcanza para 3, sí para 2.
        var rq = CrearRequisicion(Clasificacion.MateriaPrima, (Articulo, cajas, 100m));
        var r = await RepartoDisponibilidadService.RepartirAsync(
            new StockFalso((Articulo, 30m)), new ConversionFactor(12m), Articulos(),
            Clasificacion.MateriaPrima, Sucursal, rq.Lineas, default, soloCompleto: true);
        Assert.Equal(deAlmacen, r[0].DeAlmacen);
        Assert.Equal(deCompra, r[0].DeCompra);
    }

    private static Dictionary<Guid, ArticuloLectura> Articulos(params Guid[] servicios) =>
        new Dictionary<Guid, ArticuloLectura>
        {
            [Articulo] = new(Articulo, "ART-01", "Articulo", EsServicio: false),
            [Servicio] = new(Servicio, "SVC-01", "Servicio", EsServicio: servicios.Contains(Servicio)),
        };

    private static Requisicion CrearRequisicion(
        Clasificacion clasificacion,
        params (Guid ArticuloId, decimal Cantidad, decimal Precio)[] lineas)
    {
        var requisicion = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: Empresa,
            folio: Folio.Parse("MID2026-000002"),
            folioAnio: 2026,
            clasificacion: clasificacion,
            sucursalId: Sucursal,
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: "Test reparto");
        foreach (var (articuloId, cantidad, precio) in lineas)
        {
            requisicion.AgregarLinea(Guid.CreateVersion7(), articuloId, cantidad, "PZA", new Money(precio, "MXN"));
        }

        return requisicion;
    }

    private sealed class StockFalso(params (Guid ArticuloId, decimal Disponible)[] existencias) : IConsultarStockPort
    {
        public List<Guid> Consultas { get; } = new();

        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken)
        {
            Consultas.Add(articuloId);
            var disponible = existencias.FirstOrDefault(e => e.ArticuloId == articuloId).Disponible;
            return Task.FromResult(new DisponibilidadStock(OnHand: disponible, Disponible: disponible));
        }
    }

    private sealed class StockLoteContador : IConsultarStockPort
    {
        public int LlamadasUnitarias { get; private set; }

        public int LlamadasLote { get; private set; }

        public List<Guid> IdsDelLote { get; } = new();

        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken)
        {
            LlamadasUnitarias++;
            return Task.FromResult(new DisponibilidadStock(0m, 0m));
        }

        public Task<IReadOnlyDictionary<Guid, DisponibilidadStock>> ConsultarPorSucursalLoteAsync(
            Guid sucursalId, IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
        {
            LlamadasLote++;
            IdsDelLote.AddRange(articuloIds);
            return Task.FromResult<IReadOnlyDictionary<Guid, DisponibilidadStock>>(
                articuloIds.ToDictionary(id => id, _ => new DisponibilidadStock(0m, 0m)));
        }
    }

    private sealed class ConversionFactor(decimal factor) : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(
            Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad * factor, "PZA", cantidad, factor, unidadDocumento, cantidad));
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(
            Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, unidadDocumento, cantidad, 1m, unidadDocumento, cantidad));
    }
}
