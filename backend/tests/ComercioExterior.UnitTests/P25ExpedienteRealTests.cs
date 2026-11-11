using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P25ExpedienteRealTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static Expediente Crear() => new(Guid.NewGuid(), Guid.NewGuid(), "CE-EXP-DEMO", TipoExpediente.Exportacion, Guid.NewGuid(), "Muestra P25", null, Guid.NewGuid(), "USD", Actor, ["Cliente de muestra"]);

    [Fact]
    public void Nueve_facturas_conservan_nueve_vigentes_y_el_mismo_pdf_cubre_otro_requisito()
    {
        var e = Crear();
        var factura = new RequisitoDocumento(Guid.NewGuid(), "EXP_FACTURA", "Factura", true, false);
        var packing = factura with { Id = Guid.NewGuid(), Codigo = "EXP_PACKING" };
        var primero = Guid.NewGuid();
        e.RegistrarDocumento(Guid.NewGuid(), factura, primero, null, Actor);
        for (var n = 1; n < 9; n++) e.RegistrarDocumento(Guid.NewGuid(), factura, Guid.NewGuid(), null, Actor);
        e.Documentos.Count(d => d.Estado == EstadoDocumento.Vigente).Should().Be(9);
        e.RegistrarDocumento(Guid.NewGuid(), packing, primero, null, Actor);
        e.Documentos.Count(d => d.AdjuntoId == primero).Should().Be(2);
        e.Faltantes([factura, packing], [primero]).Should().BeEmpty();
    }

    [Fact]
    public void Reemplazar_un_documento_conserva_los_otros_y_no_aplica_no_convive_con_archivos()
    {
        var e = Crear(); var r = new RequisitoDocumento(Guid.NewGuid(), "EXP_FACTURA", "Factura", true, true);
        var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid(); var nuevo = Guid.NewGuid();
        e.RegistrarDocumento(d1, r, Guid.NewGuid(), null, Actor);
        e.RegistrarDocumento(d2, r, Guid.NewGuid(), null, Actor);
        e.RegistrarDocumento(Guid.NewGuid(), r, nuevo, "Corrección de muestra", Actor, reemplazaA: d1);
        e.Documentos.Single(d => d.Id == d2).Estado.Should().Be(EstadoDocumento.Vigente);
        e.MarcarNoAplica(Guid.NewGuid(), r, "No aplica en esta operación", Actor);
        e.Documentos.Count(d => d.Estado == EstadoDocumento.Vigente).Should().Be(1);
        e.RegistrarDocumento(Guid.NewGuid(), r, Guid.NewGuid(), "Ya se recibió el archivo", Actor);
        e.Documentos.Where(d => d.Estado == EstadoDocumento.Vigente).Should().ContainSingle().Which.NoAplica.Should().BeFalse();
    }

    [Fact]
    public void Revision_preserva_requisitos_historicos_y_una_modificacion_exige_revisar_el_catalogo_nuevo()
    {
        var e = Crear(); var r = new RequisitoDocumento(Guid.NewGuid(), "EXP_FACTURA", "Factura", true, false);
        var a = Guid.NewGuid(); e.RegistrarDocumento(Guid.NewGuid(), r, a, null, Actor);
        e.RevisarDocumentos([r], [a], Actor);
        var nuevo = r with { Id = Guid.NewGuid(), Codigo = "EXP_COVE" };
        e.Faltantes([r, nuevo], [a]).Should().BeEmpty();
        e.RegistrarDocumento(Guid.NewGuid(), r, Guid.NewGuid(), null, Actor);
        e.Faltantes([r, nuevo], [a]).Should().ContainSingle().Which.Id.Should().Be(nuevo.Id);
    }
}
