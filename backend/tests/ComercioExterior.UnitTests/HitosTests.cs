using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;

public sealed class HitosTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 10, 16);
    private static Expediente Crear(TipoExpediente tipo = TipoExpediente.Importacion)
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-IMP-2026-000001", tipo, Guid.NewGuid(), "DEMO hitos", tipo == TipoExpediente.Importacion ? Guid.NewGuid() : null, Guid.NewGuid(), "USD", Actor, tipo == TipoExpediente.Exportacion ? ["DEMO cliente"] : []);
        e.InicializarHitos(Hoy.AddDays(-5));
        return e;
    }
    private static void Codigo(Action action, string code) => action.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);
    private static void Real(Expediente e, string clave, DateOnly? fecha = null) => e.RegistrarHito(clave, fecha ?? Hoy, Actor, null, null, [], [], Hoy);

    [Fact]
    public void Quince_hitos_en_orden_y_solicitado_es_origen_idempotente()
    {
        var e = Crear();
        e.InicializarHitos(Hoy);
        e.Hitos.OrderBy(h => h.Orden).Select(h => h.Clave).Should().Equal(HitoExpediente.Catalogo.Select(h => h.Clave));
        e.Hitos.First().FechaReal.Should().Be(Hoy.AddDays(-5));
        e.Hitos.Should().HaveCount(15);
    }
    [Fact]
    public void Atraso_conserva_compromiso_al_completar_y_reprogramar_exige_motivo()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy.AddDays(-1), Actor, null);
        var h = e.Hitos.Single(h => h.Clave == "planeado");
        h.EstaAtrasado(Hoy).Should().BeTrue();
        Codigo(() => e.CambiarCompromiso("planeado", Hoy, Actor, "no"), "CE_MOTIVO_INVALIDO");
        h.FechaCompromiso.Should().Be(Hoy.AddDays(-1));
        Real(e, "planeado");
        h.FechaCompromiso.Should().Be(Hoy.AddDays(-1));
        h.EstaAtrasado(Hoy).Should().BeFalse();
    }
    [Fact]
    public void Hoy_no_esta_vencido_y_usa_fecha_de_Mexico()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy, Actor, null);
        var local = FechaContable.HoyLocal(new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero));
        local.Should().Be(Hoy);
        e.Hitos.Single(h => h.Clave == "planeado").EstaAtrasado(local).Should().BeFalse();
    }
    [Fact]
    public void Secuencia_admite_omision_justificada_y_no_permite_saltar()
    {
        var e = Crear();
        Codigo(() => Real(e, "booking-confirmado"), "CE_HITO_ANTERIOR_PENDIENTE");
        Codigo(() => e.OmitirHito("planeado", "no", Actor), "CE_MOTIVO_INVALIDO");
        e.OmitirHito("planeado", "Ruta DEMO sin planeación", Actor);
        Real(e, "booking-confirmado");
        e.Hitos.Single(h => h.Clave == "planeado").Omitido.Should().BeTrue();
        Codigo(() => Real(e, "planeado"), "CE_HITO_YA_RESUELTO");
    }
    [Theory]
    [InlineData("solicitado")]
    [InlineData("documentacion-completa")]
    [InlineData("despacho-aduanal")]
    [InlineData("entregado")]
    [InlineData("cerrado")]
    public void Hitos_obligatorios_no_se_omiten_aunque_los_anteriores_esten_resueltos(string clave)
    {
        var e = Crear();
        e.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Revisa, null, null);
        var orden = e.Hitos.Single(h => h.Clave == clave).Orden;
        foreach (var h in e.Hitos.Where(h => h.Orden > 1 && h.Orden < orden).OrderBy(h => h.Orden)) Real(e, h.Clave);
        var revision = e.RevisionContenido;
        Codigo(() => e.OmitirHito(clave, "Ruta DEMO no aplica", Actor), "CE_HITO_NO_OMITIBLE");
        e.Hitos.Single(h => h.Clave == clave).Omitido.Should().BeFalse();
        e.RevisionContenido.Should().Be(revision);
    }

    [Fact]
    public void Aviso_no_modifica_revision_de_contenido()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy.AddDays(-1), Actor, null);
        var revision = e.RevisionContenido;
        e.GenerarAvisos(Hoy).Should().Be(1);
        e.RevisionContenido.Should().Be(revision);
    }

    [Fact]
    public void Documentacion_completa_exige_lista_de_requisitos()
    {
        var e = Crear();
        foreach (var h in e.Hitos.Where(h => h.Orden is > 1 and < 6).OrderBy(h => h.Orden)) Real(e, h.Clave);
        var r = new RequisitoDocumento(Guid.NewGuid(), "IMP_PEDIMENTO", "Pedimento", true, false);
        var ex = Assert.Throws<DocumentosFaltantesException>(() => e.RegistrarHito("documentacion-completa", Hoy, Actor, null, null, [r], [], Hoy));
        ex.Code.Should().Be("CE_DOCUMENTOS_FALTANTES");
        ex.Faltantes.Should().Equal(r);
        var adjunto = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), r, adjunto, null, Actor);
        e.RegistrarHito("documentacion-completa", Hoy, Actor, adjunto, null, [r], [adjunto], Hoy);
        e.Hitos.Single(h => h.Clave == "documentacion-completa").EvidenciaAdjuntoId.Should().Be(adjunto);
    }
    [Fact]
    public void Despacho_exige_agente_aduanal()
    {
        var e = Crear();
        foreach (var h in e.Hitos.Where(h => h.Orden is > 1 and < 8).OrderBy(h => h.Orden)) Real(e, h.Clave);
        e.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.Otro, null, PapelParticipante.Revisa, null, null, "Agente aduanal");
        Codigo(() => Real(e, "despacho-aduanal"), "CE_AGENTE_ADUANAL_REQUERIDO");
        e.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Revisa, null, null);
        Real(e, "despacho-aduanal");
    }
    [Fact]
    public void Avisos_unicos_por_hito_y_compromiso_y_no_para_omitidos()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy.AddDays(-2), Actor, null);
        e.GenerarAvisos(Hoy).Should().Be(1);
        e.GenerarAvisos(Hoy).Should().Be(0);
        e.CambiarCompromiso("planeado", Hoy.AddDays(-1), Actor, "Nueva fecha DEMO");
        e.GenerarAvisos(Hoy).Should().Be(1);
        e.Avisos.Should().HaveCount(2);
        e.OmitirHito("planeado", "Ruta DEMO no aplica", Actor);
        e.GenerarAvisos(Hoy.AddDays(1)).Should().Be(0);
    }
    [Fact]
    public void Fisico_exportacion_exige_entregado_real_y_pod_vigente()
    {
        var e = Crear(TipoExpediente.Exportacion);
        var pod = new RequisitoDocumento(Guid.NewGuid(), "EXP_POD", "POD", false, false);
        e.EstadoFisico([pod], []).Should().Be("Pendiente");
        e.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Revisa, null, null);
        foreach (var h in e.Hitos.Where(h => h.Orden is > 1 and <= 13).OrderBy(h => h.Orden)) Real(e, h.Clave);
        e.EstadoFisico([pod], []).Should().Be("POD pendiente");
        var adjunto = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), pod, adjunto, null, Actor);
        e.EstadoFisico([pod], [adjunto]).Should().Be("Completo");
        e.EstadoFisico([pod], []).Should().Be("POD pendiente");
    }
    [Fact]
    public void Financiero_bloquea_cierre_y_hito_cerrado_incluso_omision()
    {
        var e = Crear();
        Codigo(e.Cerrar, "CE_CIERRE_FINANCIERO_PENDIENTE");
        Codigo(() => Real(e, "cerrado"), "CE_CIERRE_FINANCIERO_PENDIENTE");
        Codigo(() => e.OmitirHito("cerrado", "No aplica DEMO", Actor), "CE_HITO_NO_OMITIBLE");
        e.Estado.Should().Be(EstadoExpediente.Abierto);
    }
    [Fact]
    public void Cancelacion_bloquea_hitos_y_avisos_y_conserva_historial()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy.AddDays(-1), Actor, null);
        e.GenerarAvisos(Hoy);
        e.Cancelar("Cancelación DEMO");
        Codigo(() => Real(e, "planeado"), "CE_EXPEDIENTE_NO_ABIERTO");
        e.GenerarAvisos(Hoy).Should().Be(0);
        e.Hitos.Should().HaveCount(15);
        e.Avisos.Should().ContainSingle();
    }
    [Fact]
    public void Fechas_futuras_y_anteriores_al_previo_se_rechazan_sin_mutar()
    {
        var e = Crear();
        Codigo(() => Real(e, "planeado", Hoy.AddDays(1)), "CE_FECHA_REAL_INVALIDA");
        Codigo(() => Real(e, "planeado", Hoy.AddDays(-6)), "CE_FECHA_REAL_INVALIDA");
        e.Hitos.Single(h => h.Clave == "planeado").FechaReal.Should().BeNull();
    }
    [Fact]
    public void Evidencia_ajena_y_responsable_vacio_se_rechazan()
    {
        var e = Crear();
        Codigo(() => e.RegistrarHito("planeado", Hoy, Actor, Guid.NewGuid(), null, [], [], Hoy), "CE_ADJUNTO_INVALIDO");
        Codigo(() => e.CambiarCompromiso("planeado", Hoy, Guid.Empty, null), "CE_RESPONSABLE_REQUERIDO");
        e.Hitos.Single(h => h.Clave == "planeado").FechaReal.Should().BeNull();
    }
    [Fact]
    public void Importacion_entregada_completa_dimension_fisica_sin_pod()
    {
        var e = Crear();
        e.EstadoFisico([], []).Should().Be("Pendiente");
        e.AgregarParticipante(Guid.NewGuid(), "DEMO agente", TipoParticipante.AgenteAduanal, null, PapelParticipante.Revisa, null, null);
        foreach (var h in e.Hitos.Where(h => h.Orden is > 1 and <= 13).OrderBy(h => h.Orden)) Real(e, h.Clave);
        e.EstadoFisico([], []).Should().Be("Completo");
    }
    [Fact]
    public void Aviso_no_se_repite_al_volver_a_una_fecha_compromiso_ya_avisada()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", Hoy.AddDays(-2), Actor, null);
        e.GenerarAvisos(Hoy);
        e.CambiarCompromiso("planeado", Hoy.AddDays(2), Actor, "Nueva ruta DEMO");
        e.GenerarAvisos(Hoy).Should().Be(0);
        e.CambiarCompromiso("planeado", Hoy.AddDays(-2), Actor, "Regresa a fecha DEMO");
        e.GenerarAvisos(Hoy).Should().Be(0);
        e.Avisos.Should().ContainSingle();
    }

}
