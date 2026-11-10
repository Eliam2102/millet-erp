using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.UnitTests;

/// <summary>FAC-08 (86e3qdddf): contenido del evento de contenedor embarcado y compuerta por contenedor. Datos ficticios.</summary>
public sealed class ContenedorEventosTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 4);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();

    private static Expediente Nuevo() => new(Guid.NewGuid(), Guid.NewGuid(), "CE-EXP-DEMO", TipoExpediente.Exportacion, Sucursal, "DEMO",
        null, Guid.NewGuid(), "USD", Actor, ["DEMO cliente"]);

    private static PedidoEmbarcable Pedido(Expediente e, string numero, Guid cliente, params (int Pos, decimal Cant)[] lineas)
    {
        if (!e.Participantes.Any(p => p.ClienteId == cliente))
            e.AgregarParticipante(Guid.NewGuid(), "DEMO cliente", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, cliente);
        return new(Guid.NewGuid(), numero, cliente, "DEMO cliente", Sucursal, true, "Importado", 1,
            lineas.Select(l => new LineaEmbarcable(l.Pos, null, "Vidrio DEMO", l.Cant, "H87")).ToArray(), null, null);
    }

    /// <summary>Desde #464 la salida se autoriza (otra persona, versión actual del embarque) antes de la fecha real.</summary>
    private static void Embarcar(Expediente e, Guid contenedor)
    {
        foreach (var partida in e.Partidas.Where(x => x.ContenedorId == contenedor && x.Estado != EstadoPartida.Liberada && x.FraccionArancelaria == null).ToArray())
            e.GuardarDatosAduanales(partida.Id, "7007199900", "06", Actor, Ahora, true);
        if (!e.SalidaAutorizada) e.AutorizarSalida(Guid.NewGuid(), Ahora, null, true);
        e.RegistrarEmbarque(contenedor, Hoy, Hoy);
    }

    private static Guid Contenedor(Expediente e, string numero)
    {
        var id = Guid.NewGuid();
        e.GuardarContenedor(id, new(numero, "S", "B", "DEMO-BL-" + numero, "R", Hoy, 10, 1));
        return id;
    }

    [Fact]
    public void Embarcar_arma_el_evento_con_las_lineas_del_contenedor_agrupadas_por_pedido()
    {
        var e = Nuevo(); var cliente = Guid.NewGuid();
        e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", "A1", Hoy, Hoy, "16");
        var a = Pedido(e, "DEMO-200", cliente, (1, 100), (2, 10));
        var b = Pedido(e, "DEMO-100", cliente, (1, 5));
        var c1 = Contenedor(e, "DEMO-C1"); var c2 = Contenedor(e, "DEMO-C2");
        e.AsignarPartida(c1, a, 1, 40, Actor);
        e.AsignarPartida(c1, a, 1, 20, Actor);   // misma posición en dos partidas: se suman
        e.AsignarPartida(c1, a, 2, 10, Actor);
        e.AsignarPartida(c1, b, 1, 5, Actor);
        e.AsignarPartida(c2, a, 1, 40, Actor);   // otro contenedor: no entra en el evento de c1
        Embarcar(e, c1);

        var ev = ContenedorEmbarcadoEvent.Desde(e, c1, [a, b], Ahora);

        Assert.Equal("comercio_exterior.contenedor.embarcado.v1", ev.EventType);
        Assert.Equal(e.EmpresaId, ev.EmpresaId);
        Assert.Equal((e.Id, "CE-EXP-DEMO", c1, "DEMO-C1", "DEMO-BL-DEMO-C1", Hoy),
            (ev.ExpedienteId, ev.FolioExpediente, ev.ContenedorId, ev.NumeroContenedor, ev.BlGuia, ev.FechaEmbarqueReal));
        Assert.Equal(["DEMO-100", "DEMO-200"], ev.Pedidos.Select(p => p.NumeroPedido));
        Assert.Equal(b.Id, ev.Pedidos[0].PedidoFacturableId);
        Assert.Equal([new LineaContenedorEvento(1, 5, "H87")], ev.Pedidos[0].Lineas);
        Assert.Equal(a.Id, ev.Pedidos[1].PedidoFacturableId);
        Assert.Equal([new LineaContenedorEvento(1, 60, "H87"), new LineaContenedorEvento(2, 10, "H87")], ev.Pedidos[1].Lineas);
    }

    [Fact]
    public void Pedido_en_dos_contenedores_60_y_40_da_dos_eventos_con_su_parte()
    {
        var e = Nuevo(); var cliente = Guid.NewGuid();
        e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", "A1", Hoy, Hoy, "16");
        var p = Pedido(e, "DEMO-300", cliente, (1, 100));
        var c1 = Contenedor(e, "DEMO-C1"); var c2 = Contenedor(e, "DEMO-C2");
        e.AsignarPartida(c1, p, 1, 60, Actor); e.AsignarPartida(c2, p, 1, 40, Actor);
        Embarcar(e, c1); Embarcar(e, c2);

        Assert.Equal(60, ContenedorEmbarcadoEvent.Desde(e, c1, [p], Ahora).Pedidos.Single().Lineas.Single().Cantidad);
        Assert.Equal(40, ContenedorEmbarcadoEvent.Desde(e, c2, [p], Ahora).Pedidos.Single().Lineas.Single().Cantidad);
    }

    [Fact]
    public void Cancelar_contenedor_embarcado_arma_el_evento_con_motivo_y_pedidos()
    {
        var e = Nuevo(); var cliente = Guid.NewGuid();
        e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", "A1", Hoy, Hoy, "16");
        var p = Pedido(e, "DEMO-300", cliente, (1, 100));
        var c1 = Contenedor(e, "DEMO-C1");
        e.AsignarPartida(c1, p, 1, 60, Actor); Embarcar(e, c1);
        e.CancelarContenedor(c1, "DEMO naviera canceló el booking", false);

        var ev = ContenedorCanceladoEvent.Desde(e, c1, [p], Ahora);

        Assert.Equal("comercio_exterior.contenedor.cancelado.v1", ev.EventType);
        Assert.Equal(("DEMO-C1", "DEMO naviera canceló el booking"), (ev.NumeroContenedor, ev.Motivo));
        Assert.Equal(p.Id, ev.Pedidos.Single().PedidoFacturableId);
    }

    [Fact]
    public void Compuerta_por_contenedor_libera_la_salida_embarcada_sin_esperar_el_100_del_pedido()
    {
        var e = Nuevo(); var cliente = Guid.NewGuid();
        e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", "A1", Hoy, Hoy, "16");
        var p = Pedido(e, "DEMO-300", cliente, (1, 100));
        var c1 = Contenedor(e, "DEMO-C1"); var c2 = Contenedor(e, "DEMO-C2");
        e.AsignarPartida(c1, p, 1, 60, Actor); e.AsignarPartida(c2, p, 1, 40, Actor);
        Embarcar(e, c1);

        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p).Codigo);          // pedido completo: falta el 100 %
        Assert.Null(e.BloqueoFacturacion(p, null, c1).Codigo);                            // salida del contenedor embarcado
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p, null, c2).Codigo); // contenedor aún sin embarcar

        e.CancelarContenedor(c1, "DEMO cancelación", false);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p, null, c1).Codigo); // cancelado ya no se factura
    }

    [Fact]
    public void Datos_aduanales_de_la_factura_solo_del_contenedor_de_la_salida_y_sin_cancelados()
    {
        // H3 de QA: la factura de la salida de un contenedor no toma la fracción de otro contenedor ni la de uno cancelado.
        var e = Nuevo(); var cliente = Guid.NewGuid();
        var a = Pedido(e, "DEMO-300", cliente, (1, 60), (2, 10));
        var c1 = Contenedor(e, "DEMO-C1"); var c2 = Contenedor(e, "DEMO-C2");
        e.AsignarPartida(c1, a, 1, 30, Actor);
        e.AsignarPartida(c2, a, 2, 10, Actor);
        e.AsignarPartida(c2, a, 1, 30, Actor);
        foreach (var partida in e.Partidas.Where(p => p.ContenedorId == c2).ToArray())
            e.GuardarDatosAduanales(partida.Id, partida.Posicion == 1 ? "7005299901" : "7007199900", "01", Actor, Ahora, true);

        Assert.Equal([(1, "7005299901", "01")], e.DatosAduanalesFactura(a.Id, c1));
        Assert.Equal([(1, "7005299901", "01"), (2, "7007199900", "01")], e.DatosAduanalesFactura(a.Id, c2));
        Assert.Equal([(1, "7005299901", "01"), (2, "7007199900", "01")], e.DatosAduanalesFactura(a.Id));

        e.CancelarContenedor(c2, "DEMO naviera canceló el booking", false);

        Assert.Empty(e.DatosAduanalesFactura(a.Id, c2));
        Assert.Equal([(1, "7005299901", "01")], e.DatosAduanalesFactura(a.Id));
        Assert.Equal([(1, "7005299901", "01")], e.DatosAduanalesFactura(a.Id, c1));
    }
}
