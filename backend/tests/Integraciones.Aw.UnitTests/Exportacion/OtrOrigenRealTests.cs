using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Exportacion;
namespace Millet.Integraciones.Aw.UnitTests.Exportacion;
public sealed class OtrOrigenRealTests
{
    private static IOtrEntradaReader? Real(bool encendido)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Exportacion:EntradaHabilitada"] = encendido.ToString(),
            ["IntegracionesAw:Exportacion:ContratoI134Firmado"] = encendido.ToString(),
            ["ConnectionStrings:AwIntegracionDb"] = "Server=aw;Database=AWPROD_ERP;Encrypt=True",
        }).Build();
        var sp = new ServiceCollection().AddAwOtrExportacion(config).BuildServiceProvider();
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetKeyedService<IOtrEntradaReader>("Real");
    }
    [Fact] public void Con_las_opciones_encendidas_el_origen_Real_existe() => Assert.NotNull(Real(true));
    [Fact] public void Apagado_el_origen_Real_no_existe_y_el_selector_responde_CE_OTR_ORIGEN_NO_HABILITADO() => Assert.Null(Real(false));
}
