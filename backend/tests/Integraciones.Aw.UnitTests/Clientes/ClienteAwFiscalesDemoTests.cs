using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Clientes;

public sealed class ClienteAwFiscalesDemoTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    [Fact]
    public async Task Demo_completa_fiscales_vacios_y_nunca_pisa_la_captura_local()
    {
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var aplicar = new AplicarClienteAwService(db);
        var s = new AplicarClienteAwSnapshot("DEMO-100", "CLIENTE DEMO", DateTime.UtcNow, "1", "0-borrador",
            MonedaDefault: "MXN", MonedaNormalizada: "MXN");
        await aplicar.AplicarAsync(s, default);
        var cliente = await db.Clientes.SingleAsync();
        cliente.Rfc.Should().BeNull();
        cliente.CodigoPostalFiscal.Should().BeNull();
        await aplicar.AplicarAsync(s with { Rfc = "XAXX010101000", CodigoPostalFiscal = "97000", LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) }, default);
        (cliente.Rfc, cliente.CodigoPostalFiscal).Should().Be(("XAXX010101000", "97000"));
        cliente.ActualizarDatos(rfc: "XEXX010101000", codigoPostalFiscal: "06600");
        await db.SaveChangesAsync();
        await aplicar.AplicarAsync(s with { Rfc = "XAXX010101000", CodigoPostalFiscal = "97100", LeidoEnUtc = DateTime.UtcNow.AddMinutes(2) }, default);
        (cliente.Rfc, cliente.CodigoPostalFiscal).Should().Be(("XEXX010101000", "06600"));
    }
}
