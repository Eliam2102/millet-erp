using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Notificaciones;

namespace Millet.CuentasPorPagar.UnitTests.Infrastructure;

public sealed class AvisosRevisionDestinatariosTests
{
    private static SlaRevisionNotificacion Aviso(bool compras) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 5,
        NivelEscalamientoSla.AvisoGerente, "A-1", "Motivo", compras);

    private static readonly AvisosRevisionOptions Opciones = new()
    {
        ContadorGeneral = ["laura@example.com"],
        Compras = ["compras@example.com"],
    };

    [Fact]
    public void Sin_Compras_solo_va_al_Contador_General() =>
        Opciones.Destinatarios(Aviso(false)).Should().Equal("laura@example.com");

    [Fact]
    public void Con_Compras_van_ambos() =>
        Opciones.Destinatarios(Aviso(true)).Should().Equal("laura@example.com", "compras@example.com");

    [Fact]
    public void Repetidos_se_quitan_sin_distinguir_mayusculas() =>
        new AvisosRevisionOptions { ContadorGeneral = ["Laura@Example.com"], Compras = ["laura@example.com", "x@example.com"] }
            .Destinatarios(Aviso(true)).Should().Equal("Laura@Example.com", "x@example.com");

    [Fact]
    public void Sin_configurar_no_hay_destinatarios() =>
        new AvisosRevisionOptions().Destinatarios(Aviso(true)).Should().BeEmpty();

    [Fact]
    public void Correos_mal_escritos_se_descartan() =>
        new AvisosRevisionOptions { ContadorGeneral = ["no-es-correo", " laura@example.com ", "Laura <x@example.com>"] }
            .Destinatarios(Aviso(false)).Should().Equal("laura@example.com");
}
