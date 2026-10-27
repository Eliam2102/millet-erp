using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using MediatR;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>F1-MP-01 S1 sobre PostgreSQL real: índice único 1:1 del vínculo y puerto de variante.</summary>
public sealed class ArticuloProductoAwIntegrationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Liga_1a1_Y_Puerto_Valida_Variantes_Contra_Postgres()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var referencia = "MP" + sufijo;
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<AplicarProductoAwService>().AplicarAsync(
            new AplicarProductoAwSnapshot(referencia, "VIDRIO " + sufijo, "PZA", false,
                [new ProductoAwVarianteDato("2440x3660", 3660m, 2440m, 6m, null),
                 new ProductoAwVarianteDato("2140x3300", 3300m, 2140m, 6m, null)],
                DateTime.UtcNow, "1", "m1"), CancellationToken.None);
        var db = sp.GetRequiredService<CompartidoDbContext>();
        var producto = await db.ProductosAw.Include(p => p.Variantes).SingleAsync(p => p.ReferenciaExterna == referencia);
        var a1 = new Articulo(Guid.NewGuid(), "V1" + sufijo, "Vidrio 1", "M2");
        var a2 = new Articulo(Guid.NewGuid(), "V2" + sufijo, "Vidrio 2", "M2");
        db.AddRange(a1, a2);
        await db.SaveChangesAsync();

        var mediator = sp.GetRequiredService<IMediator>();
        static ActualizarArticuloCommand Liga(Guid articulo, Guid producto) =>
            new(articulo, null, null, null, null, null, null, null, false, false, false, producto);
        await mediator.Send(Liga(a1.Id, producto.Id));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => mediator.Send(Liga(a2.Id, producto.Id)));
        Assert.Equal("ARTICULO_PRODUCTO_AW_YA_LIGADO", ex.Code);

        var puerto = sp.GetRequiredService<IVarianteVidrioReadPort>();
        var v = producto.Variantes.Single(x => x.ClaveVariante == "2140x3300");
        Assert.Equal(7.0620m, (await puerto.ObtenerAsync(a1.Id, v.Id, CancellationToken.None)).AreaHojaM2);
        Assert.Equal(2, (await puerto.ListarPorArticuloAsync(a1.Id, CancellationToken.None)).Count);
        var ajena = await Assert.ThrowsAsync<BusinessRuleException>(() => puerto.ObtenerAsync(a2.Id, v.Id, CancellationToken.None));
        Assert.Equal("VARIANTE_NO_PERTENECE_AL_ARTICULO", ajena.Code);

        // Carrera: sin la pre-validación, el índice único rechaza la segunda liga con el nombre que mapea GlobalExceptionHandler.
        using var otro = factory.Services.CreateScope();
        var db2 = otro.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var a2b = await db2.Articulos.SingleAsync(a => a.Id == a2.Id);
        a2b.AsignarProductoAw(producto.Id);
        var violacion = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal("ux_articulos_producto_aw_id", (violacion.InnerException as Npgsql.PostgresException)?.ConstraintName);
    }
}
