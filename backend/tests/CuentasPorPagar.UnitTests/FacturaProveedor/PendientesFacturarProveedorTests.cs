using System.Text.Json;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;
using Millet.CuentasPorPagar.Domain.Almacen;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Tests.P3;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class PendientesFacturarProveedorTests
{
    private const string Rfc = "AAA010101AAA";

    [Fact]
    public async Task Devuelve_solo_OC_con_saldo_con_sus_recepciones_y_respeta_sucursal_y_RFC()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var proveedorId = Guid.NewGuid();
        var sucA = Guid.NewGuid();
        var sucB = Guid.NewGuid();
        var ocConSaldo1 = Oc(a, proveedorId, sucA, "OC-1", 10, 4);
        var ocSinSaldo = Oc(a, proveedorId, sucA, "OC-0", 10, 10);
        var ocConSaldo2 = Oc(a, proveedorId, sucB, "OC-2", 10, 0);
        var lineas = JsonSerializer.Serialize(new[] { new { lineaOcId = ocConSaldo1.Lineas[0].Id, cantidad = 3m }, new { lineaOcId = ocConSaldo1.Lineas[0].Id, cantidad = 2m } });
        a.Db.RecepcionesOcLocal.AddRange(
            new RecepcionOcLocal(a.Current!.Value, Guid.NewGuid(), "REC-1", ocConSaldo1.Id, new(2026, 10, 1), false, null, null, lineas, a.Ahora, a.Ahora),
            new RecepcionOcLocal(a.Current!.Value, Guid.NewGuid(), "REC-2", ocConSaldo1.Id, new(2026, 10, 5), true, null, null, "[]", a.Ahora, a.Ahora),
            new RecepcionOcLocal(a.Current!.Value, Guid.NewGuid(), "REC-X", ocSinSaldo.Id, new(2026, 10, 5), false, null, null, "[]", a.Ahora, a.Ahora));
        await a.Db.SaveChangesAsync();
        var puertos = new Puertos(proveedorId, [ocConSaldo1, ocSinSaldo, ocConSaldo2]);
        var handler = new PendientesFacturarProveedorHandler(a.Db, puertos, puertos);

        var todas = await handler.Handle(new(" " + Rfc.ToLowerInvariant() + " "), default);
        todas.ProveedorId.Should().Be(proveedorId);
        todas.Ocs.Select(o => o.Folio).Should().Equal("OC-1", "OC-2");
        todas.Ocs[0].CantidadPorFacturar.Should().Be(6);
        todas.Ocs[0].Recepciones.Select(r => r.Folio).Should().Equal("REC-2", "REC-1");
        todas.Ocs[0].Recepciones[1].CantidadRecibida.Should().Be(5);
        todas.Ocs[0].Recepciones[0].FacturaPendiente.Should().BeTrue();
        todas.Ocs[1].Recepciones.Should().BeEmpty();

        var acotada = await handler.Handle(new(Rfc) { SucursalesPermitidas = [sucA] }, default);
        acotada.Ocs.Select(o => o.Folio).Should().Equal("OC-1");

        var desconocido = await handler.Handle(new("ZZZ990101ZZZ"), default);
        desconocido.ProveedorId.Should().BeNull();
        desconocido.Ocs.Should().BeEmpty();
    }

    private static OrdenCompraDto Oc(P3FacturaConOcTests.Ambiente a, Guid proveedorId, Guid sucursalId, string folio, decimal cantidad, decimal facturada) =>
        new(Guid.NewGuid(), folio, proveedorId, a.Current!.Value, sucursalId, cantidad * 20, "Autorizada",
            [new(Guid.NewGuid(), Guid.NewGuid(), cantidad, 20, facturada, cantidad)]);

    private sealed class Puertos(Guid proveedorId, IReadOnlyList<OrdenCompraDto> ocs) : IProveedorReadPort, IComprasOcReadPort
    {
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult<ProveedorDto?>(rfc == Rfc ? new(proveedorId, Rfc, "Proveedor SA", null, false, true) : null);
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == proveedorId ? ocs : []);
        public Task<ProveedorDto?> ObtenerAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        Task<OrdenCompraDto?> IComprasOcReadPort.ObtenerAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
