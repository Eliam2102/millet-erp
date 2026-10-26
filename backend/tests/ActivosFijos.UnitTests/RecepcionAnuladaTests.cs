using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
/// <summary>Baja de activos por anulación de la recepción: solo por capitalizar y sin factura; repetir el evento no cambia nada.</summary>
public sealed class RecepcionAnuladaTests
{
    private static Activo Nuevo() => new(Guid.NewGuid(), Guid.NewGuid(), "AF-MAQ-000001",
        new DatosActivo("Montacargas DEMO", Guid.NewGuid(), null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 500000, "MXN", 1,
            new CustodiaActivo(Guid.NewGuid(), Guid.NewGuid(), "Conkal", Guid.NewGuid(), null), TipoImportacionActivo.No, null, false, "Soporte DEMO"),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
    private static readonly DateOnly Hoy = new(2026, 10, 22);

    [Fact]
    public void Por_capitalizar_pasa_a_dado_de_baja_con_motivo_y_repetir_no_cambia_nada()
    {
        var a = Nuevo();
        a.DarDeBajaPorRecepcionAnulada(Hoy, "Error de captura");
        a.DarDeBajaPorRecepcionAnulada(Hoy.AddDays(1), "Otro motivo");
        Assert.Equal(EstadoActivo.DadoDeBaja, a.Estado); Assert.Equal("Error de captura", a.MotivoBaja); Assert.Equal(Hoy, a.FechaBaja);
    }

    [Fact]
    public void Capitalizado_o_con_factura_conciliada_no_se_da_de_baja_por_anulacion()
    {
        var capitalizado = Nuevo(); capitalizado.ConciliarFactura(Guid.NewGuid(), 500000, "MXN", 1); capitalizado.Capitalizar(Guid.NewGuid());
        Assert.Throws<BusinessRuleException>(() => capitalizado.DarDeBajaPorRecepcionAnulada(Hoy, "Motivo"));
        var facturado = Nuevo(); facturado.ConciliarFactura(Guid.NewGuid(), 500000, "MXN", 1);
        Assert.Throws<BusinessRuleException>(() => facturado.DarDeBajaPorRecepcionAnulada(Hoy, "Motivo"));
        Assert.Equal(EstadoActivo.Activo, capitalizado.Estado); Assert.Equal(EstadoActivo.PorCapitalizar, facturado.Estado);
    }
}
