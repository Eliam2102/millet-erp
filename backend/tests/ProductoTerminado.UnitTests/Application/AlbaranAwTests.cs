using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>Descuento de PT por albaranes de A+W (el albarán vive en A+W, decisión 10-nov).</summary>
public sealed class AlbaranAwTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 11, 10);
    private readonly EntornoPt _e = new();
    private readonly AplicarAlbaranesAwHandler _handler;
    private readonly PedidoPt _pedido;

    public AlbaranAwTests()
    {
        _handler = new AplicarAlbaranesAwHandler(_e.Db, new RelojFijo(Ahora));
        _pedido = PedidoPt.Crear(EntornoPt.Empresa, Guid.NewGuid(), "10425068", EntornoPt.Sucursal, null, 1);
        _e.Db.PedidosPt.Add(_pedido);
        _e.Db.SaveChanges();
    }

    public void Dispose() => _e.Dispose();

    private List<PiezaPt> Piezas(int posicion, int cuantas, int desde = 1)
    {
        var piezas = Enumerable.Range(desde, cuantas)
            .Select(n => PiezaPt.Alta(EntornoPt.Empresa, _pedido, posicion, n, $"E{posicion}{n:000}", EntornoPt.Sucursal, Ahora, Guid.NewGuid())).ToList();
        _e.Db.Piezas.AddRange(piezas);
        _e.Db.SaveChanges();
        return piezas;
    }

    private Task<int> Aplicar(int posicion, params AlbaranAwPiezas[] albaranes) =>
        _handler.Handle(new AplicarAlbaranesAwCommand(EntornoPt.Empresa, "10425068", posicion, albaranes), CancellationToken.None);

    private int EnPt(int posicion) => _e.Db.Piezas.Count(p => p.Posicion == posicion && p.Estado == EstadoPieza.EnPT);

    [Fact]
    public async Task Descuenta_las_piezas_del_albaran_y_repetirlo_no_descuenta_otra_vez()
    {
        Piezas(5, 3);

        (await Aplicar(5, new AlbaranAwPiezas("10425068", 10884488, Hoy, 2))).Should().Be(2);
        (await Aplicar(5, new AlbaranAwPiezas("10425068", 10884488, Hoy, 2))).Should().Be(0);

        EnPt(5).Should().Be(1);
        var movs = await _e.Db.Movimientos.Where(m => m.Referencia == "aw-albaran:10425068:10884488").ToListAsync();
        movs.Should().HaveCount(2).And.OnlyContain(m => m.Tipo == TipoMovimientoPieza.Salida && m.EstadoAnterior == "EnPT" && m.EstadoNuevo == "Embarcada");
    }

    [Fact]
    public async Task Entregas_parciales_en_varios_albaranes_descuentan_solo_lo_nuevo()
    {
        Piezas(1, 4);
        (await Aplicar(1, new AlbaranAwPiezas("10425068", 80119397, Hoy, 1))).Should().Be(1);

        (await Aplicar(1, new AlbaranAwPiezas("10425068", 80119397, Hoy, 1), new AlbaranAwPiezas("10425068", 80119398, Hoy.AddDays(1), 2))).Should().Be(2);

        EnPt(1).Should().Be(1);
        _e.Db.Movimientos.Count(m => m.Referencia == "aw-albaran:10425068:80119398").Should().Be(2);
    }

    [Fact]
    public async Task Lo_que_ya_salio_por_la_hoja_de_salida_del_erp_no_se_descuenta_dos_veces()
    {
        var piezas = Piezas(2, 3);
        piezas[0].Cargar(); piezas[0].Embarcar();   // hoja de salida del ERP emitida
        piezas[1].Cargar();                          // en el camión de una hoja en borrador
        await _e.Db.SaveChangesAsync();

        (await Aplicar(2, new AlbaranAwPiezas("10425068", 1, Hoy, 2))).Should().Be(0);
        (await Aplicar(2, new AlbaranAwPiezas("10425068", 1, Hoy, 3))).Should().Be(1);

        piezas[1].Estado.Should().Be(EstadoPieza.Cargada, "la hoja del ERP la sigue llevando y la embarca al emitirse");
        EnPt(2).Should().Be(0);
    }

    [Fact]
    public async Task Si_las_piezas_aun_no_llegan_a_pt_descuenta_lo_que_hay_y_el_resto_despues()
    {
        Piezas(3, 1);
        (await Aplicar(3, new AlbaranAwPiezas("10425068", 7, Hoy, 3))).Should().Be(1);

        Piezas(3, 2, desde: 2);
        (await Aplicar(3, new AlbaranAwPiezas("10425068", 7, Hoy, 3))).Should().Be(2);
        (await Aplicar(3, new AlbaranAwPiezas("10425068", 7, Hoy, 3))).Should().Be(0);
        EnPt(3).Should().Be(0);
    }

    [Fact]
    public async Task Pedido_o_posicion_sin_piezas_en_pt_no_hace_nada()
    {
        (await _handler.Handle(new AplicarAlbaranesAwCommand(EntornoPt.Empresa, "99999999", 1, [new("10425068", 1, Hoy, 1)]), CancellationToken.None)).Should().Be(0);
        (await Aplicar(9, new AlbaranAwPiezas("10425068", 1, Hoy, 1))).Should().Be(0);
        (await _handler.Handle(new AplicarAlbaranesAwCommand(Guid.NewGuid(), "10425068", 1, [new("10425068", 1, Hoy, 1)]), CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task El_mismo_folio_en_otro_pedido_de_aw_es_otro_albaran()
    {
        Piezas(4, 3);
        (await Aplicar(4, new AlbaranAwPiezas("10425068", 55, Hoy, 1))).Should().Be(1);

        // Expedición parcial: el pedido hijo lleva su propio albarán; si TI repite el folio no se confunde con el del origen.
        (await Aplicar(4, new AlbaranAwPiezas("10425068", 55, Hoy, 1), new AlbaranAwPiezas("10425999", 55, Hoy, 1))).Should().Be(1);

        _e.Db.Movimientos.Count(m => m.Referencia == "aw-albaran:10425068:55").Should().Be(1);
        _e.Db.Movimientos.Count(m => m.Referencia == "aw-albaran:10425999:55").Should().Be(1);
        EnPt(4).Should().Be(1);
    }

    [Fact]
    public async Task Si_la_hoja_de_salida_del_erp_carga_la_pieza_a_la_vez_no_se_embarca_dos_veces_y_se_reintenta()
    {
        var piezas = Piezas(6, 1);
        // El handler ya tiene la pieza leída EnPT; otra transacción (hoja de salida del ERP) la carga antes del guardado.
        await using (var otro = _e.OtroContexto())
        {
            var pz = await otro.Piezas.SingleAsync(p => p.Id == piezas[0].Id);
            pz.Cargar();
            otro.Entry(pz).Property(p => p.Version).CurrentValue = pz.Version + 1;   // lo hace el interceptor de metadata
            await otro.SaveChangesAsync();
        }

        (await Aplicar(6, new AlbaranAwPiezas("10425068", 9, Hoy, 1))).Should().Be(0);
        // (El proveedor en memoria no es transaccional; en PostgreSQL el SaveChanges completo se revierte.)
        await using (var otro = _e.OtroContexto())
            (await otro.Piezas.SingleAsync(p => p.Id == piezas[0].Id)).Estado.Should().Be(EstadoPieza.Cargada, "no se embarcó por el albarán de A+W");

        // Siguiente ciclo, con el estado nuevo: la pieza ya está cargada por la hoja del ERP y no se descuenta otra vez.
        (await Aplicar(6, new AlbaranAwPiezas("10425068", 9, Hoy, 1))).Should().Be(0);
        (await _e.Db.Piezas.SingleAsync(p => p.Id == piezas[0].Id)).Estado.Should().Be(EstadoPieza.Cargada);
    }
}
