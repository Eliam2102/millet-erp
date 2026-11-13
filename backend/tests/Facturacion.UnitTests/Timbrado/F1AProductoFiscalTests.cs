using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Ports;
using Microsoft.EntityFrameworkCore;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.DatosMaestros;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.UnitTests.Timbrado;
public sealed class F1AProductoFiscalTests
{
    private sealed class Productos(ProductoFiscalLectura maestro) : IProductosReadPort
    {
        public Task<ProductoFiscalLectura?> ObtenerAsync(Guid productoId,CancellationToken cancellationToken) => Task.FromResult<ProductoFiscalLectura?>(maestro);
        public Task<ProductoFiscalLectura?> ResolverPorReferenciaAsync(string referenciaExterna,CancellationToken cancellationToken) => Task.FromResult<ProductoFiscalLectura?>(maestro);
        public Task<IReadOnlyList<ProductoAwBusquedaItem>> BuscarAsync(string? referencia,string? descripcion,int limit,CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProductoAwBusquedaItem>>([]);
    }
    [Fact]
    public async Task U19bc_lista_todos_los_productos_y_campos_sin_aceptar_claves_suplidas_por_la_linea()
    {
        var id=Guid.NewGuid(); var p=new Productos(new(id,"PROCESO FICTICIO",null,"H87",null,null,null,null,"Aw",EstadoFiscal:"FiscalPendiente"));
        var error=await Assert.ThrowsAsync<ProductosFiscalesIncompletosException>(()=>ValidadorProductoFiscal.ValidarAsync([
            new(1,id,"Cambio en solicitud","30171706","H87","02",0.16m),new(2,null,"RELLENO FICTICIO",null,null,null,null)],p,CancellationToken.None));
        Assert.Equal(2,error.Productos.Count); Assert.Equal("PROCESO FICTICIO",error.Productos[0].Producto); Assert.Contains("Tasa de IVA",error.Productos[0].Campos); Assert.Equal(4,error.Productos[1].Campos.Count);
    }
    [Fact]
    public async Task Por_regla_sin_revisar_con_tasa_cero_puede_timbrar()
    {
        var id=Guid.NewGuid(); var p=new Productos(new(id,"VIDRIO FICTICIO","30171706","H87","02",0m,null,null,"Aw",EstadoFiscal:"PorReglaSinRevisar"));
        await ValidadorProductoFiscal.ValidarAsync([new(1,id,"VIDRIO FICTICIO","30171706","H87","02",0m)],p,CancellationToken.None);
    }
    [Fact]
    public async Task V52_inactivo_bloquea_venta_nueva_y_permite_validar_cobro_anterior()
    {
        using var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Guid.NewGuid()));
        var id = Guid.NewGuid();
        var cliente = new ClienteFiscalLectura(id, "AAA010101AAA", "FICTICIO", "601", "97000", "G03", "03", "PUE", "MXN", false, Activo: false);
        var validador = new ValidadorReceptorFiscal(new FakeClientesReadPort(cliente), new FakeCatalogosSatReadPort(), db);
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "FICTICIO", "601", "97000", "G03", "MEX", false);
        var emisor = new DatosFiscalesEmisor("BBB010101BBB", "FICTICIO", "601", "97000");
        var error = await Assert.ThrowsAsync<ReceptorFiscalInvalidoException>(() => validador.ValidarAsync(receptor, emisor, id, CancellationToken.None, ventaNueva: true));
        Assert.Contains("inactivo", error.Message);
        await validador.ValidarAsync(receptor, emisor, id, CancellationToken.None);
    }
    [Theory]
    [InlineData("XAXX010101000", "601", "G03", "03", "PUE")]
    [InlineData("AAA010101AAA", "601", "G03", "03", "PPD")]
    [InlineData("AAA010101AAA", "601", "G03", "99", "PUE")]
    public async Task Alta_manual_reutiliza_U19_y_valida_metodo_forma(string rfc, string regimen, string uso, string forma, string metodo)
    {
        var adapter = new ValidacionAltaClienteFiscalAdapter(new FakeCatalogosSatReadPort());
        await Assert.ThrowsAsync<BusinessRuleException>(() => adapter.ValidarAsync(new(rfc, "FICTICIO", regimen, "97000", uso, "MEX", forma, metodo), CancellationToken.None));
    }
    [Theory]
    [InlineData("03", "PUE")]
    [InlineData("99", "PPD")]
    public async Task Alta_manual_completa_valida_formas_autorizadas(string forma, string metodo)
    {
        await new ValidacionAltaClienteFiscalAdapter(new FakeCatalogosSatReadPort())
            .ValidarAsync(new("AAA010101AAA", "FICTICIO", "601", "97000", "G03", "MEX", forma, metodo), CancellationToken.None);
    }

    [Fact]
    public async Task FB48_Lecturas_producto_resuelven_FK_antes_de_codigo_legacy_y_conservan_null()
    {
        using var db = new Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext(
            new DbContextOptionsBuilder<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Guid.NewGuid()));
        var piezas = new Millet.Catalogos.Domain.UnidadMedida(Guid.NewGuid(), "PZA", "Pieza ficticia",
            Millet.Catalogos.Domain.DimensionUnidad.Conteo, 1, 0, true);
        var kilos = new Millet.Catalogos.Domain.UnidadMedida(Guid.NewGuid(), "KG", "Kg ficticio",
            Millet.Catalogos.Domain.DimensionUnidad.Peso, 1, 3, true);
        db.UnidadesMedida.AddRange(piezas, kilos);
        var porFk = new ProductoAw(Guid.NewGuid(), "FB48-FK", "FICTICIO FK", "PZA", unidadMedidaId: kilos.Id);
        var legacy = new ProductoAw(Guid.NewGuid(), "FB48-LEGACY", "FICTICIO LEGACY", " pza. ");
        var desconocido = new ProductoAw(Guid.NewGuid(), "FB48-UNKNOWN", "FICTICIO UNKNOWN", "LEGACY");
        db.ProductosAw.AddRange(porFk, legacy, desconocido);
        await db.SaveChangesAsync();
        var adapter = new ProductosReadAdapter(db);
        Assert.Equal(3, (await adapter.ObtenerAsync(porFk.Id, CancellationToken.None))!.DecimalesCantidad);
        Assert.Equal(0, (await adapter.ResolverPorReferenciaAsync(legacy.ReferenciaExterna, CancellationToken.None))!.DecimalesCantidad);
        Assert.Null((await adapter.ObtenerAsync(desconocido.Id, CancellationToken.None))!.DecimalesCantidad);
        var lookup = await adapter.BuscarAsync("FB48-", null, 20, CancellationToken.None);
        Assert.Equal(3, lookup.Single(x => x.ProductoId == porFk.Id).DecimalesCantidad);
        Assert.Equal(0, lookup.Single(x => x.ProductoId == legacy.Id).DecimalesCantidad);
        Assert.Null(lookup.Single(x => x.ProductoId == desconocido.Id).DecimalesCantidad);
    }

}
