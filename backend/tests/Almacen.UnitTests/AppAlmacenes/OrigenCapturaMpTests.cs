using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.AppAlmacenes;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.AppAlmacenes;

/// <summary>M-335: origen único de captura de materia prima por evento, con bitácora, pendientes y reversa.</summary>
public class OrigenCapturaMpTests
{
    private static readonly Guid Usuario = Guid.NewGuid();

    [Fact]
    public async Task Sin_cambios_el_evento_esta_en_transicion_y_acepta_los_dos_origenes()
    {
        await using var f = new P1Fixture();
        (await OrigenCapturaMpReglas.VigenteAsync(f.Db, OrigenCapturaMp.Recepcion, default)).Origen.Should().Be(OrigenCapturaMp.Ambos);
        await OrigenCapturaMpReglas.ExigirErpAsync(f.Db, OrigenCapturaMp.Recepcion, default);
        (await OrigenCapturaMpReglas.RechazoDeMensajeAsync(f.Db, "ENTRADA_ALMACEN", default)).Should().BeNull();
    }

    [Fact]
    public async Task Con_la_recepcion_en_la_app_el_ERP_no_registra_y_el_cambio_queda_en_bitacora()
    {
        await using var f = new P1Fixture();
        await OrigenCapturaMpReglas.CambiarAsync(f.Db, OrigenCapturaMp.Recepcion, OrigenCapturaMp.App, "Arranque 13-nov", Usuario, 0, default);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => OrigenCapturaMpReglas.ExigirErpAsync(f.Db, OrigenCapturaMp.Recepcion, default));
        ex.Code.Should().Be("ORIGEN_CAPTURA_APP");
        ex.Message.Should().Contain("app de Almacenes");
        // Los demás eventos no cambian.
        await OrigenCapturaMpReglas.ExigirErpAsync(f.Db, OrigenCapturaMp.Consumo, default);
        var b = await f.Db.OrigenesCapturaMp.AsNoTracking().SingleAsync();
        (b.Evento, b.OrigenAnterior, b.Origen, b.Motivo, b.CambiadoPor).Should()
            .Be((OrigenCapturaMp.Recepcion, OrigenCapturaMp.Ambos, OrigenCapturaMp.App, "Arranque 13-nov", Usuario));
    }

    [Fact]
    public async Task Con_pendientes_sin_conciliar_no_procede_el_cambio()
    {
        await using var f = new P1Fixture();
        f.Db.MensajesApp.Add(new MensajeApp("m-1", "ENTRADA_ALMACEN", "EN-1", "{}", "ERROR", "REV-m-1", "Artículo sin alta"));
        await f.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => OrigenCapturaMpReglas.CambiarAsync(
            f.Db, OrigenCapturaMp.Recepcion, OrigenCapturaMp.App, "Arranque", Usuario, 2, default));
        ex.Code.Should().Be("ORIGEN_CAPTURA_CON_PENDIENTES");
        ex.Message.Should().Contain("1 mensaje(s) en revisión").And.Contain("2 sin acuse");
        (await f.Db.OrigenesCapturaMp.CountAsync()).Should().Be(0);
        // Otro evento sin pendientes propios sí cambia (los sin acuse se leen aparte).
        await OrigenCapturaMpReglas.CambiarAsync(f.Db, OrigenCapturaMp.Merma, OrigenCapturaMp.App, "Arranque", Usuario, 0, default);
    }

    [Fact]
    public async Task Se_revierte_con_otro_cambio_y_el_ERP_rechaza_el_mensaje_de_la_app_con_motivo_claro()
    {
        await using var f = new P1Fixture();
        await OrigenCapturaMpReglas.CambiarAsync(f.Db, OrigenCapturaMp.Recepcion, OrigenCapturaMp.App, "Arranque", Usuario, 0, default);
        await OrigenCapturaMpReglas.CambiarAsync(f.Db, OrigenCapturaMp.Recepcion, OrigenCapturaMp.Erp, "La app falló", Usuario, 0, default);

        (await OrigenCapturaMpReglas.VigenteAsync(f.Db, OrigenCapturaMp.Recepcion, default)).Origen.Should().Be(OrigenCapturaMp.Erp);
        await OrigenCapturaMpReglas.ExigirErpAsync(f.Db, OrigenCapturaMp.Recepcion, default);
        (await OrigenCapturaMpReglas.RechazoDeMensajeAsync(f.Db, "ENTRADA_ALMACEN", default)).Should().Contain("se captura en el ERP");
        (await OrigenCapturaMpReglas.RechazoDeMensajeAsync(f.Db, "ENTRADA_MANUAL", default)).Should().BeNull();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => OrigenCapturaMpReglas.CambiarAsync(
            f.Db, OrigenCapturaMp.Recepcion, OrigenCapturaMp.Erp, "otra vez", Usuario, 0, default));
        ex.Code.Should().Be("ORIGEN_CAPTURA_SIN_CAMBIO");
        (await f.Db.OrigenesCapturaMp.CountAsync()).Should().Be(2);
    }
}
