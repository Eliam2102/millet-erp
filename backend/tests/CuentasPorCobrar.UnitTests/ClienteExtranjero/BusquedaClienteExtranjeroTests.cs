using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Adapters;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.ClienteExtranjero;

/// <summary>
/// R2 (CXC-11): el alta marca genérico a cualquier RFC <c>XEXX…</c>/<c>XAXX…</c>. La búsqueda de clientes de CxC
/// debe encontrar al extranjero (para darle línea o vincularlo) y seguir excluyendo al público en general.
/// </summary>
public sealed class BusquedaClienteExtranjeroTests
{
    [Fact]
    public async Task La_busqueda_encuentra_al_extranjero_generico_y_excluye_al_publico_en_general()
    {
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"cxc-busqueda-{Guid.NewGuid():N}").Options, new SinEmpresa());
        var extranjero = new Cliente(Guid.CreateVersion7(), "EXT-1", "Cliente extranjero ficticio", rfc: "XEXX010101000",
            esGenerico: true, numRegIdTrib: "123456789", paisResidencia: "CAN");
        var publico = new Cliente(Guid.CreateVersion7(), "PG-1", "Público en general", rfc: "XAXX010101000", esGenerico: true);
        var nacional = new Cliente(Guid.CreateVersion7(), "NAC-1", "Cliente nacional ficticio", rfc: "AAA010101AAA");
        db.Clientes.AddRange(extranjero, publico, nacional);
        await db.SaveChangesAsync();
        var adapter = new ClienteReadPortAdapter(db, new SinEmpresa());

        var porRfc = await adapter.BuscarAsync("xx0101", null, null, 10, default);
        var porIds = await adapter.BuscarAsync(null, null, [extranjero.Id, publico.Id, nacional.Id], 10, default);

        porRfc.Select(c => c.Id).Should().BeEquivalentTo([extranjero.Id]);
        porIds.Select(c => c.Id).Should().BeEquivalentTo([extranjero.Id, nacional.Id]);
    }

    private sealed class SinEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
