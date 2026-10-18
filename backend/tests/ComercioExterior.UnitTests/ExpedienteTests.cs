using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class ExpedienteTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Incoterm = Guid.NewGuid();
    private static readonly Guid TipoDocumento = Guid.NewGuid();
    private static Expediente Crear() => new(Guid.NewGuid(), Empresa, "CE-EXP-2026-000001", TipoExpediente.Exportacion, Sucursal, "Solicitud DEMO", null, Incoterm, "USD", Actor, ["Cliente DEMO"]);
    private static readonly RequisitoDocumento[] Requisitos = [new(TipoDocumento, "EXP_PEDIMENTO", "Pedimento", true, false)];
    private static void Codigo(Action action, string code) => action.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(code);
    [Fact]
    public void Exportacion_con_solicitud_y_cliente_nace_abierta() => Crear().Estado.Should().Be(EstadoExpediente.Abierto);
    [Fact]
    public void Importacion_exige_oc() => Codigo(() => _ = new Expediente(Guid.NewGuid(), Empresa, "CE-IMP-2026-000001", TipoExpediente.Importacion, Sucursal, "DEMO", null, Incoterm, "USD", Actor, []), "CE_OC_SIN_IMPORTACION");
    [Fact]
    public void Exportacion_exige_clientes() => Codigo(() => _ = new Expediente(Guid.NewGuid(), Empresa, "CE-EXP-2026-000001", TipoExpediente.Exportacion, Sucursal, "DEMO", null, Incoterm, "USD", Actor, []), "CE_CLIENTE_REQUERIDO");
    [Fact]
    public void Incoterm_obligatorio() => Codigo(() => Crear().Editar("DEMO", Guid.Empty, "USD", null, null, null, Actor), "CE_INCOTERM_REQUERIDO");
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Tc_positivo(decimal valor) => Codigo(() => Crear().Editar("DEMO", Incoterm, "USD", valor, new(2026, 10, 15), "Manual", Actor), "CE_TC_INVALIDO");
    [Fact]
    public void Tc_con_fecha_y_fuente_libre()
    {
        var e = Crear();
        e.Editar("DEMO", Incoterm, "USD", 18.123456m, new(2026, 10, 15), "Por confirmar", Actor);
        e.FuenteTc.Should().Be("Por confirmar");
    }

    [Fact]
    public void Tc_sin_fecha_rechazado() => Codigo(() => Crear().Editar("DEMO", Incoterm, "USD", 18, null, "Manual", Actor), "CE_TC_DATOS_REQUERIDOS");
    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("123")]
    public void Moneda_iso(string moneda) => Codigo(() => Crear().Editar("DEMO", Incoterm, moneda, null, null, null, Actor), "CE_MONEDA_INVALIDA");
    [Fact]
    public void Participante_libre_con_contacto_y_papel()
    {
        var e = Crear();
        e.AgregarParticipante(Guid.NewGuid(), "Agente DEMO", TipoParticipante.AgenteAduanal, "contacto DEMO", PapelParticipante.Revisa, null, null);
        e.Participantes.Should().Contain(p => p.Tipo == TipoParticipante.AgenteAduanal);
    }

    [Fact]
    public void Participante_rechaza_tipo_fuera_del_catalogo() => Codigo(() => Crear().AgregarParticipante(Guid.NewGuid(), "DEMO participante", (TipoParticipante)99, null, PapelParticipante.Captura, null, null), "CE_PARTICIPANTE_INVALIDO");

    [Fact]
    public void Participante_rechaza_nombre_vacio() => Codigo(() => Crear().AgregarParticipante(Guid.NewGuid(), "", TipoParticipante.Otro, null, PapelParticipante.Captura, null, null), "CE_PARTICIPANTE_INVALIDO");
    [Fact]
    public void Faltantes_rechazan_revision_con_lista()
    {
        var e = Crear();
        var ex = Assert.Throws<DocumentosFaltantesException>(() => e.RevisarDocumentos(Requisitos, [], Actor));
        ex.Code.Should().Be("CE_DOCUMENTOS_FALTANTES");
        ex.Faltantes.Should().ContainSingle().Which.Nombre.Should().Be("Pedimento");
    }

    [Fact]
    public void Archivo_sin_version_no_cumple_requisito() => Crear().Faltantes(Requisitos, [Guid.NewGuid()]).Should().ContainSingle();
    [Fact]
    public void Rectificacion_conserva_anterior_y_numera()
    {
        var e = Crear();
        var a = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], a, null, Actor);
        e.RevisarDocumentos(Requisitos, [a], Actor);
        var b = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], b, "Rectificación DEMO", Actor);
        e.Documentos.Should().HaveCount(2);
        e.Documentos.First().Estado.Should().Be(EstadoDocumento.Reemplazada);
        e.Documentos.Last().NumeroVersion.Should().Be(2);
        e.Documentos.Last().ReemplazaA.Should().Be(e.Documentos.First().Id);
        e.RevisionPendiente.Should().BeTrue();
        e.RevisadoPorId.Should().BeNull();
        e.RevisarDocumentos(Requisitos, [b], Actor);
        e.RevisionPendiente.Should().BeFalse();
    }

    [Fact]
    public void Baja_del_adjunto_vigente_invalida_revision_y_limpia_revisor()
    {
        var e = Crear();
        var adjunto = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], adjunto, null, Actor);
        e.RevisarDocumentos(Requisitos, [adjunto], Actor);
        var revision = e.RevisionContenido;
        e.DarDeBajaAdjunto(adjunto);
        e.RevisionPendiente.Should().BeTrue();
        e.RevisadoPorId.Should().BeNull();
        e.RevisionContenido.Should().Be(revision + 1);
        e.Faltantes(Requisitos, []).Should().ContainSingle();
    }

    [Fact]
    public void Baja_de_version_reemplazada_no_invalida_revision()
    {
        var e = Crear();
        var anterior = Guid.NewGuid();
        var vigente = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], anterior, null, Actor);
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], vigente, "Rectificación DEMO", Actor);
        e.RevisarDocumentos(Requisitos, [vigente], Actor);
        var revision = e.RevisionContenido;
        e.DarDeBajaAdjunto(anterior);
        e.RevisionPendiente.Should().BeFalse();
        e.RevisadoPorId.Should().Be(Actor);
        e.RevisionContenido.Should().Be(revision);
    }

    [Fact]
    public void Rectificacion_exige_motivo()
    {
        var e = Crear();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], Guid.NewGuid(), null, Actor);
        Codigo(() => e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], Guid.NewGuid(), "x", Actor), "CE_MOTIVO_INVALIDO");
        e.Documentos.Single().Estado.Should().Be(EstadoDocumento.Vigente);
    }

    [Fact]
    public void No_aplica_solo_condicional_y_con_motivo()
    {
        var e = Crear();
        Codigo(() => e.MarcarNoAplica(Guid.NewGuid(), Requisitos[0], "No aplica DEMO", Actor), "CE_REQUISITO_NO_CONDICIONAL");
        var r = Requisitos[0] with
        {
            Condicional = true
        };
        Codigo(() => e.MarcarNoAplica(Guid.NewGuid(), r, "no", Actor), "CE_MOTIVO_INVALIDO");
        e.MarcarNoAplica(Guid.NewGuid(), r, "Ruta DEMO sin ISF", Actor);
        e.Faltantes([r], []).Should().BeEmpty();
    }

    [Fact]
    public void Documento_repetido_no_se_versiona_dos_veces()
    {
        var e = Crear();
        var a = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], a, null, Actor);
        Codigo(() => e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], a, "DEMO duplicado", Actor), "CE_ADJUNTO_YA_VERSIONADO");
    }

    [Fact]
    public void Cancelacion_conserva_documentos_y_bloquea_edicion()
    {
        var e = Crear();
        e.RegistrarDocumento(Guid.NewGuid(), Requisitos[0], Guid.NewGuid(), null, Actor);
        e.Cancelar("Cancelación DEMO");
        e.Documentos.Should().ContainSingle();
        e.Estado.Should().Be(EstadoExpediente.Cancelado);
        Codigo(() => e.Editar("DEMO", Incoterm, "USD", null, null, null, Actor), "CE_EXPEDIENTE_NO_ABIERTO");
    }

    [Fact]
    public void Cancelacion_exige_motivo() => Codigo(() => Crear().Cancelar("no"), "CE_MOTIVO_INVALIDO");
}
