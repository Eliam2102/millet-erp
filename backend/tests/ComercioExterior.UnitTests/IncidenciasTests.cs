using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class IncidenciasTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 10, 20);
    private static readonly DateTimeOffset Ahora = new(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
    private static Expediente Crear(TipoExpediente tipo = TipoExpediente.Importacion)
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-DEMO", tipo, Guid.NewGuid(), "DEMO incidencias", tipo == TipoExpediente.Importacion ? Guid.NewGuid() : null, Guid.NewGuid(), "USD", Actor, tipo == TipoExpediente.Exportacion ? ["DEMO cliente"] : []);
        e.InicializarHitos(Hoy); return e;
    }
    private static Guid Abrir(Expediente e, TipoIncidencia tipo = TipoIncidencia.MercanciaDanada, Guid[]? evidencias = null)
    {
        var id = Guid.NewGuid(); e.AbrirIncidencia(id, new(tipo, Hoy.AddDays(-2), Actor, "DEMO daño en transporte", EvidenciasIds: evidencias, HitoClave: "entregado"), evidencias ?? [], Actor, Ahora, Hoy); return id;
    }
    [Theory]
    [InlineData(TipoExpediente.Importacion)]
    [InlineData(TipoExpediente.Exportacion)]
    public void Rectificacion_conserva_dos_pedimentos_y_vigente_para_iva(TipoExpediente tipo)
    {
        var e = Crear(tipo); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var requisito = new RequisitoDocumento(Guid.NewGuid(), tipo == TipoExpediente.Importacion ? "IMP_PEDIMENTO" : "EXP_PEDIMENTO", "Pedimento", true, false);
        e.RegistrarDocumento(Guid.NewGuid(), requisito, a, null, Actor, "DEMO-ORIGINAL");
        if (tipo == TipoExpediente.Exportacion) e.RegistrarPedimentoExportacion("DEMO-ORIGINAL", "A1", Hoy, Hoy);
        var id = Abrir(e, TipoIncidencia.RectificacionPedimento, [b]);
        e.CambiarIncidencia(id, EstadoIncidencia.EnAtencion, null, null, [], null, Actor, Ahora, [a, b]);
        e.CambiarIncidencia(id, EstadoIncidencia.Resuelta, "Se rectificó la clasificación", "IVA corregido", [], null, Actor, Ahora, [a, b], requisito, b, "DEMO-NUEVO");
        e.Documentos.Should().HaveCount(2); e.Documentos.Single(d => d.Estado == EstadoDocumento.Reemplazada).NumeroPedimento.Should().Be("DEMO-ORIGINAL");
        e.Documentos.Single(d => d.Estado == EstadoDocumento.Vigente).NumeroPedimento.Should().Be("DEMO-NUEVO");
        e.PedimentoVigente("DEMO-ORIGINAL").Should().Be("DEMO-NUEVO");
        e.Incidencias.Single().PedimentoAnterior.Should().Be("DEMO-ORIGINAL");
        e.Incidencias.Single().Historial.Select(h => h.Estado).Should().Equal(EstadoIncidencia.Abierta, EstadoIncidencia.EnAtencion, EstadoIncidencia.Resuelta);
    }
    [Fact]
    public void Version_documental_posterior_actualiza_pedimento_de_importacion_sin_perder_original()
    {
        var e = Crear(); var requisito = new RequisitoDocumento(Guid.NewGuid(), "IMP_PEDIMENTO", "Pedimento", true, false);
        e.RegistrarDocumento(Guid.NewGuid(), requisito, Guid.NewGuid(), null, Actor, "DEMO-ORIGINAL");
        e.PedimentoVigente("DATO-OC-ANTERIOR").Should().Be("DEMO-ORIGINAL");
        e.RegistrarDocumento(Guid.NewGuid(), requisito, Guid.NewGuid(), "Corrección documental DEMO", Actor, "DEMO-VIGENTE");
        e.PedimentoVigente("DATO-OC-ANTERIOR").Should().Be("DEMO-VIGENTE");
        e.Documentos.Single(d => d.Estado == EstadoDocumento.Reemplazada).NumeroPedimento.Should().Be("DEMO-ORIGINAL");
    }
    [Theory]
    [InlineData("cerrado")]
    [InlineData("entregado")]
    [InlineData("documentacion-completa")]
    public void Incidencia_abierta_o_en_atencion_impide_los_tres_cierres(string hito)
    {
        var e = Crear(); var id = Abrir(e);
        for (var paso = 0; paso < 2; paso++)
        {
            var ex = Assert.Throws<BusinessRuleException>(() => e.RegistrarHito(hito, Hoy, Actor, null, null, [], [], Hoy));
            ex.Code.Should().Be("CE_INCIDENCIAS_PENDIENTES"); ex.Message.Should().Contain("DEMO daño en transporte");
            Assert.Throws<BusinessRuleException>(e.Cerrar).Code.Should().Be("CE_INCIDENCIAS_PENDIENTES");
            if (paso == 0) e.CambiarIncidencia(id, EstadoIncidencia.EnAtencion, "Revisar descarga", null, [], null, Actor, Ahora, []);
        }
    }
    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("Se repuso la pieza", false)]
    public void Resolver_exige_accion_y_evidencia_sin_mutar(string? accion, bool evidencia)
    {
        var e = Crear(); var id = Abrir(e); var a = Guid.NewGuid();
        e.CambiarIncidencia(id, EstadoIncidencia.EnAtencion, null, null, [], null, Actor, Ahora, [a]);
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(id, EstadoIncidencia.Resuelta, accion, null, evidencia ? [a] : [], null, Actor, Ahora, [a])).Code.Should().Be("CE_INCIDENCIA_RESOLUCION_INCOMPLETA");
        e.Incidencias.Single().Estado.Should().Be(EstadoIncidencia.EnAtencion); e.Incidencias.Single().Historial.Should().HaveCount(2);
    }
    [Fact]
    public void Cancelacion_con_motivo_libera_bloqueo_y_conserva_historial_aviso()
    {
        var e = Crear(); var id = Abrir(e);
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(id, EstadoIncidencia.Cancelada, null, null, [], "no", Actor, Ahora, []));
        e.CambiarIncidencia(id, EstadoIncidencia.Cancelada, null, null, [], "Registro duplicado por error", Actor, Ahora, []);
        e.ExigirSinIncidencias();
        e.Cerrar(); // sin incidencias pendientes ni gastos o anticipos abiertos, el cierre financiero procede (CE-06)
        e.FinancieroCerrado.Should().BeTrue();
        e.Incidencias.Single().Historial.Should().HaveCount(2); e.AvisosIncidencias.Single().DestinatarioUsuarioId.Should().Be(Actor);
        IncidenciaMap.Dto(e.Incidencias.Single(), new Dictionary<Guid, string>(), Hoy).Historial.Select(h => h.Estado).Should().Equal(EstadoIncidencia.Abierta, EstadoIncidencia.Cancelada);
        e.AvisosIncidencias.Single().IncidenciaId.Should().Be(id);
    }
    [Fact]
    public void Referencias_ajenas_otro_sin_descripcion_y_evidencia_inactiva_se_rechazan()
    {
        var e = Crear();
        foreach (var d in new[] { new DatosIncidencia(TipoIncidencia.Otra, Hoy, Actor, "DEMO motivo"), new DatosIncidencia(TipoIncidencia.MercanciaDanada, Hoy, Actor, "DEMO motivo", ParticipanteId: Guid.NewGuid()), new DatosIncidencia(TipoIncidencia.MercanciaDanada, Hoy, Actor, "DEMO motivo", DocumentosIds: [Guid.NewGuid()]), new DatosIncidencia(TipoIncidencia.MercanciaDanada, Hoy, Actor, "DEMO motivo", EvidenciasIds: [Guid.NewGuid()]), new DatosIncidencia(TipoIncidencia.MercanciaDanada, Hoy.AddDays(1), Actor, "DEMO motivo") })
            Assert.Throws<BusinessRuleException>(() => e.AbrirIncidencia(Guid.NewGuid(), d, [], Actor, Ahora, Hoy));
        e.Incidencias.Should().BeEmpty(); e.AvisosIncidencias.Should().BeEmpty();
    }
    [Fact]
    public void Resolucion_final_y_antiguedad_se_conservan()
    {
        var e = Crear(); var a = Guid.NewGuid(); var id = Abrir(e, evidencias: [a]);
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(id, EstadoIncidencia.Resuelta, "Acción DEMO", null, [], null, Actor, Ahora, [a])).Code.Should().Be("CE_INCIDENCIA_TRANSICION_INVALIDA");
        e.CambiarIncidencia(id, EstadoIncidencia.EnAtencion, null, null, [], null, Actor, Ahora, [a]);
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(id, EstadoIncidencia.Resuelta, "Acción DEMO", null, [], null, Actor, Ahora, []));
        var reemplazo = Guid.NewGuid();
        e.CambiarIncidencia(id, EstadoIncidencia.Resuelta, "Acción DEMO", null, [reemplazo], null, Actor, Ahora, [reemplazo]);
        e.Incidencias.Single().EvidenciasIds.Should().Contain(a).And.Contain(reemplazo);
        IncidenciaMap.Antiguedad(e.Incidencias.Single(), Hoy.AddDays(10)).Should().Be(2);
        Assert.Throws<BusinessRuleException>(() => e.CambiarIncidencia(id, EstadoIncidencia.EnAtencion, null, null, [], null, Actor, Ahora, [a]));
    }
}
