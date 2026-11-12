using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Adapters;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// PCV (86e3ke10b) · póliza de costo de ventas de la entrega de PT: lectura de <c>producto_terminado.entrega.confirmada.v1</c> (payload
/// como el outbox, PascalCase), costo por puerto, dato faltante sin valuación (PT5.12) y cuadre de la plantilla sembrada. Datos FIX.
/// </summary>
public class CostoVentasPtTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Pedido = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    /// <summary>Mismo shape que serializa el outbox de <c>EntregaConfirmadaEvent</c> (base + parámetros posicionales).</summary>
    private static string Entrega(string albaran = "ALB-FIX-1", int rechazadas = 0, (int Posicion, int Piezas)[]? entregadas = null) =>
        JsonSerializer.Serialize(new
        {
            EventType = N.EntregaPtConfirmada, EmpresaId = Empresa, OcurridoEn = Ocurrido,
            Empresa, Fecha = Ocurrido, PedidoPtId = Pedido, NumeroPedido = "PED-FIX-1", HojaSalidaFolio = "HS-FIX-1", AlbaranFolio = albaran,
            Resultado = rechazadas == 0 ? "Completa" : "Parcial", Receptor = "Receptor FIX",
            Entregadas = (entregadas ?? []).Select(e => new { e.Posicion, e.Piezas }).ToArray(), Rechazadas = rechazadas, PedidoCompleto = rechazadas == 0,
        });

    private static EventoContable Normalizar(string payload, Guid? eventoId = null) => N.Normalizar(N.EntregaPtConfirmada, eventoId ?? Guid.NewGuid(), payload);

    /// <summary>Valuación fija por posición (costo unitario) que registra lo que se le pidió.</summary>
    private sealed class ValuacionFija(Dictionary<int, decimal> unitario, string? tipoProducto = null) : ICostoProductoTerminadoReadPort
    {
        public IReadOnlyList<PiezasPosicionPt>? Pedidas { get; private set; }
        public Task<CostoEntregaPt?> CostoEntregaAsync(Guid empresaId, Guid pedidoPtId, IReadOnlyList<PiezasPosicionPt> entregadas, CancellationToken ct)
        {
            Pedidas = entregadas;
            return Task.FromResult<CostoEntregaPt?>(new CostoEntregaPt(Sucursal,
                [.. entregadas.Where(p => unitario.ContainsKey(p.Posicion)).Select(p => new CostoPosicionPt(p.Posicion, unitario[p.Posicion] * p.Piezas, tipoProducto))]));
        }
    }

    [Fact]
    public void La_entrega_se_normaliza_contra_el_pedido_con_llave_por_albaran()
    {
        var e = Normalizar(Entrega(entregadas: [(1, 30)]));

        e.Tipo.Should().Be(N.EntregaPtConfirmada);
        e.EmpresaId.Should().Be(Empresa);
        e.Documento.Modulo.Should().Be("producto_terminado");
        e.Documento.Tipo.Should().Be("entrega");
        e.Documento.Id.Should().Be(Pedido);
        e.Llave.Should().Be(N.LlaveEntregaPt(Pedido, "ALB-FIX-1"));
        e.Moneda.Should().Be("MXN");
        // Sin costo todavía: no es «sin movimiento», va al motor para que el enriquecedor ponga el costo o lo mande a la bandeja.
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void La_misma_entrega_dos_veces_tiene_la_misma_llave_y_otro_albaran_del_pedido_otra()
    {
        var a = Normalizar(Entrega(entregadas: [(1, 20)]), Guid.NewGuid());
        var b = Normalizar(Entrega(entregadas: [(1, 20)]), Guid.NewGuid());
        var c = Normalizar(Entrega("ALB-FIX-2", entregadas: [(1, 10)]), Guid.NewGuid());

        a.Llave.Should().Be(b.Llave);
        c.Llave.Should().NotBe(a.Llave);
    }

    [Fact]
    public void Rechazo_total_en_sitio_no_mueve_inventario()
    {
        var e = Normalizar(Entrega(rechazadas: 30, entregadas: [(1, 0)]));
        N.SinMovimiento(e).Should().BeTrue();
    }

    [Fact]
    public async Task Sin_valuacion_de_PT_va_a_la_bandeja_como_dato_faltante()
    {
        var e = Normalizar(Entrega(entregadas: [(1, 30)]));

        var act = () => new EnriquecedorCostoVentasPt(new CostoProductoTerminadoSinValuacionAdapter()).EnriquecerAsync(e, default);

        var ex = (await act.Should().ThrowAsync<RechazoMotorException>()).Which;
        ex.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
        ex.Detalle.Should().Be(EnriquecedorCostoVentasPt.CampoDatoFaltante);
        ex.Message.Should().StartWith("Dato faltante: costo de PT").And.Contain("PED-FIX-1");
    }

    [Fact]
    public async Task PCVa_entrega_con_costo_de_MP_de_6000_da_costo_de_ventas_por_6000()
    {
        var puerto = new ValuacionFija(new() { [1] = 200m });
        var e = await new EnriquecedorCostoVentasPt(puerto).EnriquecerAsync(Normalizar(Entrega(entregadas: [(1, 30)])), default);

        e.SucursalId.Should().Be(Sucursal);
        e.Importes["Total"].Should().Be(6_000m);
        e.Lineas.Should().ContainSingle().Which.Importe.Should().Be(6_000m);
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public async Task PCVa2_entrega_parcial_de_20_de_30_lleva_el_costo_solo_de_las_20()
    {
        var puerto = new ValuacionFija(new() { [1] = 200m });
        var e = await new EnriquecedorCostoVentasPt(puerto).EnriquecerAsync(Normalizar(Entrega(rechazadas: 10, entregadas: [(1, 20)])), default);

        puerto.Pedidas.Should().BeEquivalentTo(new[] { new PiezasPosicionPt(1, 20) });
        e.Importes["Total"].Should().Be(4_000m);
    }

    [Fact]
    public async Task Varias_posiciones_dan_una_linea_por_posicion_con_su_tipo_de_producto()
    {
        var puerto = new ValuacionFija(new() { [1] = 100m, [2] = 50.505m }, tipoProducto: "Templado");
        var e = await new EnriquecedorCostoVentasPt(puerto).EnriquecerAsync(Normalizar(Entrega(entregadas: [(2, 2), (1, 3), (3, 0)])), default);

        puerto.Pedidas!.Select(p => p.Posicion).Should().Equal(1, 2); // sin la posición con 0 piezas
        e.Lineas.Select(l => l.Importe).Should().Equal(300m, 101.01m);
        e.Lineas.Should().OnlyContain(l => l.Clasificador == "Templado");
        e.Importes["Total"].Should().Be(401.01m);
    }

    [Fact]
    public async Task Una_posicion_sin_costo_tambien_es_dato_faltante()
    {
        var puerto = new ValuacionFija(new() { [1] = 100m });
        var act = () => new EnriquecedorCostoVentasPt(puerto).EnriquecerAsync(Normalizar(Entrega(entregadas: [(1, 3), (2, 1)])), default);

        var ex = (await act.Should().ThrowAsync<RechazoMotorException>()).Which;
        ex.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
        ex.Message.Should().Contain("posición 2");
    }

    [Fact]
    public async Task Otros_eventos_no_se_tocan()
    {
        var otro = N.Normalizar("almacen.no-contable.v1", Guid.NewGuid(), JsonSerializer.Serialize(new { EmpresaId = Empresa, OcurridoEn = Ocurrido }));
        var e = await new EnriquecedorCostoVentasPt(new ValuacionFija(new())).EnriquecerAsync(otro, default);
        e.Should().BeSameAs(otro);
    }

    [Fact]
    public async Task La_plantilla_sembrada_cuadra_y_usa_costo_de_ventas_por_tipo_contra_inventario_PT()
    {
        var e = await new EnriquecedorCostoVentasPt(new ValuacionFija(new() { [1] = 200m, [2] = 10m }))
            .EnriquecerAsync(Normalizar(Entrega(entregadas: [(1, 20), (2, 5)])), default);
        var plantilla = PlantillaSembrada();

        decimal Importe(LineaPlantillaContable l) => l.PorLinea && e.Lineas.Count > 0 ? e.Lineas.Sum(x => x.Importe) : e.Importes[l.Campo]!.Value;
        var cargo = plantilla.Single(l => l.Lado == LadoPartida.Cargo);
        var abono = plantilla.Single(l => l.Lado == LadoPartida.Abono);

        cargo.Should().Match<LineaPlantillaContable>(l => l.Rol == "COSTO_DE_VENTAS" && l.PorLinea && l.RolPorClasificador);
        abono.Rol.Should().Be("INVENTARIO_PT");
        Importe(cargo).Should().Be(4_050m);
        Importe(cargo).Should().Be(Importe(abono));
        Migracion().Should().Contain("'COSTO_DE_VENTAS'").And.Contain("'INVENTARIO_PT'").And.Contain("'SM'");
    }

    [Fact]
    public void El_oyente_de_PT_contabiliza_la_entrega_confirmada_y_los_traspasos()
    {
        var w = new Millet.Contabilidad.Infrastructure.Workers.ProductoTerminadoContabilidadListenerWorker(null, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Contabilidad.Infrastructure.Workers.ProductoTerminadoContabilidadListenerWorker>.Instance);

        w.Topic.Should().Be("producto-terminado-events");
        w.TiposContables.Should().BeEquivalentTo(new[] { N.EntregaPtConfirmada, N.TraspasoPtSalidaAutorizada, N.TraspasoPtEntradaRegistrada });
    }

    private static string Migracion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        return File.ReadAllText(Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_PcvPlantillaCostoVentas.cs").Single());
    }

    private static readonly JsonSerializerOptions OpcionesPlantilla = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static List<LineaPlantillaContable> PlantillaSembrada()
    {
        var m = Regex.Match(Migracion(), @"\('producto_terminado\.entrega\.confirmada\.v1', 'SM', '[^']*', '[^']*', '(?<lineas>\[[^']*\])'\)");
        m.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, OpcionesPlantilla)!;
    }
}
