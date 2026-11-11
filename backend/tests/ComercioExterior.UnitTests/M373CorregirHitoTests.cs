using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.ComercioExterior.UnitTests;

/// <summary>M-373: corregir la fecha real o la omisión de un hito y requisito condicional configurable.</summary>
public sealed class M373CorregirHitoTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Corrige = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 13);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
    private const string Motivo = "Se capturó la fecha del correo, no la del booking";

    private static Expediente Crear()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-IMP-2026-000373", TipoExpediente.Importacion, Guid.NewGuid(), "Importación vidrio flotado", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
        e.InicializarHitos(new DateOnly(2026, 11, 2));
        return e;
    }

    private static HitoExpediente Hito(Expediente e, string clave) => e.Hitos.Single(h => h.Clave == clave);
    private static void Codigo(Action action, string code) => action.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);
    private static void Real(Expediente e, string clave, DateOnly fecha) => e.RegistrarHito(clave, fecha, Actor, null, null, [], [], Hoy);
    private static CorreccionHitoExpediente Corregir(Expediente e, string clave, DateOnly? fecha, bool omitir = false, string? motivo = Motivo) =>
        e.CorregirHito(clave, fecha, omitir, motivo, Corrige, Ahora, Hoy, [], []);

    [Fact]
    public void CA1_fecha_real_12_nov_corregida_a_10_nov_queda_en_historial_con_usuario_y_motivo()
    {
        var e = Crear();
        Real(e, "planeado", new DateOnly(2026, 11, 12));
        var revision = e.RevisionContenido;

        var c = Corregir(e, "planeado", new DateOnly(2026, 11, 10));

        Hito(e, "planeado").FechaReal.Should().Be(new DateOnly(2026, 11, 10));
        Hito(e, "planeado").Motivo.Should().Be(Motivo);
        e.CorreccionesHitos.Should().ContainSingle().Which.Should().BeSameAs(c);
        c.HitoId.Should().Be(Hito(e, "planeado").Id);
        c.FechaRealAnterior.Should().Be(new DateOnly(2026, 11, 12));
        c.FechaRealNueva.Should().Be(new DateOnly(2026, 11, 10));
        c.OmitidoAnterior.Should().BeFalse();
        c.OmitidoNuevo.Should().BeFalse();
        c.UsuarioId.Should().Be(Corrige);
        c.Motivo.Should().Be(Motivo);
        c.Fecha.Should().Be(Ahora);
        e.RevisionContenido.Should().BeGreaterThan(revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CA2_sin_motivo_se_rechaza_y_el_hito_no_cambia(string? motivo)
    {
        var e = Crear();
        Real(e, "planeado", new DateOnly(2026, 11, 12));
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 10), motivo: motivo), "CE_MOTIVO_CORRECCION_REQUERIDO");
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 10), motivo: "no"), "CE_MOTIVO_INVALIDO");
        Hito(e, "planeado").FechaReal.Should().Be(new DateOnly(2026, 11, 12));
        e.CorreccionesHitos.Should().BeEmpty();
    }

    [Fact]
    public void CA4_correccion_que_elimina_el_atraso_cierra_el_aviso_y_la_que_lo_crea_lo_reabre()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", new DateOnly(2026, 11, 10), Actor, null);
        e.GenerarAvisos(new DateOnly(2026, 11, 11)).Should().Be(1);
        Real(e, "planeado", new DateOnly(2026, 11, 12));
        var aviso = e.Avisos.Single();
        aviso.Vigente.Should().BeTrue();

        Corregir(e, "planeado", new DateOnly(2026, 11, 10));
        aviso.Vigente.Should().BeFalse();
        aviso.CerradoEn.Should().Be(Hoy);
        aviso.MotivoCierre.Should().Be(Expediente.MotivoCierreAvisoPorCorreccion);

        Corregir(e, "planeado", new DateOnly(2026, 11, 12), motivo: "La fecha correcta sí era la del 12");
        e.Avisos.Should().ContainSingle().Which.Vigente.Should().BeTrue();
        aviso.FechaAviso.Should().Be(Hoy);
    }

    [Fact]
    public void Correccion_que_crea_un_atraso_abre_el_aviso_con_la_fecha_de_hoy()
    {
        var e = Crear();
        e.CambiarCompromiso("planeado", new DateOnly(2026, 11, 10), Actor, null);
        Real(e, "planeado", new DateOnly(2026, 11, 10));
        e.Avisos.Should().BeEmpty();

        Corregir(e, "planeado", new DateOnly(2026, 11, 12));

        var aviso = e.Avisos.Should().ContainSingle().Subject;
        aviso.Vigente.Should().BeTrue();
        aviso.FechaCompromiso.Should().Be(new DateOnly(2026, 11, 10));
        aviso.FechaAviso.Should().Be(Hoy);
    }

    [Fact]
    public void Omision_se_corrige_a_fecha_real_y_fecha_real_a_omision_con_historial()
    {
        var e = Crear();
        e.OmitirHito("planeado", "Ruta sin planeación previa", Actor);
        Corregir(e, "planeado", new DateOnly(2026, 11, 5));
        Hito(e, "planeado").Omitido.Should().BeFalse();
        Hito(e, "planeado").FechaReal.Should().Be(new DateOnly(2026, 11, 5));
        var primera = e.CorreccionesHitos.Single();
        primera.OmitidoAnterior.Should().BeTrue();
        primera.MotivoAnterior.Should().Be("Ruta sin planeación previa");

        Corregir(e, "planeado", null, omitir: true, motivo: "El booking llegó sin planeación");
        Hito(e, "planeado").Omitido.Should().BeTrue();
        Hito(e, "planeado").FechaReal.Should().BeNull();
        e.CorreccionesHitos.Should().HaveCount(2);
        e.CorreccionesHitos.Last().FechaRealAnterior.Should().Be(new DateOnly(2026, 11, 5));
        e.CorreccionesHitos.Last().OmitidoNuevo.Should().BeTrue();
    }

    [Fact]
    public void Respeta_orden_de_hitos_fecha_futura_pendientes_y_no_omitibles()
    {
        var e = Crear();
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 5)), "CE_HITO_PENDIENTE");
        Real(e, "planeado", new DateOnly(2026, 11, 8));
        Real(e, "booking-confirmado", new DateOnly(2026, 11, 10));
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 11)), "CE_FECHA_REAL_INVALIDA");
        Codigo(() => Corregir(e, "booking-confirmado", new DateOnly(2026, 11, 7)), "CE_FECHA_REAL_INVALIDA");
        Codigo(() => Corregir(e, "booking-confirmado", Hoy.AddDays(1)), "CE_FECHA_REAL_INVALIDA");
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 8)), "CE_CORRECCION_SIN_CAMBIO");
        Codigo(() => Corregir(e, "planeado", null), "CE_FECHA_REAL_REQUERIDA");
        Codigo(() => Corregir(e, "planeado", new DateOnly(2026, 11, 8), omitir: true), "CE_CORRECCION_INVALIDA");
        Codigo(() => Corregir(e, "solicitado", null, omitir: true), "CE_HITO_NO_OMITIBLE");
        Codigo(() => Corregir(e, "cerrado", Hoy), "CE_HITO_NO_CORREGIBLE");
        Corregir(e, "booking-confirmado", new DateOnly(2026, 11, 8));
        Hito(e, "booking-confirmado").FechaReal.Should().Be(new DateOnly(2026, 11, 8));
    }

    [Fact]
    public void De_omision_a_fecha_real_exige_los_documentos_del_hito()
    {
        var e = Crear();
        e.OmitirHito("planeado", "Ruta sin planeación previa", Actor);
        var requisito = new RequisitoDocumento(Guid.NewGuid(), "IMP_FACTURA", "Factura comercial", true, false, "planeado");
        Action accion = () => e.CorregirHito("planeado", new DateOnly(2026, 11, 5), false, Motivo, Corrige, Ahora, Hoy, [requisito], []);
        accion.Should().Throw<DocumentosFaltantesException>();
        Hito(e, "planeado").Omitido.Should().BeTrue();
    }

    [Fact]
    public void Expediente_cerrado_o_cancelado_no_se_corrige()
    {
        var e = Crear();
        Real(e, "planeado", new DateOnly(2026, 11, 8));
        e.Cancelar("Operación cancelada por el proveedor");
        Action accion = () => Corregir(e, "planeado", new DateOnly(2026, 11, 7));
        accion.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void CA3_tipo_de_documento_condicional_se_configura_sin_codigo()
    {
        var tipo = new AdjuntoTipoDocumento(Guid.NewGuid(), "expediente_ce", "EXP_ORIGEN", "Certificado de origen", 11, true, null, false);
        tipo.Condicional.Should().BeFalse();
        tipo.CambiarCondicional(true);
        tipo.Condicional.Should().BeTrue();

        var e = Crear();
        var requisito = new RequisitoDocumento(tipo.Id, tipo.Codigo, tipo.Nombre, tipo.Obligatorio, tipo.Condicional);
        e.Faltantes([requisito], []).Should().ContainSingle();
        e.MarcarNoAplica(Guid.NewGuid(), requisito, "La mercancía no se acoge a trato preferencial", Actor);
        e.Faltantes([requisito], []).Should().BeEmpty();
        Codigo(() => e.MarcarNoAplica(Guid.NewGuid(), requisito with { Condicional = false }, "No aplica a esta operación", Actor), "CE_REQUISITO_NO_CONDICIONAL");
    }
}
