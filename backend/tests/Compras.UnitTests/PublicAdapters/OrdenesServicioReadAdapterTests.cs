using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
namespace Millet.Compras.UnitTests.PublicAdapters;

public sealed class OrdenesServicioReadAdapterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("OC-P6")]
    [InlineData("' OR 1=1 --")]
    public void Busqueda_parametrizada_acota_empresa_sucursal_proveedor_y_servicios(string? texto)
    {
        using var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
            .UseNpgsql("Host=invalid;Database=p6").UseSnakeCaseNamingConvention().Options, new Empresa());
        var sql = OrdenesServicioReadAdapter.CrearConsulta(db, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), texto).ToQueryString();
        Assert.Contains("empresa_id", sql); Assert.Contains("sucursal_destino_id", sql);
        Assert.Contains("proveedor_id", sql); Assert.Contains("es_servicio", sql);
        Assert.Contains($"estado <> {(int)Millet.Compras.Domain.Oc.EstadoOrdenCompra.Cancelada}", sql); Assert.Contains("LIMIT", sql);
        if (texto is not null) { Assert.Contains("ILIKE @", sql); Assert.Contains("orden_compra_lineas", sql); }
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => Guid.Empty;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
