using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Api.UnitTests.DatosMaestros;

public sealed class ExpedienteProveedorListadoTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero); }

    [Fact]
    public void Resumen_y_filtro_son_una_consulta_SQL_agregada()
    {
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseNpgsql("Host=localhost;Database=design_only").UseSnakeCaseNamingConvention().Options, new Empresa());
        var pendientes = ExpedienteAdjuntos.PendientesProveedores(db, new Reloj());
        var sql = db.Proveedores.Select(p => new { p.Id, Cantidad = pendientes.Count(x => x.ProveedorId == p.Id) })
            .Where(x => x.Cantidad > 0).OrderBy(x => x.Id).Take(10).ToQueryString();
        Assert.Contains("count(*)", sql);
        Assert.Contains("LIMIT", sql);
    }

    [Theory]
    [InlineData(TipoPersonaProveedor.Moral, OrigenProveedor.Nacional)]
    [InlineData(TipoPersonaProveedor.Fisica, OrigenProveedor.Extranjero)]
    [InlineData(TipoPersonaProveedor.Fisica, null)]
    public async Task Agregado_coincide_con_puerto_en_faltantes_vencidos_bajas_y_excepciones(TipoPersonaProveedor persona, OrigenProveedor? origen)
    {
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        await db.Database.EnsureCreatedAsync();
        var proveedor = new Proveedor(Guid.NewGuid(), "PRUEBA", "Proveedor sintético", "XEXX010101000", persona, origenCarga: "CargaSap");
        if (origen.HasValue) proveedor.ActualizarClasificacionFiscal(origen, origen == OrigenProveedor.Nacional ? "601" : null, null);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync();
        var reloj = new Reloj();
        var puerto = new ExpedienteProveedorReadAdapter(db, reloj);
        async Task ComprobarAsync()
        {
            var resumen = (await puerto.ObtenerAsync(proveedor.Id, default))!;
            var codigos = await ExpedienteAdjuntos.PendientesProveedores(db, reloj).Where(x => x.ProveedorId == proveedor.Id).Select(x => x.Codigo).ToArrayAsync();
            Assert.Equal(resumen.Faltantes.Concat(resumen.Vencidos).Order(), codigos.Order());
            Assert.Equal(resumen.Completo, codigos.Length == 0);
        }
        await ComprobarAsync();
        var tipo = await db.AdjuntoTiposDocumento.FirstAsync(t => t.Codigo == "contrato" && t.TipoEntidad == "proveedor");
        var viejo = new Adjunto(Guid.NewGuid(), "proveedor", proveedor.Id, null, tipo.Id, "prueba.pdf", "application/pdf", 20,
            new string('a', 64), "prueba/blob", new DateOnly(2026, 10, 28), Guid.NewGuid(), reloj.UtcNow.AddHours(-1));
        db.Adjuntos.Add(viejo); await db.SaveChangesAsync();
        await ComprobarAsync();
        var nuevo = new Adjunto(Guid.NewGuid(), "proveedor", proveedor.Id, null, tipo.Id, "prueba.pdf", "application/pdf", 20,
            new string('b', 64), "prueba/blob2", new DateOnly(2026, 11, 29), Guid.NewGuid(), reloj.UtcNow);
        db.Adjuntos.Add(nuevo); await db.SaveChangesAsync();
        await ComprobarAsync();
        nuevo.DarDeBaja("Baja sintética de prueba", Guid.NewGuid(), reloj.UtcNow); await db.SaveChangesAsync();
        await ComprobarAsync();
        var excepcion = new AdjuntoExcepcion("proveedor", proveedor.Id, tipo.Id, "Excepción sintética", null, null, reloj.UtcNow);
        db.AdjuntoExcepciones.Add(excepcion); await db.SaveChangesAsync();
        await ComprobarAsync();
        excepcion.Revocar(null, reloj.UtcNow); await db.SaveChangesAsync();
        await ComprobarAsync();
        foreach (var aplica in Enum.GetValues<AdjuntoAplicaOrigen>())
        {
            tipo.Configurar(tipo.Nombre, true, false, null, false, aplica, true);
            await db.SaveChangesAsync();
            await ComprobarAsync();
        }
        tipo.Configurar(tipo.Nombre, true, true, null, false, AdjuntoAplicaOrigen.Todos, false);
        await db.SaveChangesAsync();
        await ComprobarAsync();
    }
}
