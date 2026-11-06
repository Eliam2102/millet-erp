using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.UnitTests;
public sealed class BajaDafTests
{
    [Theory] [InlineData("Baja")] [InlineData("Venta")]
    public void Retiro_exige_autor_pendiente_y_motivo_y_cierra_solicitud(string tipo)
    {
        var actor = Guid.NewGuid(); var ahora = DateTimeOffset.UtcNow;
        var op = new OperacionLibro { Tipo = tipo, RegistradoPor = actor };
        Assert.Throws<BusinessRuleException>(() => op.Retirar(Guid.NewGuid(), "Error de captura", ahora));
        Assert.Throws<BusinessRuleException>(() => op.Retirar(actor, " ", ahora));
        op.Retirar(actor, " Error de captura ", ahora);
        Assert.True(op.Anulada); Assert.Equal("Error de captura", op.MotivoRetiro);
        Assert.Equal(actor, op.RetiradoPor); Assert.Equal(ahora, op.RetiradoEn);
        Assert.Throws<BusinessRuleException>(() => op.Retirar(actor, "Otra vez", ahora));
        var contabilizada = new OperacionLibro { Tipo = tipo, RegistradoPor = actor, PolizaId = Guid.NewGuid() };
        Assert.Throws<BusinessRuleException>(() => contabilizada.Retirar(actor, "Error de captura", ahora));
    }
    [Fact]
    public void Baja_historica_con_motivo_libre_se_puede_autorizar()
    {
        var op = new OperacionLibro { Tipo = "Baja", Motivo = "Motivo histórico libre",
            SolicitadoPor = "Sin dato (anterior al 26-oct-2026)", Evidencia = "Soporte anterior", RegistradoPor = Guid.NewGuid() };
        op.Validar(Guid.NewGuid(), false);
    }

    [Fact]
    public void Baja_exige_area_y_motivo_catalogado()
    {
        var op = new OperacionLibro { Tipo = "Baja", Motivo = "Venta", Evidencia = "Factura", RegistradoPor = Guid.NewGuid() };
        Assert.Equal("AF_SOLICITANTE_REQUERIDO", Assert.Throws<BusinessRuleException>(() => op.Validar(Guid.NewGuid(), false)).Code);
        op.SolicitadoPor = "Transportes";
        op.ValidarSolicitudBaja();
        op.Validar(Guid.NewGuid(), false);
        op.Motivo = "Otro";
        Assert.Equal("AF_MOTIVO_REQUERIDO", Assert.Throws<BusinessRuleException>(() => op.ValidarSolicitudBaja()).Code);
    }
}
