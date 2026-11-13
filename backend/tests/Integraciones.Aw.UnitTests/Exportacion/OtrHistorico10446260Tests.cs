using Millet.ComercioExterior.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Infrastructure.Exportacion;
namespace Millet.Integraciones.Aw.UnitTests.Exportacion;

/// <summary>
/// Forma del pedido 10446260 (diagnóstico 12-nov): 51 posiciones y 186 piezas en OTR etiquetadas con el origen; el hijo 11022579
/// («Expedición parcial», estatus 72) llevó 105 piezas en 36 posiciones (30 completas, que dejan el origen en cero); quedan 81 en el
/// origen (estatus 115, facturado en SAP).
/// </summary>
public sealed class OtrHistorico10446260Tests
{
    private const string Origen = "10446260", Hijo = "11022579";
    private static readonly HistoricoSapOptions Encendido = new() { Habilitado = true };

    // (posición, piezas originales, piezas del hijo): 1–30 salieron completas en el hijo, 31–36 a medias, 37–51 sólo en el origen.
    private static readonly (int Pos, decimal Total, decimal EnHijo)[] Posiciones =
        Enumerable.Range(1, 30).Select(p => (p, 3m, 3m))
        .Concat(Enumerable.Range(31, 6).Select(p => (p, 5m, p <= 33 ? 2m : 3m)))
        .Concat(Enumerable.Range(37, 15).Select(p => (p, p == 51 ? 10m : 4m, 0m))).ToArray();
    private static readonly OtrTablasAdapter.HijoParcial[] Hijos =
        Posiciones.Where(x => x.EnHijo > 0).Select((x, i) => new OtrTablasAdapter.HijoParcial(x.Pos, Hijo, i + 1, x.EnHijo, 72)).ToArray();

    /// <summary>Simula los 20 envíos: cada posición llega en pirámides de una o dos piezas, en orden.</summary>
    private static List<List<OtrLineaRecibida>> Envios()
    {
        var envios = Enumerable.Range(0, 20).Select(_ => new List<OtrLineaRecibida>()).ToList();
        var siguiente = 0;
        foreach (var (pos, total, _) in Posiciones)
        {
            decimal previo = 0;
            while (previo < total)
            {
                var piezas = Math.Min(2, total - previo);
                var linea = new OtrLineaRecibida(Origen, pos, piezas, "H87", Encendido.EsHistorico(115));
                envios[siguiente++ % 20].AddRange(OtrTablasAdapter.Repartir(linea, previo, Hijos.Where(h => h.PosicionOrigen == pos).ToArray(), Encendido.EsHistorico));
                previo += piezas;
            }
        }
        return envios;
    }

    [Fact]
    public void Datos_de_prueba_cuadran_con_el_diagnostico()
    {
        Assert.Equal(51, Posiciones.Length);
        Assert.Equal(186, Posiciones.Sum(x => x.Total));
        Assert.Equal(105, Hijos.Sum(x => x.Cantidad));
        Assert.Equal(36, Hijos.Length);
        Assert.Equal(30, Posiciones.Count(x => x.Total == x.EnHijo));
    }

    [Fact]
    public void Las_186_piezas_se_reparten_105_al_hijo_y_81_al_origen_sin_exceder_ninguna_posicion()
    {
        var todas = Envios().SelectMany(x => x).ToArray();
        Assert.Equal(186, todas.Sum(x => x.Cantidad));
        Assert.Equal(105, todas.Where(x => x.NumeroPedido == Hijo).Sum(x => x.Cantidad));
        Assert.Equal(81, todas.Where(x => x.NumeroPedido == Origen).Sum(x => x.Cantidad));
        foreach (var h in Hijos)
            Assert.Equal(h.Cantidad, todas.Where(x => x.NumeroPedido == Hijo && x.Posicion == h.Posicion).Sum(x => x.Cantidad));
        foreach (var (pos, total, enHijo) in Posiciones)
            Assert.Equal(total - enHijo, todas.Where(x => x.NumeroPedido == Origen && x.Posicion == pos).Sum(x => x.Cantidad));
        // Las 30 posiciones completas del hijo no dejan nada en el origen.
        Assert.DoesNotContain(todas, x => x.NumeroPedido == Origen && x.Posicion <= 30);
        Assert.All(todas, x => Assert.True(x.Cantidad > 0));
    }

    [Fact]
    public void Con_el_corte_encendido_el_origen_en_115_es_historico_y_el_hijo_en_72_no()
    {
        var todas = Envios().SelectMany(x => x).ToArray();
        Assert.All(todas.Where(x => x.NumeroPedido == Origen), x => Assert.True(x.Historico));
        Assert.All(todas.Where(x => x.NumeroPedido == Hijo), x => Assert.False(x.Historico));
        Assert.False(new HistoricoSapOptions().EsHistorico(115)); // apagado por omisión
    }

    [Fact]
    public void Envio_solo_del_origen_queda_Historico_SAP_sin_vinculo_y_uno_con_el_hijo_queda_por_vincular()
    {
        // Una pirámide con sólo posiciones que quedaron en el origen (37–51) y otra con las posiciones repartidas (31–36).
        var todas = Envios().SelectMany(x => x).ToList();
        var soloOrigen = todas.Where(l => l.Posicion >= 37 && l.NumeroPedido == Origen).ToList();
        var conHijo = todas.Where(l => l.NumeroPedido == Origen ? l.Posicion is >= 31 and <= 36 : Hijos.Any(h => h.Posicion == l.Posicion && h.PosicionOrigen is >= 31 and <= 36)).ToList();
        Assert.Contains(conHijo, l => l.NumeroPedido == Hijo); Assert.Contains(conHijo, l => l.NumeroPedido == Origen);

        var historico = Aplicar("R-1", soloOrigen);
        Assert.Equal(EstadoOtrEnvio.HistoricoSap, historico.Estado);
        Assert.All(historico.Lineas, l => Assert.Equal(EstadoOtrLinea.Omitida, l.Estado));
        Assert.Equal("CE_OTR_HISTORICO_SAP", Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() => historico.Vincular(null!)).Code);

        var mixto = Aplicar("R-2", conHijo);
        Assert.Equal(EstadoOtrEnvio.PorVincular, mixto.Estado);
        Assert.All(mixto.Lineas.Where(l => l.NumeroPedido == Origen), l => Assert.Equal(EstadoOtrLinea.Omitida, l.Estado));
        Assert.All(mixto.Lineas.Where(l => l.NumeroPedido == Hijo), l => Assert.Equal(EstadoOtrLinea.Pendiente, l.Estado));
        // Sin líneas duplicadas por pedido y posición (las facturables son sólo las del hijo).
        Assert.Equal(mixto.Lineas.Count, mixto.Lineas.Select(l => (l.NumeroPedido, l.Posicion)).Distinct().Count());
    }

    private static OtrEnvio Aplicar(string id, List<OtrLineaRecibida> lineas)
    {
        var o = new OtrEnvio(Guid.NewGuid(), id);
        var unicas = lineas.GroupBy(l => (l.NumeroPedido, l.Posicion)).Select(g => g.First() with { Cantidad = g.Sum(x => x.Cantidad) }).ToArray();
        o.Aplicar(1, new(Guid.NewGuid(), Guid.NewGuid(), "CROWN DISTRIBUTION", "Entrega", null, new("C-1", null, null, null, "R", null, null, null), unicas));
        return o;
    }

    [Fact]
    public void Repartir_respeta_lo_que_ya_ocuparon_pirámides_anteriores()
    {
        var hijo = new[] { new OtrTablasAdapter.HijoParcial(31, Hijo, 1, 2, 72) };
        var linea = new OtrLineaRecibida(Origen, 31, 2, "H87");
        Assert.Equal([(Hijo, 1, 2m)], OtrTablasAdapter.Repartir(linea, 0, hijo, _ => false).Select(x => (x.NumeroPedido, x.Posicion, x.Cantidad)));
        Assert.Equal([(Hijo, 1, 1m), (Origen, 31, 1m)], OtrTablasAdapter.Repartir(linea, 1, hijo, _ => false).Select(x => (x.NumeroPedido, x.Posicion, x.Cantidad)));
        Assert.Equal([(Origen, 31, 2m)], OtrTablasAdapter.Repartir(linea, 2, hijo, _ => false).Select(x => (x.NumeroPedido, x.Posicion, x.Cantidad)));
        // Sin hijos o con cantidad cero, la línea pasa igual.
        Assert.Same(linea, OtrTablasAdapter.Repartir(linea, 0, [], _ => false).Single());
    }
}
