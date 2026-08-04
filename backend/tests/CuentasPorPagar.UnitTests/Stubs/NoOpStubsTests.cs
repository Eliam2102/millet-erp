using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Stubs;

namespace Millet.CuentasPorPagar.UnitTests.Stubs;

/// <summary>
/// Smoke tests F0-PR1: validan que los 10 stubs <c>NoOp*</c> de los
/// puertos cross-module se comportan según el contrato (devuelven null
/// o lista vacía) y no lanzan en runtime. Cuando un adapter real
/// reemplaza al stub en su PR correspondiente, este test sigue
/// aplicando contra el stub residual hasta que se borre.
/// </summary>
public sealed class NoOpStubsTests
{
    [Fact]
    public async Task NoOpSucursalReadPort_devuelve_null_y_lista_vacia()
    {
        var port = new NoOpSucursalReadPort(NullLogger<NoOpSucursalReadPort>.Instance);
        (await port.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await port.ListarPorEmpresaAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();
    }

    // NoOpEmpleadoReadPort y NoOpPuestoReadPort retirados en ADM-PR2 —
    // sus adapters reales (Adapters.EmpleadoReadPortAdapter /
    // PuestoReadPortAdapter) se cubren en Adapters/PuestoEmpleadoReadAdaptersTests.

    [Fact]
    public async Task NoOpDependenciaRevisoraReadPort_devuelve_seed_local_de_4_dependencias()
    {
        var port = new NoOpDependenciaRevisoraReadPort(NullLogger<NoOpDependenciaRevisoraReadPort>.Instance);
        var lista = await port.ListarAsync(CancellationToken.None);
        lista.Should().HaveCount(4);
        lista.Select(d => d.Codigo).Should().Contain(["COMPRAS", "ALMACEN", "PRODUCCION", "DIRECCION_F"]);
    }

    [Fact]
    public async Task NoOpTipoCambioReadPort_devuelve_1_para_misma_moneda_y_null_en_otra_combinacion()
    {
        var port = new NoOpTipoCambioReadPort(NullLogger<NoOpTipoCambioReadPort>.Instance);
        var fecha = new DateOnly(2026, 5, 22);

        (await port.ObtenerAsync("MXN", "MXN", fecha, CancellationToken.None)).Should().Be(1m);
        (await port.ObtenerAsync("USD", "MXN", fecha, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NoOpProveedorReadPort_devuelve_null()
    {
        var port = new NoOpProveedorReadPort(NullLogger<NoOpProveedorReadPort>.Instance);
        (await port.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await port.ObtenerPorRfcAsync("AAA010101AAA", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NoOpArticuloReadPort_devuelve_null()
    {
        var port = new NoOpArticuloReadPort(NullLogger<NoOpArticuloReadPort>.Instance);
        (await port.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task NoOpComprasOcReadPort_devuelve_null_y_lista_vacia()
    {
        var port = new NoOpComprasOcReadPort(NullLogger<NoOpComprasOcReadPort>.Instance);
        (await port.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await port.ListarAutorizadasPorProveedorAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task NoOpConceptoContableReadPort_devuelve_null_y_lista_vacia()
    {
        var port = new NoOpConceptoContableReadPort(NullLogger<NoOpConceptoContableReadPort>.Instance);
        (await port.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
        (await port.ListarAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task NoOpAlmacenRecepcionReadPort_devuelve_null()
    {
        var port = new NoOpAlmacenRecepcionReadPort(NullLogger<NoOpAlmacenRecepcionReadPort>.Instance);
        (await port.ObtenerRecepcionDeOcAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }
}
