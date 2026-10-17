using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compartido.UnitTests;

public sealed class CambioMaestroAwTests
{
    private static CambioMaestroAw Nuevo(EstadoCambioMaestroAw estado, string? antes = "a") =>
        new(Guid.NewGuid(), "Cliente", Guid.NewGuid(), "REF-1", "Nombre de origen", antes, "b", "AW",
            TipoCambioMaestroAw.Aplicado, estado, "Cambio recibido de A+W.", "hash", "0-propuesta", null, DateTime.UtcNow);

    [Fact]
    public void Constructor_recorta_valor_antes_a_400()
        => Assert.Equal(400, Nuevo(EstadoCambioMaestroAw.Informativo, new string('x', 500)).ValorAntes!.Length);

    [Fact]
    public void Resolver_sobre_informativo_lanza_ya_resuelto()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Nuevo(EstadoCambioMaestroAw.Informativo).Resolver("MantenerSeparados", Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Equal("CONFLICTO_AW_YA_RESUELTO", ex.Code);
    }

    [Fact]
    public void Resolver_sobre_pendiente_deja_resuelto()
    {
        var c = Nuevo(EstadoCambioMaestroAw.Pendiente); var u = Guid.NewGuid();
        c.Resolver("MantenerSeparados", u, DateTimeOffset.UtcNow);
        Assert.Equal(EstadoCambioMaestroAw.Resuelto, c.Estado); Assert.Equal("MantenerSeparados", c.Resolucion); Assert.Equal(u, c.ResueltoPor);
    }
}
