using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Consultas = Millet.ProductoTerminado.Application.Consultas;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Traspasos;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class TraspasosTests
{
    private static readonly Guid Preparador = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
    private static readonly Guid Autorizador = Guid.Parse("00000000-0000-0000-0000-0000000000ee");

    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId { get; } = id;
        public string? UserName => null;
    }

    private sealed class Permisos(params string[] otorgados) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(otorgados.Contains(permiso));
    }

    private sealed class Sucursales(params Guid[] ids) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(ids);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) => Task.FromResult(ids.Contains(sucursalId));
    }

    private sealed class FacturacionFalsa : IFacturacionSalidaPort
    {
        public List<SolicitudCartaPorte> CartasPorte { get; } = [];
        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct) => Task.CompletedTask;
        public Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct) => Task.FromResult(new CancelacionSalidaPt(false, false, false));
        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct)
        {
            CartasPorte.Add(solicitud);
            return Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-TR-1"));
        }
        public Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> albaranFolios, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, FacturaAlbaranRuta>>(new Dictionary<string, FacturaAlbaranRuta>());
        public Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid sucursalId, IReadOnlyList<CobroRutaAlbaran> cobros, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CobroRutaRegistrado>>([]);
        public Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> pedidoFacturableIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaPedidoSalida>>([]);

        public Task<bool> CartaPorteVigenteAsync(Guid cartaPorteId, CancellationToken ct) => Task.FromResult(false);
        public Task<DocumentoFiscalPt> EmitirNotaCreditoDevolucionAsync(Guid facturaId, IReadOnlyList<(int Posicion, int Piezas)> piezas, string descripcion, CancellationToken ct) =>
            Task.FromResult(new DocumentoFiscalPt(Guid.NewGuid(), "NCRED-1", "Timbrado", false));
        public Task<DocumentoFiscalPt> SolicitarCancelacionFacturaAsync(Guid facturaId, string motivoSat, string? uuidSustituto, CancellationToken ct) =>
            Task.FromResult(new DocumentoFiscalPt(facturaId, "F-1", "CancelacionPendiente", true));
        public Task<DocumentoFiscalPt?> EstadoDocumentoAsync(bool notaCredito, Guid id, CancellationToken ct) => Task.FromResult<DocumentoFiscalPt?>(null);
    }

    private sealed class AvisoFalso(bool falla = false) : IAvisoTraspasoPort
    {
        public List<AvisoTraspasoPorRecibir> Avisos { get; } = [];
        public Task AvisarPorRecibirAsync(AvisoTraspasoPorRecibir aviso, CancellationToken ct)
        {
            if (falla) throw new InvalidOperationException("correo caído");
            Avisos.Add(aviso);
            return Task.CompletedTask;
        }
    }

    private static readonly Guid Destino = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private static readonly Guid Vehiculo = Guid.NewGuid(), Operador = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static PtAcceso Acceso(string[]? permisos = null, Guid? usuario = null, params Guid[] sucursales) =>
        new(new Usuario(usuario ?? Preparador), new Permisos(permisos ?? [PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, PermisosCanonicos.ProductoTerminadoPiezasVer]),
            new Sucursales(sucursales.Length == 0 ? [EntornoPt.Sucursal, Destino] : sucursales));

    private static async Task<EntornoPt> Preparar(int piezas = 2)
    {
        var t = new EntornoPt();
        t.AgregarPedido("P1", 1, piezas);
        for (var i = 1; i <= piezas; i++) await t.Escanear($"E{i}", "P1", 1, i.ToString());
        return t;
    }

    private static CrearTraspasoHandler Preparacion(EntornoPt t, PtAcceso? acceso = null) => new(t.Db, acceso ?? Acceso(), new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));

    private static AutorizarTraspasoHandler Autorizacion(EntornoPt t, FacturacionFalsa? fac = null, AvisoFalso? aviso = null, PtAcceso? acceso = null) =>
        new(t.Db, acceso ?? Acceso(usuario: Autorizador), fac ?? new FacturacionFalsa(), aviso ?? new AvisoFalso(), t.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora.AddHours(1)), NullLogger<AutorizarTraspasoHandler>.Instance);

    private static RecibirTraspasoHandler Recepcion(EntornoPt t, PtAcceso? acceso = null) => new(t.Db, acceso ?? Acceso(), t.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora.AddHours(5)));

    private static ClasificarDiferenciasTraspasoHandler Clasificacion(EntornoPt t) => new(t.Db, Acceso());

    private static AclararDiferenciaTraspasoHandler Aclaracion(EntornoPt t) => new(t.Db, Acceso(), t.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora.AddHours(6)));

    /// <summary>Prepara y autoriza (otra persona) un traspaso de las etiquetas dadas.</summary>
    private static async Task<TraspasoDto> EnTransito(EntornoPt t, params string[] etiquetas)
    {
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, etiquetas), default);
        return await Autorizacion(t).Handle(new AutorizarTraspasoCommand(preparado.Id), default);
    }

    private static async Task<Guid> PiezaId(EntornoPt t, string etiqueta) => (await t.Db.Piezas.SingleAsync(p => p.Etiqueta == etiqueta)).Id;

    [Fact]
    public async Task Preparar_documenta_con_folio_pero_no_mueve_las_piezas()
    {
        using var t = await Preparar();
        var dto = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"]), default);

        dto.Estado.Should().Be("Preparado");
        dto.Folio.Should().MatchRegex(@"^TR-\d{8}-[0-9A-F]{5}$");
        dto.PreparadoPorId.Should().Be(Preparador);
        dto.Piezas.Select(p => p.NumeroPedido).Should().OnlyContain(n => n == "P1");
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT && p.SucursalId == EntornoPt.Sucursal)).Should().BeTrue();
        (await t.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.TrasladoSalida)).Should().Be(0);
        t.Eventos.Eventos.OfType<TraspasoSalidaAutorizadaEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Autorizar_otra_persona_pasa_las_piezas_a_transito_con_movimiento_evento_y_aviso_al_destino()
    {
        using var t = await Preparar();
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"]), default);
        var aviso = new AvisoFalso();
        var dto = await Autorizacion(t, aviso: aviso).Handle(new AutorizarTraspasoCommand(preparado.Id), default);

        dto.Estado.Should().Be("EnTransito");
        dto.AutorizadoPorId.Should().Be(Autorizador);
        dto.Total.Should().Be(2);
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnTransito && p.SucursalId == EntornoPt.Sucursal)).Should().BeTrue();
        var salidas = await t.Db.Movimientos.Where(m => m.Tipo == TipoMovimientoPieza.TrasladoSalida).ToListAsync();
        salidas.Should().HaveCount(2).And.OnlyContain(m => m.Referencia == dto.Id.ToString() && m.SucursalOrigenId == EntornoPt.Sucursal && m.SucursalDestinoId == Destino);
        var evento = t.Eventos.Eventos.OfType<TraspasoSalidaAutorizadaEvent>().Single();
        evento.SucursalOrigenId.Should().Be(EntornoPt.Sucursal);
        evento.Pedidos.Should().ContainSingle().Which.Posiciones.Should().ContainSingle().Which.Piezas.Should().Be(2);
        aviso.Avisos.Should().ContainSingle().Which.Should().Match<AvisoTraspasoPorRecibir>(a => a.SucursalDestinoId == Destino && a.Folio == dto.Folio && a.Piezas == 2);
    }

    [Fact]
    public async Task Quien_prepara_no_puede_autorizar_y_nada_se_mueve()
    {
        using var t = await Preparar();
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);

        var mismo = async () => await Autorizacion(t, acceso: Acceso(usuario: Preparador)).Handle(new AutorizarTraspasoCommand(preparado.Id), default);

        await mismo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_MISMO_USUARIO");
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT)).Should().BeTrue();
        (await t.Db.Traspasos.SingleAsync()).Estado.Should().Be(EstadoTraspaso.Preparado);
        t.Eventos.Eventos.OfType<TraspasoSalidaAutorizadaEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Un_traspaso_sin_autorizar_no_se_recibe()
    {
        using var t = await Preparar(1);
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);

        var recibir = async () => await Recepcion(t).Handle(new RecibirTraspasoCommand(preparado.Id, null), default);

        await recibir.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_NO_AUTORIZADO");
    }

    [Fact]
    public async Task Autorizar_dos_veces_o_con_el_aviso_caido_no_rompe_la_autorizacion()
    {
        using var t = await Preparar(1);
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);

        var dto = await Autorizacion(t, aviso: new AvisoFalso(falla: true)).Handle(new AutorizarTraspasoCommand(preparado.Id), default);
        dto.Estado.Should().Be("EnTransito");

        var otra = async () => await Autorizacion(t).Handle(new AutorizarTraspasoCommand(preparado.Id), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_NO_PREPARADO");
    }

    [Fact]
    public async Task Foranea_sin_vehiculo_u_operador_no_se_prepara()
    {
        using var t = await Preparar(1);
        var sinVehiculo = new TransporteTraspaso("Cancún", null, Operador, 1300m, 800m, "97300", "YUC", "77500", "ROO");

        var f = async () => await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"], true, sinVehiculo), default);

        await f.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_CARTA_PORTE_INCOMPLETA");
        (await t.Db.Traspasos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Foranea_emite_la_Carta_Porte_al_autorizar_y_la_guarda_en_el_traspaso()
    {
        using var t = await Preparar();
        var transporte = new TransporteTraspaso("Cancún", Vehiculo, Operador, 1300m, 800m, "97300", "yuc", "77500", "ROO");
        var preparado = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1", "E2"], true, transporte), default);
        preparado.Foranea.Should().BeTrue();
        var fac = new FacturacionFalsa();

        var dto = await Autorizacion(t, fac).Handle(new AutorizarTraspasoCommand(preparado.Id), default);

        dto.CartaPorteFolio.Should().Be("CP-TR-1");
        var cp = fac.CartasPorte.Should().ContainSingle().Which;
        cp.Should().Match<SolicitudCartaPorte>(c => c.VehiculoId == Vehiculo && c.OperadorId == Operador && c.OrigenEstado == "YUC" && c.DestinoEstado == "ROO" && c.PesoKg == 800m);
        (await t.Db.Traspasos.SingleAsync()).CartaPorteId.Should().NotBeNull();
    }

    [Fact]
    public async Task Recepcion_total_deja_las_piezas_en_PT_de_destino_y_asienta_la_entrada()
    {
        using var t = await Preparar();
        var salida = await EnTransito(t, "E1", "E2");
        var dto = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);

        dto.Estado.Should().Be("Recibido");
        dto.Pendientes.Should().Be(0);
        (await t.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT && p.SucursalId == Destino)).Should().BeTrue();
        (await t.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.TrasladoRecepcion)).Should().Be(2);
        var entrada = t.Eventos.Eventos.OfType<TraspasoEntradaRegistradaEvent>().Single();
        entrada.Should().Match<TraspasoEntradaRegistradaEvent>(e => e.Resultado == "Recepcion" && e.SucursalId == Destino);
    }

    [Fact]
    public async Task Recepcion_con_una_pieza_de_menos_queda_ConDiferencia_y_la_que_llega_tarde_cierra_en_Recibido()
    {
        using var t = await Preparar();
        var salida = await EnTransito(t, "E1", "E2");

        var dto = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [await PiezaId(t, "E1")]), default);

        dto.Estado.Should().Be("ConDiferencia");
        dto.Pendientes.Should().Be(1);
        (await t.Db.Piezas.SingleAsync(p => p.Etiqueta == "E2")).Estado.Should().Be(EstadoPieza.EnTransito);

        // Sin clasificar, la que llegaba tarde se recibe como cualquier pieza y no hubo diferencia que aclarar.
        var final = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        final.Estado.Should().Be("Recibido");
    }

    [Fact]
    public async Task Diez_piezas_se_reciben_ocho_hay_que_clasificar_dos_y_al_aclararlas_se_cierra()
    {
        using var t = await Preparar(10);
        var salida = await EnTransito(t, Enumerable.Range(1, 10).Select(i => $"E{i}").ToArray());
        var llegan = new List<Guid>();
        for (var i = 1; i <= 8; i++) llegan.Add(await PiezaId(t, $"E{i}"));
        var faltante = await PiezaId(t, "E9");
        var danada = await PiezaId(t, "E10");

        var parcial = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, llegan), default);
        parcial.Estado.Should().Be("ConDiferencia");
        parcial.Pendientes.Should().Be(2);

        // Sin clasificar no se aclara.
        var sinClasificar = async () => await Aclaracion(t).Handle(new AclararDiferenciaTraspasoCommand(salida.Id, faltante, "RegresadaAlOrigen"), default);
        await sinClasificar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_SIN_CLASIFICAR");

        // El motivo es obligatorio y la evidencia, si llegó dañada.
        var sinMotivo = async () => await Clasificacion(t).Handle(new ClasificarDiferenciasTraspasoCommand(salida.Id, [new(faltante, "Faltante", " ", null)]), default);
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_MOTIVO_REQUERIDO");
        var sinEvidencia = async () => await Clasificacion(t).Handle(new ClasificarDiferenciasTraspasoCommand(salida.Id, [new(danada, "DanadaEnTransito", "Llegó rota del chofer", null)]), default);
        await sinEvidencia.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_EVIDENCIA_REQUERIDA");

        var clasificado = await Clasificacion(t).Handle(new ClasificarDiferenciasTraspasoCommand(salida.Id,
            [new(faltante, "Faltante", "No venía en el camión", null), new(danada, "DanadaEnTransito", "Llegó rota", "blob://evidencias/rota.jpg")]), default);
        clasificado.Piezas.Single(p => p.PiezaId == danada).Should().Match<TraspasoPiezaDto>(p => p.Clasificacion == "DanadaEnTransito" && p.Evidencia == "blob://evidencias/rota.jpg" && p.Motivo == "Llegó rota");

        // Una regresa al origen: sigue sin cerrar.
        var regreso = await Aclaracion(t).Handle(new AclararDiferenciaTraspasoCommand(salida.Id, faltante, "RegresadaAlOrigen"), default);
        regreso.Estado.Should().Be("ConDiferencia");
        var pz = await t.Db.Piezas.SingleAsync(p => p.Id == faltante);
        (pz.Estado, pz.SucursalId).Should().Be((EstadoPieza.EnPT, EntornoPt.Sucursal));
        (await t.Db.Movimientos.SingleAsync(m => m.PiezaId == faltante && m.Tipo == TipoMovimientoPieza.TrasladoRegreso)).Referencia.Should().Be(salida.Id.ToString());

        // La otra se da por perdida: queda en revisión para Calidad y el traspaso se cierra.
        var cierre = await Aclaracion(t).Handle(new AclararDiferenciaTraspasoCommand(salida.Id, danada, "DadaPorPerdida"), default);
        cierre.Estado.Should().Be("Cerrado");
        cierre.Pendientes.Should().Be(0);
        cierre.RecibidoAt.Should().NotBeNull();
        (await t.Db.Piezas.SingleAsync(p => p.Id == danada)).Estado.Should().Be(EstadoPieza.EnRevision);
        (await t.Db.Movimientos.CountAsync(m => m.PiezaId == danada && m.Tipo == TipoMovimientoPieza.TrasladoPerdida)).Should().Be(1);

        // Cada pieza que sale entra una vez: 8 recibidas + 1 regreso + 1 pérdida = 10, el puente cierra en cero.
        var entradas = t.Eventos.Eventos.OfType<TraspasoEntradaRegistradaEvent>().ToList();
        entradas.Sum(e => e.Pedidos.Sum(p => p.Posiciones.Sum(x => x.Piezas))).Should().Be(10);
        entradas.Select(e => (e.Resultado, e.SucursalId)).Should().BeEquivalentTo([("Recepcion", Destino), ("RegresoOrigen", EntornoPt.Sucursal), ("Perdida", Destino)]);

        var otra = async () => await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_NO_AUTORIZADO");
    }

    [Fact]
    public async Task La_pieza_clasificada_que_llega_tarde_queda_aclarada_y_cierra_el_traspaso()
    {
        using var t = await Preparar();
        var salida = await EnTransito(t, "E1", "E2");
        await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [await PiezaId(t, "E1")]), default);
        var tarde = await PiezaId(t, "E2");
        await Clasificacion(t).Handle(new ClasificarDiferenciasTraspasoCommand(salida.Id, [new(tarde, "EnCuarentena", "Retenida en la otra sucursal", null)]), default);

        var dto = await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [tarde]), default);

        dto.Estado.Should().Be("Cerrado");
        dto.Piezas.Single(p => p.PiezaId == tarde).Resolucion.Should().Be("RecibidaTarde");
        (await t.Db.Piezas.SingleAsync(p => p.Id == tarde)).SucursalId.Should().Be(Destino);
    }

    [Fact]
    public async Task Piezas_en_transito_muestran_destino_y_no_cuentan_como_EnPt_en_el_origen()
    {
        using var t = await Preparar();
        await EnTransito(t, "E1");

        var r = await new Consultas.ListarPiezasHandler(t.Db, Acceso()).Handle(new Consultas.ListarPiezasQuery("P1", EntornoPt.Sucursal, 1, 50), default);

        var enTransito = r.Items.Single(p => p.Estado == "EnTransito");
        enTransito.Etiqueta.Should().Be("E1");
        enTransito.SucursalDestinoId.Should().Be(Destino);
        r.Items.Single(p => p.Etiqueta == "E2").SucursalDestinoId.Should().BeNull();
        r.Avance.Single().EnPt.Should().Be(1);
    }

    [Fact]
    public async Task Por_recibir_lista_los_traspasos_en_transito_y_con_diferencia_del_destino()
    {
        using var t = await Preparar(3);
        var uno = await EnTransito(t, "E1");
        await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E2"]), default);
        var otroDestino = await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Guid.NewGuid(), ["E3"]), default);
        otroDestino.Estado.Should().Be("Preparado");

        var lista = await new ListarTraspasosHandler(t.Db, Acceso(null, null, EntornoPt.Sucursal, Destino)).Handle(new ListarTraspasosQuery("EnTransito,ConDiferencia", null, 1, 50, Destino), default);

        lista.Items.Should().ContainSingle().Which.Id.Should().Be(uno.Id);
    }

    [Fact]
    public async Task Pieza_ya_recibida_no_se_recibe_dos_veces()
    {
        using var t = await Preparar(2);
        var salida = await EnTransito(t, "E1", "E2");
        var id = await PiezaId(t, "E1");
        await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [id]), default);

        var otra = async () => await Recepcion(t).Handle(new RecibirTraspasoCommand(salida.Id, [id]), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_RECEPCION_INVALIDA");
    }

    [Fact]
    public async Task Pieza_preparada_o_en_transito_no_puede_ir_en_otro_traspaso()
    {
        using var t = await Preparar(2);
        await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        var preparada = async () => await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        await preparada.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_PIEZA_YA_PREPARADA");

        await EnTransito(t, "E2");
        var enTransito = async () => await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E2"]), default);
        await enTransito.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_PIEZA_NO_EN_PT");
    }

    [Fact]
    public async Task Pieza_de_otra_sucursal_o_etiqueta_inexistente_no_se_prepara()
    {
        using var t = await Preparar(1);
        var otraSucursal = async () => await Preparacion(t).Handle(new CrearTraspasoCommand(Destino, EntornoPt.Sucursal, ["E1"]), default);
        await otraSucursal.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_PIEZA_OTRA_SUCURSAL");
        var inexistente = async () => await Preparacion(t).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["NO-EXISTE"]), default);
        await inexistente.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PT_TRASPASO_PIEZAS_INVALIDAS");
    }

    [Fact]
    public async Task Sin_permiso_o_fuera_de_alcance_no_opera()
    {
        using var t = await Preparar(1);
        var soloVer = async () => await Preparacion(t, Acceso([PermisosCanonicos.ProductoTerminadoPiezasVer])).Handle(new CrearTraspasoCommand(EntornoPt.Sucursal, Destino, ["E1"]), default);
        await soloVer.Should().ThrowAsync<ForbiddenException>();

        var salida = await EnTransito(t, "E1");
        var origenRecibe = async () => await Recepcion(t, Acceso(null, null, EntornoPt.Sucursal)).Handle(new RecibirTraspasoCommand(salida.Id, null), default);
        await origenRecibe.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void Origen_igual_a_destino_o_sin_piezas_no_crea_traspaso()
    {
        var f = () => TraspasoPt.Crear(EntornoPt.Empresa, Destino, Destino, [Guid.NewGuid()], Preparador, Ahora);
        f.Should().Throw<BusinessRuleException>();
        var g = () => TraspasoPt.Crear(EntornoPt.Empresa, EntornoPt.Sucursal, Destino, [], Preparador, Ahora);
        g.Should().Throw<BusinessRuleException>();
    }
}
