using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.UnitTests.TestDoubles;

internal static class ReceptorFiscalTestFactory
{
    public static ValidadorReceptorFiscal Crear(FacturacionDbContext db,
        string rfc = "AAA010101AAA", string regimen = "601", string cp = "97000") =>
        new(new FakeClientesReadPort(new ClienteFiscalLectura(Guid.Empty, rfc,
            "Cliente prueba", regimen, cp, "G03", "01", "PUE", "MXN", false)),
            new FakeCatalogosSatReadPort(), db);
}
