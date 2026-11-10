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
    private static void Pedimento(Expediente e, string clave = "A1") => e.RegistrarPedimentoExportacion("DEMO-PEDIMENTO", clave, Hoy, Hoy, "16");
    private static void PrepararSalida(Expediente e)
    {
        foreach (var partida in e.Partidas.ToArray()) e.GuardarDatosAduanales(partida.Id, "7007199900", "06", Actor, new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero), true);
        e.AutorizarSalida(Guid.NewGuid(), new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero), null, true);
    }
    private static void Embarcar(Expediente e, Guid c, DateOnly fecha, DateOnly hoy)
    { PrepararSalida(e); e.RegistrarEmbarque(c, fecha, hoy); }
    private static void Codigo(string code, Action action) => Assert.Equal(code, Assert.Throws<BusinessRuleException>(action).Code);

    [Fact]
    public void M158_al_revisar_la_fraccion_de_exportacion_exige_10_digitos()
    {
        var e = Nuevo(); var c = Contenedor(e); Pedimento(e); var p = Pedido(e); e.AsignarPartida(c, p, 1, 100, Actor);
        var partida = e.Partidas.Single();
        var ahora = new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
        var ex = Assert.Throws<BusinessRuleException>(() => e.GuardarDatosAduanales(partida.Id, "70071999", "06", Actor, ahora, true));
        Assert.Equal("CCE_FRACCION_INVALIDA", ex.Code);
        Assert.Contains("10 dígitos", ex.Message);
        // El borrador sin revisar sí se guarda; con 10 dígitos la revisión pasa.
        e.GuardarDatosAduanales(partida.Id, "70071999", "06", Actor, ahora, false);
        e.GuardarDatosAduanales(partida.Id, "7007199900", "06", Actor, ahora, true);
        Assert.Equal("7007199900", e.Partidas.Single().FraccionArancelaria);
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("99")]
    public void Pedimento_exige_aduana_del_catalogo(string? aduana)
    {
        var e = Nuevo();
        var ex = Assert.Throws<BusinessRuleException>(() => e.RegistrarPedimentoExportacion("DEMO", "A1", Hoy, Hoy, aduana));
        Assert.Equal("CE_ADUANA_INVALIDA", ex.Code);
        Assert.Equal("Seleccione una aduana del catálogo", ex.Message);
        Assert.Null(e.NumeroPedimentoExportacion);
    }

    [Fact]
    public void Pedimento_guarda_aduana_y_libera_facturacion()
    {
        var e = Nuevo(); var c = Contenedor(e); var p = Pedido(e);
        Pedimento(e); e.AsignarPartida(c, p, 1, 100, Actor); Embarcar(e, c, Hoy, Hoy);
        Assert.Equal("16", e.AduanaPedimentoExportacion);
        Assert.Null(e.BloqueoFacturacion(p).Codigo);
    }

    [Fact]
    public void Pedimento_viejo_embarca_pero_exige_aduana_para_facturar()
    {
        var e = Nuevo(); var c = Contenedor(e); var p = Pedido(e);
        Pedimento(e); e.AsignarPartida(c, p, 1, 100, Actor);
        typeof(Expediente).GetProperty(nameof(Expediente.AduanaPedimentoExportacion))!.SetValue(e, null);
        Assert.True(e.PedimentoExportacionVigente());
        Embarcar(e, c, Hoy, Hoy);
        Assert.Equal("CE_PEDIMENTO_SIN_ADUANA", e.BloqueoFacturacion(p).Codigo);
        Pedimento(e); Assert.Null(e.BloqueoFacturacion(p).Codigo);
    }

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
        Embarcar(e, c, Hoy.AddDays(-1), Hoy);
        Assert.Equal(Hoy.AddDays(-1), e.Contenedor(c).FechaEmbarqueReal);
    }
    [Fact]
    public void K1_varios_pedidos_clientes_y_contenedores_sin_tope_e_importacion_con_gastos()
    {
        var e = Nuevo(); var c1 = Contenedor(e); var c2 = Contenedor(e);
        for (var i = 0; i < 9; i++) { var p = Pedido(e); e.AsignarPartida(c1, p, 1, 60, Actor); e.AsignarPartida(c2, p, 1, 40, Actor); }
        Assert.Equal(18, e.Partidas.Count); Assert.Equal(9, e.Partidas.Select(p => p.ClienteId).Distinct().Count());
        var importacion = Nuevo(TipoExpediente.Importacion);
        Contenedor(importacion);
        Assert.Single(importacion.Contenedores);
    }
    [Fact]
    public void K2_cada_parcialidad_es_un_pedido_completo_y_solo_cuenta_lo_embarcado()
    {
        var e = Nuevo(); Pedimento(e); var c1 = Contenedor(e); var c2 = Contenedor(e);
        var p1 = Pedido(e, 60); var p2 = Pedido(e, 40);
        e.AsignarPartida(c1, p1, 1, 60, Actor); e.AsignarPartida(c2, p2, 1, 40, Actor);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p1).Codigo);
        Embarcar(e, c1, Hoy, Hoy);
        Assert.Null(e.BloqueoFacturacion(p1).Codigo); Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p2).Codigo);
        Assert.Contains("40", e.BloqueoFacturacion(p2).Motivo);
    }
    [Fact]
    public void La_factura_parcial_exige_autorizacion_de_la_version_y_revision_aduanal_del_contenedor()
    {
        var e = Nuevo();
        var c = Contenedor(e);
        var otroContenedor = Contenedor(e);
        var pedido = Pedido(e);
        Pedimento(e);
        e.AsignarPartida(c, pedido, 1, 60, Actor);
        e.AsignarPartida(otroContenedor, pedido, 1, 40, Actor);
        var fecha = new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
        var autoriza = Guid.NewGuid();
        e.AutorizarSalida(autoriza, fecha, null, true);
        e.RegistrarEmbarque(c, Hoy, Hoy);

        Assert.Equal("CE_DATOS_ADUANALES_SIN_REVISAR", e.BloqueoFacturacion(pedido, contenedorId: c).Codigo);
        var partida = e.Partidas.Single(p => p.ContenedorId == c);
        e.GuardarDatosAduanales(partida.Id, "7007199900", "06", Actor, fecha, true);
        Assert.Equal("CE_SALIDA_SIN_AUTORIZAR", e.BloqueoFacturacion(pedido, contenedorId: c).Codigo);
        e.AutorizarSalida(autoriza, fecha, null, true);

        Assert.Null(e.BloqueoFacturacion(pedido, contenedorId: c).Codigo);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(pedido, contenedorId: otroContenedor).Codigo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Una_parcialidad_preparada_no_sale_sin_factura_o_excepcion(bool tieneFactura)
    {
        var e = Nuevo();
        var c = Contenedor(e);
        var pedido = Pedido(e, 10);
        e.AsignarPartida(c, pedido, 1, 8, Actor);
        PrepararSalida(e);
        var ahora = new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
        var aprobador = Guid.NewGuid();
        Codigo("CE_PARCIAL_SIN_AUTORIZAR", () => e.PrepararFacturaExportacion(c, [pedido], ahora));
        e.AprobarDespacho(c, true, aprobador, ahora, "credito", "Parcialidad confirmada", null);
        Assert.True(e.PrepararFacturaExportacion(c, [pedido], ahora));
        Assert.False(e.PrepararFacturaExportacion(c, [pedido], ahora));
        Assert.Null(e.Contenedor(c).FechaEmbarqueReal);
        Codigo("CE_CONTENEDOR_PREPARADO", () => e.AsignarPartida(c, pedido, 1, 2, Actor));

        if (!tieneFactura)
        {
            Codigo("CE_FACTURA_DESPACHO_REQUERIDA", () => e.DespacharExportacion(c, [pedido], false, Hoy, Hoy));
            e.AprobarDespacho(c, false, aprobador, ahora, "administracion", "Excepción documentada", null);
        }
        e.DespacharExportacion(c, [pedido], tieneFactura, Hoy, Hoy);
        Assert.Equal(Hoy, e.Contenedor(c).FechaEmbarqueReal);
        Assert.Equal(8, Assert.Single(e.Partidas).Cantidad);
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
        var e = Nuevo(); var c = Contenedor(e); Pedimento(e); var p = Pedido(e); e.AsignarPartida(c, p, 1, 100, Actor); Embarcar(e, c, Hoy, Hoy);
        Assert.Null(e.BloqueoFacturacion(p).Codigo);
        var modificado = p with { VersionOrigen = 2 };
        Assert.Equal("CE_PARTIDA_REQUIERE_REVISION", e.BloqueoFacturacion(modificado).Codigo);
        e.ActualizarRevisiones([modificado]); Assert.Equal(EstadoPartida.RequiereRevision, e.Partidas.Single().Estado);
        var revisor = Guid.NewGuid(); e.ConfirmarPartida(e.Partidas.Single().Id, modificado, revisor);
        Assert.Equal("CE_SALIDA_SIN_AUTORIZAR", e.BloqueoFacturacion(modificado).Codigo);
        PrepararSalida(e);
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
        var e = Nuevo(); var c = Contenedor(e); Codigo("CE_EMBARQUE_SIN_PEDIMENTO", () => Embarcar(e, c, Hoy, Hoy));
        Pedimento(e, clave); var incompleto = Contenedor(e, false);
        Codigo("CE_EMBARQUE_DATOS_REQUERIDOS", () => Embarcar(e, incompleto, Hoy, Hoy));
        Codigo("CE_FECHA_EMBARQUE_INVALIDA", () => Embarcar(e, c, Hoy.AddDays(1), Hoy));
        Embarcar(e, c, Hoy, Hoy); Assert.Equal(Hoy, e.Contenedor(c).FechaEmbarqueReal);
    }
    [Fact]
    public void K7_pedimento_versionado_usa_numero_vigente_y_marca_revision()
    {
        var e = Nuevo(); var tipo = new RequisitoDocumento(Guid.NewGuid(), "EXP_PEDIMENTO", "Pedimento", true, false);
        var doc = Guid.NewGuid(); var adjunto = Guid.NewGuid(); e.RegistrarDocumento(doc, tipo, adjunto, null, Actor, "DEMO-ORIGINAL");
        e.RegistrarPedimentoExportacion("", "A1", Hoy, Hoy, "16", doc); Assert.Equal("DEMO-ORIGINAL", e.NumeroPedimentoExportacion);
        Assert.True(e.PedimentoExportacionVigente([adjunto])); Assert.False(e.PedimentoExportacionVigente([]));
        var nuevo = Guid.NewGuid(); e.RegistrarDocumento(nuevo, tipo, Guid.NewGuid(), "DEMO rectificación", Actor, "DEMO-RECTIFICADO");
        Assert.Equal("DEMO-RECTIFICADO", e.NumeroPedimentoExportacion); Assert.Equal(nuevo, e.DocumentoPedimentoExportacionId); Assert.True(e.RevisionPendiente); Assert.Equal("16", e.AduanaPedimentoExportacion);
        Codigo("CE_PEDIMENTO_NO_VIGENTE", () => e.RegistrarPedimentoExportacion("N", "A1", Hoy, Hoy, "16", doc));
        Codigo("CE_PEDIMENTO_INVALIDO", () => e.RegistrarPedimentoExportacion("N", "ZZ", Hoy, Hoy, "16"));
    }
    [Fact]
    public void K9_cancelar_no_cancela_pedido_y_no_se_quita_con_factura()
    {
        var e = Nuevo(); var c1 = Contenedor(e); var c2 = Contenedor(e); var p = Pedido(e); Pedimento(e); e.AsignarPartida(c1, p, 1, 100, Actor);
        Assert.Equal("CE_CONTENEDOR_CON_FACTURA", Assert.Throws<ConflictException>(() => e.CancelarContenedor(c1, "DEMO cancelación", true)).Code);
        Assert.Equal(EstadoContenedor.Activo, e.Contenedor(c1).Estado);
        e.CancelarContenedor(c1, "DEMO booking cancelado", false); Assert.Single(e.Partidas); Assert.Equal("Importado", p.Estado);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", e.BloqueoFacturacion(p).Codigo);
        e.AsignarPartida(c2, p, 1, 100, Actor); Embarcar(e, c2, Hoy, Hoy); Assert.Null(e.BloqueoFacturacion(p).Codigo);
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
