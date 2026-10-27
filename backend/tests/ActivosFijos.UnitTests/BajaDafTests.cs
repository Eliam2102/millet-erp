using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.UnitTests;
public sealed class BajaDafTests
{
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
