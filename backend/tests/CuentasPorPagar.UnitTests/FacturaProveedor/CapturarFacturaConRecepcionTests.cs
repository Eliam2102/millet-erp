using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Almacen;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Tests.P3;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>86e3tfedr: la factura guarda la recepción elegida, la valida en servidor y la publica en el evento.</summary>
public sealed class CapturarFacturaConRecepcionTests
{
    private static async Task<Guid> ProyectarRecepcionAsync(P3FacturaConOcTests.Ambiente a)
    {
        var rec = a.Recepcion(5);
        await a.Recepciones.Handle(rec, default);
        return rec.Payload.RecepcionId;
    }

    [Fact]
    public async Task Guarda_la_recepcion_elegida_y_la_publica_en_el_evento()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var recepcionId = await ProyectarRecepcionAsync(a);

        var r = await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = recepcionId }, default);

        r.Estado.Should().Be(EstadoPasivo.Capturada);
        (await a.Db.FacturasProveedor.SingleAsync()).RecepcionId.Should().Be(recepcionId);
        a.Eventos.Integraciones.OfType<FacturaProveedorRegistradaIntegrationEvent>().Single().RecepcionId.Should().Be(recepcionId);
    }

    [Fact]
    public async Task Recepcion_inexistente_rechaza_con_RECEPCION_NO_ENCONTRADA()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();

        var captura = () => a.Handler.Handle(a.Command(4, 20) with { RecepcionId = Guid.NewGuid() }, default);

        (await captura.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Recepcion_de_otra_OC_rechaza_con_RECEPCION_DE_OTRA_OC()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var otroId = Guid.NewGuid();
        a.Db.RecepcionesOcLocal.Add(new RecepcionOcLocal(a.Current!.Value, otroId, "REC-OTRA", Guid.NewGuid(), new(2026, 10, 9), false, null, null, "[]", a.Ahora, a.Ahora));
        await a.Db.SaveChangesAsync();

        var captura = () => a.Handler.Handle(a.Command(4, 20) with { RecepcionId = otroId }, default);

        (await captura.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_DE_OTRA_OC");
    }

    [Fact]
    public async Task Recepcion_ya_ligada_a_factura_vigente_rechaza_con_RECEPCION_YA_FACTURADA()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var recepcionId = await ProyectarRecepcionAsync(a);
        await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = recepcionId }, default);

        var segunda = () => a.Handler.Handle(a.Command(4, 20) with { RecepcionId = recepcionId }, default);

        var error = (await segunda.Should().ThrowAsync<BusinessRuleException>()).Which;
        error.Code.Should().Be("RECEPCION_YA_FACTURADA");
        error.Message.Should().Contain("REC-P3-FICTICIA");
    }

    [Fact]
    public async Task Al_cancelar_la_factura_la_recepcion_queda_libre()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var recepcionId = await ProyectarRecepcionAsync(a);
        await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = recepcionId }, default);
        var primera = await a.Db.FacturasProveedor.SingleAsync();
        primera.Cancelar(MotivoCancelacion.ErrorCaptura, "prueba", null, a.Ahora);
        await a.Db.SaveChangesAsync();

        var r = await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = recepcionId }, default);

        r.Estado.Should().Be(EstadoPasivo.Capturada);
    }

    [Fact]
    public async Task Sin_recepcion_el_evento_lleva_null()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();

        await a.Handler.Handle(a.Command(4, 20), default);

        a.Eventos.Integraciones.OfType<FacturaProveedorRegistradaIntegrationEvent>().Single().RecepcionId.Should().BeNull();
    }
}
