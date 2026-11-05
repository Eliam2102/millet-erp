using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P18FacturasTests
{
    [Fact]
    public void Guarda_uuid_version_y_sustituta_sin_perder_original_y_tolero_reenvios()
    {
        var actor = Guid.NewGuid(); var pedido = Guid.NewGuid(); var ahora = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "EXP-P18", TipoExpediente.Exportacion, Guid.NewGuid(), "Facturas y versiones", null, Guid.NewGuid(), "USD", actor, ["Cliente prueba"]);
        var original = Guid.NewGuid(); var sustituta = Guid.NewGuid(); var uuid = Guid.NewGuid().ToString(); var uuidNueva = Guid.NewGuid().ToString();
        e.RegistrarFactura(original, pedido, 3, uuid, "F-18", ahora, false, null, ahora);
        e.RegistrarFactura(sustituta, pedido, 4, uuidNueva, "F-19", ahora, false, uuid, ahora);
        e.RegistrarFactura(original, pedido, 3, uuid, "F-18", ahora, true, null, ahora.AddMinutes(1));
        var revision = e.RevisionContenido;
        e.RegistrarFactura(original, pedido, 3, uuid, "F-18", ahora, false, null, ahora);
        e.RevisionContenido.Should().Be(revision);
        e.Facturas.Should().HaveCount(2);
        e.Facturas.Single(f => f.Id == original).Cancelada.Should().BeTrue();
        e.Facturas.Single(f => f.Id == original).VersionEmbarque.Should().Be(3);
        e.Facturas.Single(f => f.Id == sustituta).UuidOriginal.Should().Be(uuid);
    }
}
