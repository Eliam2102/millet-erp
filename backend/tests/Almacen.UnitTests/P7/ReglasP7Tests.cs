using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Apartados;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.P7;

public sealed class ReglasP7Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recepcion_en_periodo_contable_cerrado_rechaza_antes_de_consultar_conversion(bool packing)
    {
        await using var f = new P1Fixture();
        var conversion = new ConversionNoDisponible();
        RegistrarRecepcionLineaInput[] lineas = [new(f.ArticuloId, f.LineaId, 1, null, null, f.BinId)];
        Func<Task> recibir = packing
            ? () => new RegistrarRecepcionConPackingListHandler(f.Db, f.Oc, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(false), conversion)
                .Handle(new(f.DocumentoId, P1Fixture.Fecha, "DEMO-P7.pdf", null, lineas), default)
            : () => new RegistrarRecepcionConFacturaHandler(f.Db, f.Oc, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(false), conversion)
                .Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas), default);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(recibir);
        ex.Code.Should().Be("PERIODO_CONTABLE_NO_ADMITE");
        conversion.Llamadas.Should().Be(0);
        (await f.Db.Movimientos.AnyAsync()).Should().BeFalse();
        f.Events.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(6, 0, null, 0)]
    [InlineData(3, 3, null, 0)]
    [InlineData(5, 0, null, 15)]
    [InlineData(2, 1, null, 17)]
    [InlineData(2, 1, 7, 7)]
    public void Reorden_evalua_punto_y_repone_maximo_o_cantidad_fija(decimal existencia, decimal vivo, int? fija, decimal esperado)
    {
        var config = new ConfiguracionReorden(Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Almacen, Guid.NewGuid(), 1, 20, 5, true, ObjetivoReposicion.Maximo, cantidadFija: fija);
        config.CalcularReposicion(existencia, vivo).Should().Be(esperado);
    }

    [Theory]
    [InlineData(2, 12, 1, 24)]
    [InlineData(24, 1, 12, 2)]
    public void Conversion_usa_factores_en_ambos_sentidos(decimal cantidad, decimal origen, decimal destino, decimal esperado) =>
        ConversionUnidades.Convertir(cantidad, origen, destino).Should().Be(esperado);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recibir_dos_cajas_conserva_captura_y_registra_24_piezas(bool packing)
    {
        await using var f = new P1Fixture();
        var oc = (await f.Oc.ObtenerAsync(f.DocumentoId, default))!;
        var port = new P1Fixture.OcPort(oc with { Lineas = [oc.Lineas[0] with { CantidadSolicitada = 30 }] });
        RegistrarRecepcionLineaInput[] lineas = [new(f.ArticuloId, f.LineaId, 2, null, null, f.BinId, "CAJA")];
        var conv = new P7Support.Conversion();
        if (packing) await new RegistrarRecepcionConPackingListHandler(f.Db, port, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), conv).Handle(new(f.DocumentoId, P1Fixture.Fecha, "packing.pdf", null, lineas), default);
        else await new RegistrarRecepcionConFacturaHandler(f.Db, port, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), conv).Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas), default);
        var linea = (await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single();
        linea.Cantidad.Should().Be(24);
        linea.UnidadMedida.Should().Be("PZA");
        linea.CantidadCapturada.Should().Be(2);
        linea.UnidadCapturada.Should().Be("CAJA");
        f.Events.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Salida_convierte_caja_a_base_y_respeta_tope_documental()
    {
        await using var f = new P1Fixture();
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var port = new P1Fixture.RqPort(rq with { Lineas = [rq.Lineas[0] with { CantidadSolicitada = 24, CantidadDisponibleEntregar = 24 }] });
        await new RegistrarSalidaConRequisicionHandler(f.Db, port, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db)).Handle(new(f.DocumentoId, P1Fixture.Fecha, null, null, [new(f.ArticuloId, f.LineaId, 2, null, null, null, null, f.BinId, "CAJA")]), default);
        var linea = (await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single();
        linea.Cantidad.Should().Be(24);
        linea.CantidadCapturada.Should().Be(2);
        linea.UnidadCapturada.Should().Be("CAJA");
    }

    [Fact]
    public async Task Apartado_no_se_ofrece_a_otra_rq_y_se_libera_al_surtir_o_cancelar()
    {
        await using var f = new P1Fixture();
        var saldo = await f.Db.SaldosInventario.SingleAsync();
        f.Db.SaldosInventario.Remove(saldo);
        f.Db.SaldosInventario.Add(new(f.BinId, f.SubId, f.ArticuloId, 2, 25));
        await f.Db.SaveChangesAsync();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var servicio = P7Support.Apartados(f.Db);
        var apartado = await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, f.DocumentoId, f.LineaId, f.ArticuloId, 10, default);
        apartado.Should().Be(2);
        (10 - apartado).Should().Be(8);
        (await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, Guid.NewGuid(), Guid.NewGuid(), f.ArticuloId, 10, default)).Should().Be(0);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => servicio.ConsumirAsync(sucursal, Guid.NewGuid(), Guid.NewGuid(), f.ArticuloId, 1, default));
        ex.Code.Should().Be("SALIDA_EXISTENCIA_APARTADA");
        await servicio.BloquearAsync(sucursal, [f.ArticuloId], default);
        await servicio.ConsumirAsync(sucursal, f.DocumentoId, f.LineaId, f.ArticuloId, 1, default);
        await f.Db.SaveChangesAsync();
        (await f.Db.ApartadosRequisicion.SingleAsync()).Pendiente.Should().Be(1);
        await servicio.LiberarAsync(f.DocumentoId, default);
        (await new AlmacenSaldoQueryAdapter(f.Db).ConsultarDisponibilidadPorSucursalAsync(sucursal, f.ArticuloId, default)).CantidadDisponible.Should().Be(2);
    }

    [Fact]
    public async Task Avisos_separan_vales_vencidos_por_vencer_y_regularizados()
    {
        await using var f = new P1Fixture();
        var vencido = await f.MovimientoAsync(Millet.Almacen.Domain.Movimientos.TipoMovimiento.SalidaPorVale);
        vencido.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
        var cerca = await f.MovimientoAsync(Millet.Almacen.Domain.Movimientos.TipoMovimiento.SalidaPorVale);
        cerca.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(2));
        var lejos = await f.MovimientoAsync(Millet.Almacen.Domain.Movimientos.TipoMovimiento.SalidaPorVale);
        lejos.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(30));
        var regularizado = await f.MovimientoAsync(Millet.Almacen.Domain.Movimientos.TipoMovimiento.SalidaPorVale);
        regularizado.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
        regularizado.RegularizarVale(f.DocumentoId);
        await f.Db.SaveChangesAsync();
        var handler = new ListarSalidasHandler(f.Db, f.Rq);
        var query = new ListarSalidasQuery(null, null, null, null, null, null, true, true, 0, 20);
        var atrasados = await handler.Handle(query with { SoloVencidos = true }, default);
        atrasados.Items.Select(i => i.Id).Should().BeEquivalentTo([vencido.Id]);
        var proximos = await handler.Handle(query with { SoloPorVencer = true }, default);
        proximos.Items.Select(i => i.Id).Should().BeEquivalentTo([cerca.Id]);
    }
    [Fact]
    public async Task Motor_no_genera_sobre_punto_y_genera_borrador_hasta_maximo()
    {
        await using var f = new P1Fixture();
        var almacen = await f.Db.Almacenes.SingleAsync();
        f.Db.ConfiguracionesReorden.Add(new(Guid.NewGuid(), f.ArticuloId, NivelReorden.Almacen, almacen.Id, 1, 200, 50, true, ObjetivoReposicion.Maximo));
        await f.Db.SaveChangesAsync();
        var vivo = new VivoP7();
        var crear = new CrearP7();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<MediatR.IRequestHandler<Millet.Almacen.Application.Reorden.EvaluarReordenQuery, IReadOnlyList<Millet.Almacen.Application.Reorden.FaltanteReorden>>>(new Millet.Almacen.Application.Reorden.EvaluarReordenHandler(f.Db, new AlmacenSaldoQueryAdapter(f.Db), vivo));
        using var provider = services.BuildServiceProvider();
        var handler = new Millet.Almacen.Application.Reorden.GenerarBorradoresReordenHandler(new MediatR.Mediator(provider), f.Db, crear, Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Almacen.Application.Reorden.GenerarBorradoresReordenHandler>.Instance);
        (await handler.Handle(new(), default)).RqsCreadas.Should().Be(0);
        crear.Solicitudes.Should().BeEmpty();
        f.Db.SaldosInventario.Remove(await f.Db.SaldosInventario.SingleAsync());
        f.Db.SaldosInventario.Add(new(f.BinId, f.SubId, f.ArticuloId, 2, 25));
        await f.Db.SaveChangesAsync();
        vivo.Manual = 3;
        (await handler.Handle(new(), default)).RqsCreadas.Should().Be(1);
        crear.Solicitudes.Single().Lineas.Single().Cantidad.Should().Be(195);
        vivo.Manual = 195;
        (await handler.Handle(new(), default)).RqsCreadas.Should().Be(0);
        crear.Solicitudes.Should().ContainSingle();
    }
    private sealed class VivoP7 : IComprasPedidoVivoReadPort
    {
        public decimal Manual { get; set; }
        public Task<IReadOnlyDictionary<PedidoVivoClave, decimal>> ObtenerVivoDeSistemaAsync(IReadOnlyCollection<PedidoVivoClave> pares, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<PedidoVivoClave, decimal>>(new Dictionary<PedidoVivoClave, decimal>());
        public Task<IReadOnlyDictionary<PedidoVivoSucursalClave, decimal>> ObtenerVivoManualAsync(IReadOnlyCollection<PedidoVivoSucursalClave> pares, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<PedidoVivoSucursalClave, decimal>>(pares.ToDictionary(p => p, _ => Manual));
    }
    private sealed class ConversionNoDisponible : IConversionUnidadPort
    {
        public int Llamadas { get; private set; }
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? unidadCapturada, string unidadDocumento, CancellationToken ct)
        {
            Llamadas++;
            throw new EntityNotFoundException("ARTICULO_NO_ENCONTRADO", "Artículo DEMO inexistente.");
        }
    }
    private sealed class CrearP7 : IComprasCrearRqSistemaPort
    {
        public List<CrearRqSistemaSolicitud> Solicitudes { get; } = [];
        public Task<Guid> CrearBorradorSistemaAsync(CrearRqSistemaSolicitud solicitud, CancellationToken ct) { Solicitudes.Add(solicitud); return Task.FromResult(Guid.NewGuid()); }
    }
}
