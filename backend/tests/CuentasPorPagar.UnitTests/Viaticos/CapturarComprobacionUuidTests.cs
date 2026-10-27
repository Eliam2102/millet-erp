using Millet.CuentasPorPagar.Application.Viaticos.CapturarComprobacion;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Viaticos;

public sealed class CapturarComprobacionUuidTests
{
    private static CapturarLineaViaticosInput Linea(string uuid) => new(
        null, uuid, Guid.NewGuid(), "F-1", DateTimeOffset.UtcNow, 100m, 16m, 0m, 116m, "MXN", "Hospedaje", false);

    [Fact]
    public async Task Rechaza_el_mismo_UUID_dos_veces_en_la_misma_captura_aunque_cambie_la_capitalizacion()
    {
        await using var a = new P8Fixture();
        var s = SolicitudViaticos.Solicitar(a.Current!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "FIX", TipoDestinoViatico.Nacional,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), "MXN", 1000m, 6000m, 10, null, DateTimeOffset.UtcNow);
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        a.Db.Add(s); await a.Db.SaveChangesAsync();
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();

        var act = () => new CapturarComprobacionViaticosHandler(a.Db, a).Handle(
            new CapturarComprobacionViaticosCommand(s.Id, s.Version, [Linea(uuid), Linea(uuid.ToLowerInvariant())]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VIA_LINEA_FACTURA_DUPLICADA");
    }

    private static async Task<SolicitudViaticos> SolicitudAnticipadaAsync(P8Fixture a)
    {
        var s = SolicitudViaticos.Solicitar(a.Current!.Value, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "FIX", TipoDestinoViatico.Nacional,
            new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), "MXN", 1000m, 6000m, 10, null, DateTimeOffset.UtcNow);
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        a.Db.Add(s); await a.Db.SaveChangesAsync();
        return s;
    }

    [Fact]
    public async Task Rechaza_UUID_ya_comprobado_en_otro_viatico_pendiente_aunque_cambie_la_capitalizacion()
    {
        await using var a = new P8Fixture();
        var s1 = await SolicitudAnticipadaAsync(a);
        var s2 = await SolicitudAnticipadaAsync(a);
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        await new CapturarComprobacionViaticosHandler(a.Db, a).Handle(
            new CapturarComprobacionViaticosCommand(s1.Id, s1.Version, [Linea(uuid)]), default);

        var act = () => new CapturarComprobacionViaticosHandler(a.Db, a).Handle(
            new CapturarComprobacionViaticosCommand(s2.Id, s2.Version, [Linea(uuid.ToLowerInvariant())]), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VIA_LINEA_FACTURA_DUPLICADA");
    }
}
