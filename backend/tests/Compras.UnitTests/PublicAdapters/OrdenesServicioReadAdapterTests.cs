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
        Assert.Contains("estado", sql); Assert.DoesNotContain($"estado <> {(int)Millet.Compras.Domain.Oc.EstadoOrdenCompra.Cancelada}", sql); Assert.Contains("LIMIT", sql);
        if (texto is not null) { Assert.Contains("ILIKE @", sql); Assert.Contains("orden_compra_lineas", sql); }
    }
    [Fact]
    public void Mapear_total_suma_solo_lineas_de_servicio_en_oc_mixta()
    {
        var oc = new Millet.Compras.Domain.Oc.OrdenCompra(Guid.NewGuid(), Guid.NewGuid(), Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"), 2026,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow), sinRequisicionPrevia: true, motivoSinRequisicion: "mixta");
        oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 1m, "PZA", 1000m, Guid.NewGuid(), esServicio: true, centroCostoId: Guid.NewGuid());
        oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 1m, "PZA", 5000m, Guid.NewGuid(), esServicio: false, centroCostoId: Guid.NewGuid());
        var lectura = OrdenesServicioReadAdapter.Mapear(oc, "Proveedor", []);
        var servicio = Assert.Single(lectura.Lineas);
        Assert.Equal(servicio.Importe, lectura.Total);
        Assert.NotEqual(oc.CalcularTotales().TotalAPagar, lectura.Total);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => Guid.Empty;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
