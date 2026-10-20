using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// V-10 (86e3pc50v), M-047: el guard de recepción de Almacén rechaza una OC que no
/// esté Autorizada (Cancelada incluida) antes de tocar líneas, tolerancias o saldos.
/// La misma regla de Compras (<c>OC_MOVIMIENTO_ESTADO_INVALIDO</c>) se prueba en
/// <c>OcRecepcionEnAlmacenHandlerSubEstadosTests</c> de integración.
/// </summary>
public sealed class RecepcionOcGuardEstadoTests
{
    [Fact]
    public async Task Oc_cancelada_se_rechaza_con_RECEPCION_OC_NO_AUTORIZADA()
    {
        var oc = new OcLectura(
            Id: Guid.NewGuid(),
            Folio: "OC-TEST-CANCELADA",
            ProveedorId: Guid.NewGuid(),
            EmpresaId: Guid.NewGuid(),
            Estado: "Cancelada",
            Lineas: [new OcLineaLectura(Guid.NewGuid(), Guid.NewGuid(), "PZA", 100m, 0m, 10m)]);

        var act = () => RecepcionOcGuard.ValidarAsync(
            oc, [], recibidoPropio: new Dictionary<Guid, decimal>(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(act);
        Assert.Equal("RECEPCION_OC_NO_AUTORIZADA", ex.Code);
    }
}
