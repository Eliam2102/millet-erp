using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Adapters;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-249 · póliza del traspaso de PT entre sucursales: la salida carga la cuenta puente y abona el inventario del origen, la entrada hace lo contrario
/// en la sucursal donde queda la pieza, así el puente se salda en cero. Payload como el del outbox (PascalCase). Datos FIX.
/// </summary>
public class TraspasoPtContabilidadTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Traspaso = Guid.NewGuid(), Origen = Guid.NewGuid(), Destino = Guid.NewGuid(), Pedido1 = Guid.NewGuid(), Pedido2 = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private static readonly (Guid Pedido, string Numero, (int Posicion, int Piezas)[] Posiciones)[] Dos =
        [(Pedido1, "PED-FIX-1", [(1, 4), (2, 1)]), (Pedido2, "PED-FIX-2", [(1, 5)])];

    private static Dictionary<string, object>[] Pedidos((Guid Pedido, string Numero, (int Posicion, int Piezas)[] Posiciones)[] p) =>
        [.. p.Select(x => new Dictionary<string, object>
        {
            ["PedidoPtId"] = x.Pedido, ["NumeroPedido"] = x.Numero, ["Posiciones"] = x.Posiciones.Select(y => new { y.Posicion, y.Piezas }).ToArray(),
        })];

    private static string Salida() => JsonSerializer.Serialize(new
    {
        EventType = N.TraspasoPtSalidaAutorizada, EmpresaId = Empresa, OcurridoEn = Ocurrido, Empresa, Fecha = Ocurrido, TraspasoId = Traspaso, Folio = "TR-FIX-1",
        SucursalOrigenId = Origen, SucursalDestinoId = Destino, Pedidos = Pedidos(Dos),
    });

    private static string Entrada(Guid sucursal, string resultado = "Recepcion", (Guid, string, (int, int)[])[]? pedidos = null) => JsonSerializer.Serialize(new
    {
        EventType = N.TraspasoPtEntradaRegistrada, EmpresaId = Empresa, OcurridoEn = Ocurrido, Empresa, Fecha = Ocurrido, TraspasoId = Traspaso, Folio = "TR-FIX-1",
        Resultado = resultado, SucursalId = sucursal, Pedidos = Pedidos(pedidos ?? Dos),
    });

    /// <summary>Costo unitario por pedido y posición; registra lo que se le pidió. Siempre responde con la sucursal del pedido (que el traspaso no usa).</summary>
    private sealed class ValuacionFija(Dictionary<(Guid, int), decimal> unitario) : ICostoProductoTerminadoReadPort
    {
        public List<(Guid Pedido, IReadOnlyList<PiezasPosicionPt> Piezas)> Pedidas { get; } = [];
        public Task<CostoEntregaPt?> CostoEntregaAsync(Guid empresaId, Guid pedidoPtId, IReadOnlyList<PiezasPosicionPt> entregadas, CancellationToken ct)
        {
            Pedidas.Add((pedidoPtId, entregadas));
            return Task.FromResult<CostoEntregaPt?>(new CostoEntregaPt(Guid.NewGuid(),
                [.. entregadas.Where(p => unitario.ContainsKey((pedidoPtId, p.Posicion))).Select(p => new CostoPosicionPt(p.Posicion, unitario[(pedidoPtId, p.Posicion)] * p.Piezas))]));
        }
    }

    private static readonly Dictionary<(Guid, int), decimal> Costos = new() { [(Pedido1, 1)] = 100m, [(Pedido1, 2)] = 50m, [(Pedido2, 1)] = 10m };

    [Fact]
    public void La_salida_va_a_la_sucursal_origen_y_la_entrada_a_la_que_recibe()
    {
        var salida = N.Normalizar(N.TraspasoPtSalidaAutorizada, Guid.NewGuid(), Salida());
        var entrada = N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), Entrada(Origen, "RegresoOrigen"));

        salida.SucursalId.Should().Be(Origen);
        entrada.SucursalId.Should().Be(Origen);
        salida.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "producto_terminado" && d.Tipo == "traspaso" && d.Id == Traspaso && d.Folio == "TR-FIX-1");
        salida.Moneda.Should().Be("MXN");
        N.SinMovimiento(salida).Should().BeFalse(); // sin costo todavía: el enriquecedor decide
    }

    [Fact]
    public async Task El_costo_del_traspaso_suma_las_piezas_de_cada_pedido_y_posicion()
    {
        var puerto = new ValuacionFija(Costos);
        var e = await new EnriquecedorCostoTraspasoPt(puerto).EnriquecerAsync(N.Normalizar(N.TraspasoPtSalidaAutorizada, Guid.NewGuid(), Salida()), default);

        // 4 x 100 + 1 x 50 del pedido 1, 5 x 10 del pedido 2.
        e.Importes["Total"].Should().Be(500m);
        puerto.Pedidas.Select(p => p.Pedido).Should().Equal(Pedido1, Pedido2);
        e.SucursalId.Should().Be(Origen);
    }

    [Fact]
    public async Task Una_pieza_que_regresa_al_origen_entra_ahi_por_su_costo_y_salda_el_puente()
    {
        var puerto = new ValuacionFija(Costos);
        var salida = await new EnriquecedorCostoTraspasoPt(puerto).EnriquecerAsync(N.Normalizar(N.TraspasoPtSalidaAutorizada, Guid.NewGuid(), Salida()), default);
        // Llegan 8 de 10 a destino y las otras 2 (una por pedido) regresan al origen.
        var recibidas = await new EnriquecedorCostoTraspasoPt(puerto).EnriquecerAsync(
            N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), Entrada(Destino, pedidos: [(Pedido1, "PED-FIX-1", [(1, 3), (2, 1)]), (Pedido2, "PED-FIX-2", [(1, 4)])])), default);
        var regreso1 = await new EnriquecedorCostoTraspasoPt(puerto).EnriquecerAsync(
            N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), Entrada(Origen, "RegresoOrigen", [(Pedido1, "PED-FIX-1", [(1, 1)])])), default);
        var regreso2 = await new EnriquecedorCostoTraspasoPt(puerto).EnriquecerAsync(
            N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), Entrada(Origen, "RegresoOrigen", [(Pedido2, "PED-FIX-2", [(1, 1)])])), default);

        recibidas.SucursalId.Should().Be(Destino);
        regreso1.SucursalId.Should().Be(Origen);
        (recibidas.Importes["Total"]!.Value + regreso1.Importes["Total"]!.Value + regreso2.Importes["Total"]!.Value).Should().Be(salida.Importes["Total"]!.Value);
    }

    [Fact]
    public async Task Sin_valuacion_o_con_una_posicion_sin_costo_es_dato_faltante_y_nunca_cero()
    {
        var sin = () => new EnriquecedorCostoTraspasoPt(new CostoProductoTerminadoSinValuacionAdapter()).EnriquecerAsync(N.Normalizar(N.TraspasoPtSalidaAutorizada, Guid.NewGuid(), Salida()), default);
        var ex = (await sin.Should().ThrowAsync<RechazoMotorException>()).Which;
        ex.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
        ex.Message.Should().StartWith("Dato faltante: costo de PT").And.Contain("PED-FIX-1").And.Contain("TR-FIX-1");

        var parcial = () => new EnriquecedorCostoTraspasoPt(new ValuacionFija(new() { [(Pedido1, 1)] = 100m, [(Pedido2, 1)] = 10m }))
            .EnriquecerAsync(N.Normalizar(N.TraspasoPtSalidaAutorizada, Guid.NewGuid(), Salida()), default);
        (await parcial.Should().ThrowAsync<RechazoMotorException>()).Which.Message.Should().Contain("posición 2");
    }

    [Fact]
    public void Un_evento_sin_piezas_sale_sin_movimiento()
    {
        var e = N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), Entrada(Destino, pedidos: [(Pedido1, "PED-FIX-1", [(1, 0)])]));
        N.SinMovimiento(e).Should().BeTrue();
    }

    [Fact]
    public void Un_evento_sin_sucursal_o_sin_folio_es_ilegible()
    {
        var sinSucursal = JsonSerializer.Serialize(new { EmpresaId = Empresa, OcurridoEn = Ocurrido, TraspasoId = Traspaso, Folio = "TR-FIX-1", Pedidos = Array.Empty<object>() });
        var f = () => N.Normalizar(N.TraspasoPtEntradaRegistrada, Guid.NewGuid(), sinSucursal);
        f.Should().Throw<EventoIlegibleException>();
    }

    [Fact]
    public void Las_plantillas_sembradas_cuadran_y_el_puente_se_salda_en_cero()
    {
        var salida = Plantilla("salida-autorizada");
        var entrada = Plantilla("entrada-registrada");

        salida.Single(l => l.Lado == LadoPartida.Cargo).Rol.Should().Be("INVENTARIO_POR_TRASPASO");
        salida.Single(l => l.Lado == LadoPartida.Abono).Rol.Should().Be("INVENTARIO_PT");
        entrada.Single(l => l.Lado == LadoPartida.Cargo).Rol.Should().Be("INVENTARIO_PT");
        entrada.Single(l => l.Lado == LadoPartida.Abono).Rol.Should().Be("INVENTARIO_POR_TRASPASO");
        salida.Concat(entrada).Should().OnlyContain(l => l.Campo == "Total" && !l.PorLinea);
        Migracion().Should().Contain("115.05.00.00").And.Contain("115.05.01.00").And.Contain("INVENTARIO_EN_PRESTAMO");
    }

    private static string Migracion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        return File.ReadAllText(Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_TraspasoPtCuentaPuente.cs").Single());
    }

    private static readonly JsonSerializerOptions OpcionesPlantilla = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static List<LineaPlantillaContable> Plantilla(string evento)
    {
        var m = Regex.Match(Migracion(), @"\('producto_terminado\.traspaso\." + evento + @"\.v1', '[A-Z]+', '[^']*', '[^']*',\s*'(?<lineas>\[[^']*\])'\)");
        m.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, OpcionesPlantilla)!;
    }
}
