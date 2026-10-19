using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;
public sealed class OtrTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Cliente = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static OtrContenido Contenido(decimal peso = 100) => new(Cliente, Sucursal, "DEMO cliente", "DEMO entrega", "DEMO vidrio", new("DEMO-C", null, null, null, "DEMO-R", new(2026, 10, 20), peso, 2), [new("DEMO-PED", 1, 10, "H87")]);
    private static Expediente Expediente(Guid? cliente = null)
    {
        var e = new Expediente(Guid.NewGuid(), Empresa, "CE-EXP-DEMO", TipoExpediente.Exportacion, Sucursal, "DEMO", null, Guid.NewGuid(), "USD", Actor, ["DEMO cliente"]);
        e.AgregarParticipante(Guid.NewGuid(), "DEMO cliente", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, cliente ?? Cliente); return e;
    }
    private static OtrEnvio Envio() { var o = new OtrEnvio(Empresa, "DEMO-OTR"); o.Aplicar(1, Contenido()); return o; }
    private static void Codigo(string code, Action action) => Assert.Equal(code, Assert.Throws<BusinessRuleException>(action).Code);
    [Fact]
    public void O3_version_repetida_o_menor_no_cambia_hash_contenido_ni_lineas()
    {
        var o = Envio(); Assert.True(o.Aplicar(2, Contenido(125))); var hash = o.Hash; var json = o.ContenidoJson; var lineas = o.Lineas.Count;
        Assert.False(o.Aplicar(1, Contenido(999))); Assert.False(o.Aplicar(2, Contenido(125)));
        Codigo("CE_OTR_VERSION_CONTENIDO_DISTINTO", () => o.Aplicar(2, Contenido(999))); Assert.Equal(hash, o.Hash); Assert.Equal(json, o.ContenidoJson); Assert.Equal(lineas, o.Lineas.Count);
        Codigo("CE_OTR_VERSION_INVALIDA", () => o.Aplicar(0, Contenido()));
        Assert.True(o.Aplicar(3, Contenido(130))); Assert.NotEqual(hash, o.Hash);
    }
    [Fact]
    public void O4_O5_conflictos_no_sobrescriben_y_la_decision_se_audita()
    {
        var o = Envio(); var e = Expediente(); o.Vincular(e); e.GuardarContenedor(o.ContenedorId!.Value, o.Conciliar(null, Ahora));
        var c = e.Contenedor(o.ContenedorId.Value); var antes = OtrEnvio.Datos(c); var manual = antes with { PesoKg = 150, Booking = "DEMO-ERP" };
        o.RegistrarEdicion(antes, manual, Actor.ToString(), Ahora); e.GuardarContenedor(c.Id, manual);
        var nuevo = Contenido(200) with { Contenedor = Contenido(200).Contenedor with { Booking = "DEMO-OTR-BOOK" } }; o.Aplicar(2, nuevo);
        e.GuardarContenedor(c.Id, o.Conciliar(c, Ahora.AddMinutes(1))); Assert.Equal(150, c.PesoKg); Assert.Equal("DEMO-ERP", c.Booking); Assert.Equal(2, o.Conflictos.Count);
        var conflicto = o.Conflictos.Single(x => x.Campo == "PesoKg");
        Codigo("CE_MOTIVO_INVALIDO", () => o.Resolver(conflicto.Id, true, "no", Actor, Ahora, c));
        e.GuardarContenedor(c.Id, o.Resolver(conflicto.Id, true, "DEMO usar el peso OTR", Actor, Ahora, c)); Assert.Equal(200, c.PesoKg);
        Assert.Equal(Actor, conflicto.ResueltoPorId); Assert.True(conflicto.UsarOtr); Assert.Equal(Actor.ToString(), conflicto.ActorErp);
        Codigo("CE_OTR_CONFLICTO_RESUELTO", () => o.Resolver(conflicto.Id, false, "DEMO conservar", Actor, Ahora, c));
        var booking = o.Conflictos.Single(x => x.Campo == "Booking"); e.GuardarContenedor(c.Id, o.Resolver(booking.Id, false, "DEMO conservar booking", Actor, Ahora, c)); Assert.Equal("DEMO-ERP", c.Booking);
    }
    [Fact]
    public void O5_asignacion_manual_se_conserva_y_el_conflicto_de_piezas_se_resuelve_con_auditoria()
    {
        var o = Envio(); var clave = OtrEnvio.ClavePartida("DEMO-PED", 1);
        var actual = OtrEnvio.ValorPartida(6, "H87");
        o.RegistrarEdicionPartidas(new Dictionary<string, string> { [clave] = OtrEnvio.ValorPartida(10, "H87") }, new Dictionary<string, string> { [clave] = actual }, Actor.ToString(), Ahora);
        o.Aplicar(2, Contenido()); var linea = o.Lineas.Single(l => l.VersionOrigen == 2);
        Assert.True(o.ConflictoPartida(linea, actual, Ahora)); Assert.Equal(EstadoOtrLinea.Pendiente, linea.Estado);
        var conflicto = o.Conflictos.Single(); Assert.Equal(actual, conflicto.ValorErp); Assert.Equal(OtrEnvio.ValorPartida(10, "H87"), conflicto.ValorOtr);
        Codigo("CE_OTR_CONFLICTO_DESACTUALIZADO", () => o.ResolverValor(conflicto.Id, true, "DEMO resolución", Actor, Ahora, OtrEnvio.ValorPartida(5, "H87"), clave));
        o.ResolverValor(conflicto.Id, false, "DEMO conservar piezas ERP", Actor, Ahora, actual, clave); Assert.False(conflicto.UsarOtr); Assert.Equal(Actor, conflicto.ResueltoPorId);
        o.Aplicar(3, Contenido()); var siguiente = o.Lineas.Single(l => l.VersionOrigen == 3); Assert.True(o.ConflictoPartida(siguiente, actual, Ahora));
        var nuevo = o.Conflictos.Single(c => c.VersionOrigen == 3); o.ResolverValor(nuevo.Id, true, "DEMO usar piezas OTR", Actor, Ahora, actual, clave); Assert.True(nuevo.UsarOtr);
        o.Aplicar(4, Contenido()); Assert.False(o.ConflictoPartida(o.Lineas.Single(l => l.VersionOrigen == 4), actual, Ahora));
    }
    [Fact]
    public void O5_no_aplica_una_decision_con_valores_desactualizados()
    {
        var o = Envio(); var e = Expediente(); o.Vincular(e);
        var datos = o.Conciliar(null, Ahora); e.GuardarContenedor(o.ContenedorId!.Value, datos with { Booking = "DEMO-ERP" });
        o.Aplicar(2, Contenido() with { Contenedor = Contenido().Contenedor with { Booking = "DEMO-OTR" } }); var c = e.Contenedor(o.ContenedorId.Value); o.Conciliar(c, Ahora);
        e.GuardarContenedor(c.Id, OtrEnvio.Datos(c) with { Booking = "DEMO-OTRO" });
        Codigo("CE_OTR_CONFLICTO_DESACTUALIZADO", () => o.Resolver(o.Conflictos.Single().Id, true, "DEMO resolución", Actor, Ahora, c));
    }
    [Fact]
    public void O6_propuesta_exige_cantidad_y_unidad_validas_sin_inventar_pedido()
    {
        var o = Envio(); var l = Assert.Single(o.Lineas); l.Pendiente("CE_OTR_PEDIDO_INEXISTENTE"); Assert.Equal(EstadoOtrLinea.Pendiente, l.Estado); Assert.Null(l.PartidaId);
        var e = Expediente(); o.Vincular(e); e.GuardarContenedor(o.ContenedorId!.Value, o.Conciliar(null, Ahora));
        var p = new PedidoEmbarcable(Guid.NewGuid(), "DEMO-PED", Cliente, "DEMO", Sucursal, true, "Importado", 1, [new(1, null, "DEMO", 10, "H87")], null, null);
        Codigo("CE_CANTIDAD_EXCEDE_PEDIDO", () => e.AsignarPartida(o.ContenedorId.Value, p, 1, 11, Actor));
        e.AsignarPartida(o.ContenedorId.Value, p, 1, 10, Actor); l.Proponer(e.Partidas.Single().Id); Assert.Equal(EstadoOtrLinea.Propuesta, l.Estado);
        Codigo("CE_OTR_DATOS_INVALIDOS", () => o.Aplicar(2, Contenido() with { Lineas = [new("DEMO-PED", 1, -1, "H87")] }));
    }
    [Fact]
    public void O7_solo_exportacion_abierta_del_cliente_y_un_expediente()
    {
        var o = Envio(); var e = Expediente(); Assert.Equal(EstadoOtrEnvio.PorVincular, o.Estado);
        Codigo("CE_OTR_CLIENTE_DISTINTO", () => o.Vincular(Expediente(Guid.NewGuid())));
        var cancelado = Expediente(); cancelado.Cancelar("DEMO cancelar"); Codigo("CE_EXPEDIENTE_NO_ABIERTO", () => o.Vincular(cancelado));
        o.Vincular(e); Assert.Equal(e.Id, o.ExpedienteId); Codigo("CE_OTR_YA_VINCULADO", () => o.Vincular(Expediente()));
    }
    [Fact]
    public void O8_salida_unica_con_acuse_y_reintento_hasta_maximo()
    {
        var o = Envio(); var s = o.CrearSalida("hito", 1, "{}", []);
        Codigo("CE_OTR_SALIDA_DUPLICADA", () => o.CrearSalida("hito", 1, "{}", [s]));
        Assert.Equal(EstadoOtrSalida.Pendiente, s.Estado); s.Intentar(2, Ahora); s.ErrorEntrega(2, "DEMO error"); Assert.Equal(EstadoOtrSalida.Pendiente, s.Estado);
        s.Intentar(2, Ahora); s.ErrorEntrega(2, "DEMO error"); Assert.Equal(EstadoOtrSalida.Fallida, s.Estado); Assert.Equal(2, s.Intentos);
        Codigo("CE_OTR_SALIDA_NO_REINTENTABLE", () => s.Intentar(2, Ahora)); var nueva = s.Reintentar();
        Assert.Equal(EstadoOtrSalida.Reemplazada, s.Estado); Assert.Equal(nueva.Id, s.ReemplazadaPorId); Assert.NotEqual(s.Id, nueva.Id);
        Assert.Equal(2, nueva.Intento); Assert.Equal(s.VersionErp, nueva.VersionErp); Assert.Equal(0, nueva.Intentos);
        nueva.Intentar(2, Ahora); nueva.Entregada(); nueva.Acusar(Ahora, "Aplicada", "DEMO recibido"); Assert.Equal(EstadoOtrSalida.Acusada, nueva.Estado);
        Codigo("CE_OTR_SALIDA_NO_FALLIDA", () => nueva.Reintentar());
        var rechazo = o.CrearSalida("booking", 2, "{}", []); rechazo.Intentar(2, Ahora); rechazo.Acusar(Ahora, "Rechazada", "DEMO revisar"); Assert.Equal(EstadoOtrSalida.Fallida, rechazo.Estado);
        var reentrega = rechazo.Reintentar(); Assert.Equal("Rechazada", rechazo.AcuseResultado); Assert.Equal(reentrega.Id, rechazo.ReemplazadaPorId);
        reentrega.Intentar(2, Ahora); reentrega.Entregada(); reentrega.Acusar(Ahora.AddDays(2), "Aplicada", "DEMO acuse tardío");
        Assert.Equal(EstadoOtrSalida.Acusada, reentrega.Estado); Assert.Equal(EstadoOtrSalida.Reemplazada, rechazo.Estado);
    }
    [Fact]
    public void O6_propuesta_no_atribuye_confirmacion_humana_hasta_confirmar_en_ce()
    {
        var o = Envio(); var e = Expediente(); o.Vincular(e); e.GuardarContenedor(o.ContenedorId!.Value, o.Conciliar(null, Ahora));
        var p = new PedidoEmbarcable(Guid.NewGuid(), "DEMO-PED", Cliente, "DEMO", Sucursal, true, "Importado", 1, [new(1, null, "DEMO", 10, "H87")], null, null);
        e.AsignarPartida(o.ContenedorId.Value, p, 1, 10, Guid.Empty, propuesta: true);
        var partida = e.Partidas.Single(); Assert.Equal(Guid.Empty, partida.ConfirmadoPorId); Assert.Equal(EstadoPartida.RequiereRevision, partida.Estado);
        e.ConfirmarPartida(partida.Id, p, Actor); Assert.Equal(Actor, partida.ConfirmadoPorId); Assert.Equal(EstadoPartida.Vigente, partida.Estado);
    }
    [Fact]
    public void Version_nueva_supera_conflictos_abiertos_y_conserva_referencia()
    {
        var o = Envio();
        o.Aplicar(2, Contenido() with { Contenedor = Contenido().Contenedor with { Booking = "DEMO-BOOK" } });
        o.Conciliar(null, Ahora); var conflicto = Assert.Single(o.Conflictos);
        Assert.Equal("Pendiente", conflicto.Estado);
        o.Aplicar(3, Contenido());
        Assert.Equal("Superado", conflicto.Estado); Assert.Equal(3, conflicto.SuperadoPorVersion);
        Assert.DoesNotContain(o.Conflictos, c => c.Estado == "Pendiente");
        Codigo("CE_OTR_CONFLICTO_RESUELTO", () => o.ResolverValor(conflicto.Id, true, "DEMO decisión", Actor, Ahora, conflicto.ValorErp));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Identidad_aw_desconocida_se_conserva_por_vincular(bool cliente)
    {
        var o = new OtrEnvio(Empresa, "DEMO-SIN-MAPEO");
        o.Aplicar(1, Contenido() with { ClienteId = cliente ? Guid.Empty : Cliente, SucursalId = cliente ? Sucursal : Guid.Empty, ClienteNumeroAw = "DEMO-CLI", SucursalClaveAw = "DEMO-SUC" });
        Assert.Equal(EstadoOtrEnvio.PorVincular, o.Estado);
        Codigo(cliente ? "CE_OTR_CLIENTE_DESCONOCIDO" : "CE_OTR_SUCURSAL_DESCONOCIDA", o.ExigirIdentidad);
    }
    [Fact]
    public void O9_cancelacion_y_cambio_ponen_partidas_en_revision_sin_cancelar_pedido()
    {
        var o = Envio(); var e = Expediente(); o.Vincular(e); e.GuardarContenedor(o.ContenedorId!.Value, o.Conciliar(null, Ahora));
        var p = new PedidoEmbarcable(Guid.NewGuid(), "DEMO-PED", Cliente, "DEMO", Sucursal, true, "Importado", 1, [new(1, null, "DEMO", 10, "H87")], null, null);
        e.AsignarPartida(o.ContenedorId.Value, p, 1, 10, Actor); var partida = e.Partidas.Single(); o.Lineas.Single().Proponer(partida.Id);
        o.Aplicar(2, Contenido() with { Cancelado = true }); e.RevisarPartidasOtr([partida.Id]);
        Assert.Equal(EstadoOtrEnvio.Cancelado, o.Estado); Assert.Equal(EstadoPartida.RequiereRevision, partida.Estado); Assert.Equal("Importado", p.Estado);
        Codigo("CE_OTR_CANCELADO", () => o.Vincular(e)); Codigo("CE_OTR_CANCELADO", () => o.Aplicar(3, Contenido()));
    }
}
