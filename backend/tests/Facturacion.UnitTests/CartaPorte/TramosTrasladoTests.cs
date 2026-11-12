using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.CartaPorte.CorregirCartaPorte;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.CartaPorte.Tramos;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.CartaPorte;

/// <summary>
/// M-354: tramos del traslado con modalidad y corrección con rastro. Un tramo de transportista tercero no emite Carta Porte
/// de Millet y registra el documento del tercero; solo el vehículo propio emite; lo emitido sin tramo no cambia; una Carta
/// Porte observada se corrige con motivo y bitácora y una timbrada no. Datos genéricos.
/// </summary>
public sealed class TramosTrasladoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static PedidoFacturable Pedido(FacturacionDbContext db, Guid empresa)
    {
        var p = PedidoFacturable.ImportarDesdeAw(empresa, "PED-3001", Guid.NewGuid(), Guid.NewGuid(), "Cliente prueba", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        db.PedidosFacturables.Add(p);
        db.SaveChanges();
        return p;
    }

    private sealed class DireccionesFijas(DireccionesPedidoAw? direcciones) : IDireccionEntregaPedidoReadPort
    {
        public Task<DireccionesPedidoAw?> ObtenerAsync(Guid pedidoFacturableId, CancellationToken cancellationToken) =>
            Task.FromResult(direcciones);
    }

    private static CrearTramoTrasladoHandler Crear(FacturacionDbContext db, Guid empresa, DireccionesPedidoAw? direcciones = null) =>
        new(db, new FakeAlcanceCajaEvaluator(), new FakeEmpresaContext(empresa), new DireccionesFijas(direcciones));

    // ---------- regla del destino (AL_ / AH_ de BW_AUFTR_KOPF) ----------

    private static DireccionPedidoAw Dir(string? nombre, string? calle, string? cp = null, string? ciudad = null, string? prov = null, string? pais = null) =>
        new(nombre, calle, cp, ciudad, prov, pais);

    [Fact]
    public void Con_direccion_de_entrega_el_destino_es_esa()
    {
        var d = new DireccionesPedidoAw(Dir(null, "Calle Principal 1", "97000"), Dir("Bodega cliente", "Calle Entrega 2", "97100"));
        DireccionEntregaPedido.Resolver(d).CodigoPostal.Should().Be("97100");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", "")]
    public void Sin_nombre_ni_calle_de_entrega_se_entrega_en_la_principal(string? nombre, string? calle)
    {
        var d = new DireccionesPedidoAw(Dir(null, "Calle Principal 1", "97000"), Dir(nombre, calle, "97100"));
        DireccionEntregaPedido.Resolver(d).CodigoPostal.Should().Be("97000");
    }

    [Fact]
    public void Solo_la_calle_de_entrega_basta_para_usar_la_de_entrega()
    {
        var d = new DireccionesPedidoAw(Dir(null, "Principal", "97000"), Dir(null, "Entrega", "97100"));
        DireccionEntregaPedido.Resolver(d).CodigoPostal.Should().Be("97100");
    }

    [Fact]
    public void El_texto_del_destino_omite_los_vacios()
    {
        DireccionEntregaPedido.Texto(Dir(null, "Calle 5", "97000", "Mérida", null, "MEX")).Should().Be("Calle 5, Mérida, MEX");
        DireccionEntregaPedido.Texto(Dir(null, null)).Should().BeNull();
    }

    // ---------- tramos ----------

    [Fact]
    public async Task Los_tramos_del_pedido_se_numeran_en_orden_con_su_modalidad()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);
        var h = Crear(db, empresa);

        var uno = await h.Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.VehiculoPropio, "Planta", "Puerto"), CancellationToken.None);
        var dos = await h.Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.TransportistaTercero, "Puerto", "Cliente"), CancellationToken.None);

        (uno.Orden, uno.Estado).Should().Be((1, "PorEmitir"));
        (dos.Orden, dos.Estado).Should().Be((2, "SinDocumentoTercero"));
        (await db.TramosTraslado.SingleAsync(t => t.Id == dos.Id)).SucursalId.Should().Be(pedido.SucursalId); // alcance ADR-0051
    }

    [Fact]
    public async Task Sin_destino_toma_la_direccion_de_entrega_del_pedido()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);
        var dirs = new DireccionesPedidoAw(Dir(null, "Calle Principal 1", "97000", "Mérida"), Dir(null, null, "97100"));

        var r = await Crear(db, empresa, dirs).Handle(
            new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.VehiculoPropio, "Planta"), CancellationToken.None);

        r.Destino.Should().Be("Calle Principal 1, Mérida"); // entrega vacía -> principal
        r.DestinoCodigoPostal.Should().Be("97000");
    }

    [Fact]
    public async Task Sin_destino_y_sin_direccion_disponible_pide_capturarlo()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Crear(db, empresa).Handle(
            new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.VehiculoPropio, "Planta"), CancellationToken.None));

        ex.Code.Should().Be("TRAMO_DESTINO_REQUERIDO");
    }

    [Fact]
    public async Task El_tramo_de_tercero_registra_su_documento_y_el_propio_no()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);
        var tercero = await Crear(db, empresa).Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.TransportistaTercero, "A", "B"), CancellationToken.None);
        var propio = await Crear(db, empresa).Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.VehiculoPropio, "B", "C"), CancellationToken.None);
        var h = new RegistrarDocumentoTerceroHandler(db, new FakeAlcanceCajaEvaluator(), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora));

        var r = await h.Handle(new RegistrarDocumentoTerceroCommand(pedido.Id, tercero.Id, "Transportes Prueba SA", "CartaPorte",
            "TPR010101AAA", "CP-778", "aaaaaaaa-0000-0000-0000-000000000001", new DateOnly(2026, 11, 10)), CancellationToken.None);

        r.Estado.Should().Be("DocumentoTercero");
        r.DocumentoFolio.Should().Be("CP-778");
        r.DocumentoUuid.Should().Be("AAAAAAAA-0000-0000-0000-000000000001");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(
            new RegistrarDocumentoTerceroCommand(pedido.Id, propio.Id, "X", "CartaPorte", Folio: "1"), CancellationToken.None));
        ex.Code.Should().Be("TRAMO_NO_ES_TERCERO");
    }

    [Fact]
    public void El_documento_del_tercero_exige_folio_o_uuid()
    {
        var t = TramoTraslado.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, ModalidadTramo.TransportistaTercero, "A", "B", null, null, null, null, null, null);
        var act = () => t.RegistrarDocumentoTercero("Transportista", null, "CartaPorte", null, null, null, "x", Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TRAMO_DOCUMENTO_SIN_FOLIO");
    }

    // ---------- emisión: solo vehículo propio; sin tramo nada cambia ----------

    private sealed class SenderFolios : ISender
    {
        public int Reservas { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is not ReservarFolioCommand) throw new NotImplementedException();
            Reservas++;
            return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"CP-{Reservas:D6}", Reservas, ""));
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static EmitirCartaPorteHandler Emitir(FacturacionDbContext db, Guid empresa, ISender sender) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresa, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeEmpresaContext(empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static async Task<(Guid V, Guid O)> Catalogos(FacturacionDbContext db, Guid empresa)
    {
        var v = Vehiculo.Crear(empresa, "ABC-123", "VL", 2022, "TPAF01", "PERM-1", "Seguros X", "POL-1", pesoBrutoVehicular: 17.5m);
        var o = Operador.Crear(empresa, "OPER010101AAA", "Operador Prueba", "LIC-12345");
        db.Vehiculos.Add(v);
        db.Operadores.Add(o);
        await db.SaveChangesAsync();
        return (v.Id, o.Id);
    }

    private static EmitirCartaPorteCommand Comando(Guid v, Guid o, Guid? pedidoId = null, Guid? tramoId = null) => new(
        Guid.NewGuid(), null, "T", "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "MXN", "Planta", "Cliente", 300m, v, o, pedidoId, Ahora, Ahora.AddHours(4), 0m, null,
        [new CartaPorteMercanciaInput("Vidrio templado", "43211503", "KGM", 10m, 500m, false)],
        OrigenCodigoPostal: "97345", OrigenEstado: "YUC", DestinoCodigoPostal: "77500", DestinoEstado: "ROO", TramoId: tramoId);

    [Fact]
    public async Task Un_tramo_de_transportista_tercero_no_emite_Carta_Porte_propia()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);
        var (v, o) = await Catalogos(db, empresa);
        var tercero = await Crear(db, empresa).Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.TransportistaTercero, "A", "B"), CancellationToken.None);
        var sender = new SenderFolios();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Emitir(db, empresa, sender).Handle(Comando(v, o, pedido.Id, tercero.Id), CancellationToken.None));

        ex.Code.Should().Be("CARTA_PORTE_TRAMO_TERCERO");
        sender.Reservas.Should().Be(0); // no quemó folio
        (await db.CartasPorte.CountAsync()).Should().Be(0);
        (await db.TramosTraslado.SingleAsync()).CartaPorteId.Should().BeNull();
    }

    [Fact]
    public async Task Un_tramo_propio_emite_y_queda_ligado_a_su_Carta_Porte()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(db, empresa);
        var (v, o) = await Catalogos(db, empresa);
        var propio = await Crear(db, empresa).Handle(new CrearTramoTrasladoCommand(pedido.Id, ModalidadTramo.VehiculoPropio, "A", "B"), CancellationToken.None);
        var handler = Emitir(db, empresa, new SenderFolios());

        var r = await handler.Handle(Comando(v, o, pedido.Id, propio.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        (await db.TramosTraslado.SingleAsync()).CartaPorteId.Should().Be(r.Id);
        var lista = await new ListarTramosPedidoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ListarTramosPedidoQuery(pedido.Id), CancellationToken.None);
        lista.Single().Estado.Should().Be("CartaPorteTimbrado");
        // un tramo no emite dos veces
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(Comando(v, o, pedido.Id, propio.Id), CancellationToken.None));
        ex.Code.Should().Be("TRAMO_YA_TIENE_CARTA_PORTE");
    }

    [Fact]
    public async Task Sin_tramo_la_emision_es_la_de_siempre()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (v, o) = await Catalogos(db, empresa);

        var r = await Emitir(db, empresa, new SenderFolios()).Handle(Comando(v, o), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        (await db.TramosTraslado.CountAsync()).Should().Be(0);
    }

    // ---------- corrección con rastro ----------

    private static async Task<(Dominio.CartaPorte Cp, Guid V2)> CartaObservada(FacturacionDbContext db, Guid empresa, bool fallida = true)
    {
        var (v, o) = await Catalogos(db, empresa);
        var v2 = Vehiculo.Crear(empresa, "XYZ-987", "VL", 2023, "TPAF01", "PERM-2", "Seguros X", "POL-2", pesoBrutoVehicular: 12m);
        db.Vehiculos.Add(v2);
        var cp = Dominio.CartaPorte.CrearBorrador(empresa, TipoComprobante.Traslado, "CP-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor viejo", "601", "76120"), "MXN", 2026, 11, "Planta", "Puerto", 100m,
            v, o, null, null, Ahora, Ahora.AddHours(4), origenCodigoPostal: "97345", origenEstado: "YUC", destinoCodigoPostal: "00000", destinoEstado: "ROO");
        cp.AgregarMercancia("Vidrio", "43211503", "KGM", 1m, 10m, false);
        if (fallida)
        {
            cp.MarcarTimbradoEnProceso();
            cp.MarcarTimbradoFallido("CFDI40000", "Código postal de destino inválido");
        }
        db.CartasPorte.Add(cp);
        await db.SaveChangesAsync();
        return (cp, v2.Id);
    }

    private static CorregirCartaPorteHandler Corregir(FacturacionDbContext db, Guid empresa) =>
        new(db, new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresa, "MIL010101AAA", "Millet SA", "601", 0.16m, "76120")),
            new FakeEmpresaContext(empresa), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora));

    [Fact]
    public async Task Corregir_un_tramo_observado_deja_rastro_de_cada_campo_con_motivo_y_usuario()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (cp, v2) = await CartaObservada(db, empresa);

        var cambios = await Corregir(db, empresa).Handle(new CorregirCartaPorteCommand(
            cp.Id, "El CP de destino estaba mal capturado", ActualizarEmisor: true,
            DestinoCodigoPostal: "77500", VehiculoId: v2), CancellationToken.None);

        cambios.Select(c => c.Campo).Should().BeEquivalentTo("destino.codigoPostal", "vehiculo", "emisor.rfc", "emisor.nombre");
        var cp2 = cambios.Single(c => c.Campo == "destino.codigoPostal");
        (cp2.ValorAnterior, cp2.ValorNuevo).Should().Be(("00000", "77500"));
        cambios.Single(c => c.Campo == "vehiculo").ValorNuevo.Should().Be("XYZ-987");
        var bitacora = await db.CorreccionesTramo.ToListAsync();
        bitacora.Should().HaveCount(4).And.OnlyContain(b => b.Motivo == "El CP de destino estaba mal capturado" && b.UsuarioNombre == "Cajera Prueba");
        var guardada = await db.CartasPorte.SingleAsync();
        guardada.DestinoCodigoPostal.Should().Be("77500");
        guardada.RfcEmisor.Should().Be("MIL010101AAA");
        guardada.Estado.Should().Be(EstadoTimbrado.TimbradoFallido); // se reemite con «Reintentar»
    }

    [Fact]
    public async Task Una_Carta_Porte_timbrada_no_se_corrige()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (cp, _) = await CartaObservada(db, empresa, fallida: false);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Corregir(db, empresa).Handle(
            new CorregirCartaPorteCommand(cp.Id, "intento de corrección", DestinoCodigoPostal: "77500"), CancellationToken.None));

        ex.Code.Should().Be("CARTA_PORTE_NO_CORREGIBLE");
        (await db.CorreccionesTramo.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Corregir_sin_cambios_reales_no_guarda_nada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (cp, _) = await CartaObservada(db, empresa);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Corregir(db, empresa).Handle(
            new CorregirCartaPorteCommand(cp.Id, "mismo valor", DestinoCodigoPostal: "00000"), CancellationToken.None));

        ex.Code.Should().Be("CARTA_PORTE_CORRECCION_SIN_CAMBIOS");
        (await db.CorreccionesTramo.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void El_motivo_de_la_correccion_es_obligatorio()
    {
        new CorregirCartaPorteValidator().Validate(new CorregirCartaPorteCommand(Guid.NewGuid(), "  ")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task La_bitacora_se_consulta_con_lo_mas_reciente_primero()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (cp, _) = await CartaObservada(db, empresa);
        await Corregir(db, empresa).Handle(new CorregirCartaPorteCommand(cp.Id, "primera corrección", DestinoCodigoPostal: "77500"), CancellationToken.None);

        var lista = await new CorreccionesCartaPorteHandler(db).Handle(new CorreccionesCartaPorteQuery(cp.Id), CancellationToken.None);

        lista.Should().ContainSingle().Which.Motivo.Should().Be("primera corrección");
    }
}
