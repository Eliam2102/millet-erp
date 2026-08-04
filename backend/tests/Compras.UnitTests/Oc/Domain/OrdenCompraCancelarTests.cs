using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests del método <see cref="OrdenCompra.Cancelar"/> (F3-PR3).
/// </summary>
public class OrdenCompraCancelarTests
{
    private static OrdenCompra NewOcBorrador() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

    [Fact]
    public void Cancelar_DesdeBorrador_TransicionaA_Cancelada_Y_EmiteEvento()
    {
        var oc = NewOcBorrador();
        var motivoId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        var resultado = oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: ahora,
            motivoCancelacionId: motivoId,
            motivoCancelacionTexto: "Pedido duplicado");

        Assert.Equal(EstadoOrdenCompra.Cancelada, oc.Estado);
        Assert.Equal(motivoId, oc.MotivoCancelacionId);
        Assert.Equal("Pedido duplicado", oc.MotivoCancelacion);
        Assert.Equal(EstadoOrdenCompra.Borrador, resultado.EventoCancelada.EstadoPrevio);
        Assert.Equal(ahora, resultado.EventoCancelada.OcurridoEn);
        Assert.Empty(resultado.RequisicionesALiberar);
    }

    [Fact]
    public void Cancelar_DesdeCancelada_Lanza()
    {
        var oc = NewOcBorrador();
        oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        // Segunda cancelación falla.
        var ex = Assert.Throws<BusinessRuleException>(() => oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7()));
        Assert.Equal("OC_CANCELAR_ESTADO_TERMINAL", ex.Code);
    }

    [Fact]
    public void Cancelar_MotivoVacio_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.Empty));
        Assert.Equal("OC_CANCELAR_MOTIVO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Cancelar_TextoDemasiadoLargo_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.Cancelar(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7(),
            motivoCancelacionTexto: new string('x', 501)));
        Assert.Equal("OC_CANCELAR_TEXTO_DEMASIADO_LARGO", ex.Code);
    }
}
