using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;

public sealed class P18ImportacionTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Hoy = new(2026, 11, 5);
    private static Expediente Crear() => new(Guid.NewGuid(), Guid.NewGuid(), "IMP-P18", TipoExpediente.Importacion,
        Guid.NewGuid(), "Importación de prueba", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
    private static Guid Contenedor(Expediente e)
    {
        var id = Guid.NewGuid();
        e.GuardarContenedor(id, new("CONT-18", null, "BOOK-18", "BL-18", null, null, null, null, ModalidadTransporte.Maritimo));
        return id;
    }
    [Fact]
    public void Seis_de_diez_dejan_cuatro_y_once_se_rechaza_sin_mutar()
    {
        var e = Crear(); var c = Contenedor(e); var linea = Guid.NewGuid();
        e.AsignarPartidaImportacion(c, linea, 1, 6, 10, "PZA", Actor);
        e.RemanenteImportacion(linea, 10).Should().Be(4);
        var ex = Assert.Throws<BusinessRuleException>(() => e.AsignarPartidaImportacion(c, linea, 1, 5, 10, "PZA", Actor));
        ex.Code.Should().Be("CE_CANTIDAD_EXCEDE_OC");
        e.PartidasImportacion.Should().ContainSingle();
    }
    [Fact]
    public void Maritimo_sin_naviera_se_rechaza()
    {
        var e = Crear(); var c = Contenedor(e);
        e.AgregarParticipante(Guid.NewGuid(), "Agente prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AsignarPartidaImportacion(c, Guid.NewGuid(), 1, 1, 10, "PZA", Actor);
        var ex = Assert.Throws<BusinessRuleException>(() => e.RegistrarEmbarque(c, Hoy, Hoy));
        ex.Code.Should().Be("CE_TRANSPORTISTA_REQUERIDO");
        e.Contenedor(c).FechaEmbarqueReal.Should().BeNull();
        e.AgregarParticipante(Guid.NewGuid(), "Naviera prueba", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        e.RegistrarEmbarque(c, Hoy, Hoy);
        e.Contenedor(c).FechaEmbarqueReal.Should().Be(Hoy);
    }
    [Fact]
    public void Importacion_sin_partidas_no_puede_embarcar()
    {
        var e = Crear(); var c = Contenedor(e);
        e.AgregarParticipante(Guid.NewGuid(), "Agente prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera prueba", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        Assert.Throws<BusinessRuleException>(() => e.RegistrarEmbarque(c, Hoy, Hoy)).Code.Should().Be("CE_EMBARQUE_SIN_PARTIDAS");
    }
}
