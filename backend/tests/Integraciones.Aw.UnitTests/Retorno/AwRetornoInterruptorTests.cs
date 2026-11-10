using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Xunit;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

/// <summary>10-nov: con IntegracionesAw:Retorno:Habilitado=false no hay adaptador Real de aw_retorno (el ERP no escribe en SQL Server).</summary>
public sealed class AwRetornoInterruptorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Habilitado_decide_si_hay_adaptador_Real(string? habilitado, bool esperado)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AwIntegracionDb"] = "Server=tcp:localhost,1433;Database=test;User Id=sa;Password=test;TrustServerCertificate=True;",
            ["IntegracionesAw:Retorno:Habilitado"] = habilitado,
        }).Build();
        var services = new ServiceCollection();

        services.AddIntegracionesAwPedidosAdapters(config);

        services.Any(d => d.ServiceType == typeof(IAwRetornoPort))
            .Should().Be(esperado);
    }
}
