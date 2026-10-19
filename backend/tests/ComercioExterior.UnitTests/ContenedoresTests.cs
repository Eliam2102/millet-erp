using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class ContenedoresTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 16);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static Expediente Nuevo(TipoExpediente tipo = TipoExpediente.Exportacion) => new(Guid.NewGuid(), Guid.NewGuid(), "CE-EXP-TEST", tipo, Sucursal, "DEMO", tipo == TipoExpediente.Importacion ? Guid.NewGuid() : null, Guid.NewGuid(), "USD", Actor, ["DEMO cliente"]);
    private static PedidoEmbarcable Pedido(Expediente e, decimal cantidad = 100, Guid? cliente = null)
    {
        var id = cliente ?? Guid.NewGuid();
        if (!e.Participantes.Any(p => p.ClienteId == id)) e.AgregarParticipante(Guid.NewGuid(), "DEMO cliente", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, id);
        return new(Guid.NewGuid(), "DEMO-PED", id, "DEMO cliente", Sucursal, true, "Importado", 1, [new(1, null, "Descripción solo de consulta", cantidad, "H87")], null, null);
    }
    private static Guid Contenedor(Expediente e, bool completo = true)
    {
        var id = Guid.NewGuid(); e.GuardarContenedor(id, new(completo ? "DEMO-C" : null, "S", "B", completo ? "DEMO-BL" : null, "R", Hoy, 10, 1)); return id;
    }
    private static void Pedimento(Expediente e, string clave = "A1") => e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", clave, Hoy, Hoy);
    private static void Codigo(string code, Action action) => Assert.Equal(code, Assert.Throws<BusinessRuleException>(action).Code);

    [Fact]
    public void Cancelar_expediente_libera_partidas_y_vinculos_con_historial_y_rechaza_facturas()
    {
        var e = Nuevo(); var c = Contenedor(e); var p = Pedido(e);
        e.AsignarPartida(c, p, 1, 100, Actor);
        Assert.Equal("CE_EXPEDIENTE_CON_FACTURAS", Assert.Throws<ConflictException>(() => e.Cancelar("DEMO cancelar", true)).Code);
        Assert.Equal(EstadoExpediente.Abierto, e.Estado);
        e.Cancelar("DEMO cancelar");
        Assert.Equal(EstadoPartida.Liberada, Assert.Single(e.Partidas).EstadoActual(999));
        Assert.False(Assert.Single(e.VinculosPedidos).Activo);
        Assert.Equal(EstadoPartida.Liberada, Assert.Single(e.Partidas).Estado);
    }
    [Fact]
    public void Fecha_pedimento_posterior_a_embarque_no_impone_regla_no_confirmada()
    {
        var e = Nuevo(); var c = Contenedor(e); Pedimento(e);
        e.RegistrarEmbarque(c, Hoy.AddDays(-1), Hoy);
        Assert.Equal(Hoy.AddDays(-1), e.Contenedor(c).FechaEmbarqueReal);
    }
    [Fact]
    public void K1_varios_pedidos_clientes_y_contenedores_sin_tope_y_rechazo_importacion()
    {
        var e = Nuevo(); var c1 = Contenedor(e); var c2 = Contenedor(e);
        for (var i = 0; i < 9; i++) { var p = Pedido(e); e.AsignarPartida(c1, p, 1, 60, Actor); e.AsignarPartida(c2, p, 1, 40, Actor); }
        Assert.Equal(18, e.Partidas.Count); Assert.Equal(9, e.Partidas.Select(p => p.ClienteId).Distinct().Count());
        Codigo("CE_SOLO_EXPORTACION", () => Contenedor(Nuevo(TipoExpediente.Importacion)));
    }
    [Fact]
    public void K2_cada_parcialidad_es_un_pedido_completo_y_solo_cuenta_lo_embarcado()
    {
        var e = Nuevo(); Pedimento(e); var c1 = Contenedor(e); var c2 = Contenedor(e);
        var p1 = Pedido(e, 60); var p2 = Pedido(e, 40);
        e.AsignarPartida(c1, p1, 1, 60, Actor); e.AsignarPartida(c2, p2, 1, 40, Actor);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p1).Codigo);
        e.RegistrarEmbarque(c1, Hoy, Hoy);
        Assert.Null(e.BloqueoFacturacion(p1).Codigo); Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p2).Codigo);
        Assert.Contains("40", e.BloqueoFacturacion(p2).Motivo);
    }
    [Fact]
    public void K3_cantidad_en_todos_los_contenedores_no_excede_y_conserva_unidad()
    {
        var e = Nuevo(); var c1 = Contenedor(e); var c2 = Contenedor(e); var p = Pedido(e);
        e.AsignarPartida(c1, p, 1, 60, Actor); e.AsignarPartida(c2, p, 1, 40, Actor);
        Assert.All(e.Partidas, x => Assert.Equal("H87", x.Unidad));
        Codigo("CE_CANTIDAD_EXCEDE_PEDIDO", () => e.AsignarPartida(c2, p, 1, 1, Actor));
        Codigo("CE_PARTIDA_DATOS_INVALIDOS", () => e.AsignarPartida(c1, p, 1, -1, Actor));
    }
    [Fact]
    public void K4_cambio_de_version_requiere_reconfirmar_sin_copiar_datos_comerciales()
    {
        var e = Nuevo(); var c = Contenedor(e); Pedimento(e); var p = Pedido(e); e.AsignarPartida(c, p, 1, 100, Actor); e.RegistrarEmbarque(c, Hoy, Hoy);
        Assert.Null(e.BloqueoFacturacion(p).Codigo);
        var modificado = p with { VersionOrigen = 2 };
        Assert.Equal("CE_PARTIDA_REQUIERE_REVISION", e.BloqueoFacturacion(modificado).Codigo);
        e.ActualizarRevisiones([modificado]); Assert.Equal(EstadoPartida.RequiereRevision, e.Partidas.Single().Estado);
        var revisor = Guid.NewGuid(); e.ConfirmarPartida(e.Partidas.Single().Id, modificado, revisor);
        Assert.Null(e.BloqueoFacturacion(modificado).Codigo); Assert.Equal(revisor, e.Partidas.Single().ConfirmadoPorId);
        Assert.Equal(2, e.Partidas.Single().VersionOrigenAsignada);
        Assert.DoesNotContain(typeof(PartidaEmbarcada).GetProperties(), prop => prop.Name is "Precio" or "Descripcion" or "Total");
    }
    [Theory]
    [InlineData("Facturado")][InlineData("Cancelado")][InlineData("Excepcion")][InlineData("Bloqueado")]
    public void K5_pedido_importado_del_cliente_y_exportacion_son_obligatorios(string estado)
    {
        var e = Nuevo(); var c = Contenedor(e); var p = Pedido(e); e.AsignarPartida(c, p, 1, 10, Actor);
        Codigo("CE_PEDIDO_NO_ASIGNABLE", () => e.AsignarPartida(c, p with { Estado = estado }, 1, 1, Actor));
        Codigo("CE_PEDIDO_NO_ASIGNABLE", () => e.AsignarPartida(c, p with { EsExportacion = false }, 1, 1, Actor));
        Codigo("CE_PEDIDO_NO_ASIGNABLE", () => e.AsignarPartida(c, p with { ClienteId = Guid.NewGuid() }, 1, 1, Actor));
        Codigo("CE_SUCURSAL_DISTINTA", () => e.AsignarPartida(c, p with { SucursalId = Guid.NewGuid() }, 1, 1, Actor));
    }
    [Theory]
    [InlineData("A1")][InlineData("RT")]
    public void K6_sin_pedimento_no_embarca_con_pedimento_vigente_si(string clave)
    {
        var e = Nuevo(); var c = Contenedor(e); Codigo("CE_EMBARQUE_SIN_PEDIMENTO", () => e.RegistrarEmbarque(c, Hoy, Hoy));
        Pedimento(e, clave); var incompleto = Contenedor(e, false);
        Codigo("CE_EMBARQUE_DATOS_REQUERIDOS", () => e.RegistrarEmbarque(incompleto, Hoy, Hoy));
        Codigo("CE_FECHA_EMBARQUE_INVALIDA", () => e.RegistrarEmbarque(c, Hoy.AddDays(1), Hoy));
        e.RegistrarEmbarque(c, Hoy, Hoy); Assert.Equal(Hoy, e.Contenedor(c).FechaEmbarqueReal);
    }
    [Fact]
    public void K7_pedimento_versionado_usa_numero_vigente_y_marca_revision()
    {
        var e = Nuevo(); var tipo = new RequisitoDocumento(Guid.NewGuid(), "EXP_PEDIMENTO", "Pedimento", true, false);
        var doc = Guid.NewGuid(); var adjunto = Guid.NewGuid(); e.RegistrarDocumento(doc, tipo, adjunto, null, Actor, "DEMO-ORIGINAL");
        e.RegistrarPedimentoExportacion("", "A1", Hoy, Hoy, doc); Assert.Equal("DEMO-ORIGINAL", e.NumeroPedimentoExportacion);
        Assert.True(e.PedimentoExportacionVigente([adjunto])); Assert.False(e.PedimentoExportacionVigente([]));
        var nuevo = Guid.NewGuid(); e.RegistrarDocumento(nuevo, tipo, Guid.NewGuid(), "DEMO rectificación", Actor, "DEMO-RECTIFICADO");
        Assert.Equal("DEMO-RECTIFICADO", e.NumeroPedimentoExportacion); Assert.Equal(nuevo, e.DocumentoPedimentoExportacionId); Assert.True(e.RevisionPendiente);
        Codigo("CE_PEDIMENTO_NO_VIGENTE", () => e.RegistrarPedimentoExportacion("N", "A1", Hoy, Hoy, doc));
        Codigo("CE_PEDIMENTO_INVALIDO", () => e.RegistrarPedimentoExportacion("N", "ZZ", Hoy, Hoy));
    }
    [Fact]
    public void K9_cancelar_no_cancela_pedido_y_no_se_quita_con_factura()
    {
        var e = Nuevo(); var c1 = Contenedor(e); var c2 = Contenedor(e); var p = Pedido(e); Pedimento(e); e.AsignarPartida(c1, p, 1, 100, Actor);
        Assert.Equal("CE_CONTENEDOR_CON_FACTURA", Assert.Throws<ConflictException>(() => e.CancelarContenedor(c1, "DEMO cancelación", true)).Code);
        Assert.Equal(EstadoContenedor.Activo, e.Contenedor(c1).Estado);
        e.CancelarContenedor(c1, "DEMO booking cancelado", false); Assert.Single(e.Partidas); Assert.Equal("Importado", p.Estado);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p).Codigo);
        e.AsignarPartida(c2, p, 1, 100, Actor); e.RegistrarEmbarque(c2, Hoy, Hoy); Assert.Null(e.BloqueoFacturacion(p).Codigo);
    }
    [Fact]
    public void Pedido_reducido_no_se_confirma_hasta_corregir_asignaciones()
    {
        var e = Nuevo(); var c = Contenedor(e); var p = Pedido(e); e.AsignarPartida(c, p, 1, 100, Actor);
        var mod = p with { VersionOrigen = 2, Lineas = [new(1, null, "Cambio", 80, "H87")] };
        Codigo("CE_CANTIDAD_EXCEDE_PEDIDO", () => e.ConfirmarPartida(e.Partidas.Single().Id, mod, Actor));
        e.QuitarPartida(e.Partidas.Single().Id, false); e.AsignarPartida(c, mod, 1, 80, Actor); Assert.Equal(80, e.Partidas.Single().Cantidad);
    }
}
