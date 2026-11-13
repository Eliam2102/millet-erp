using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.UnitTests.Application;

/// <summary>Paginación servidor compartida: más de 200 filas se alcanzan por páginas y el total es el real.</summary>
public sealed class PaginacionTests
{
    private sealed class Fila { public int Id { get; set; } }

    private sealed class Ctx(DbContextOptions<Ctx> o) : DbContext(o)
    {
        public DbSet<Fila> Filas => Set<Fila>();
    }

    private static async Task<Ctx> SembrarAsync(int n)
    {
        var db = new Ctx(new DbContextOptionsBuilder<Ctx>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Filas.AddRange(Enumerable.Range(1, n).Select(i => new Fila { Id = i }));
        await db.SaveChangesAsync();
        return db;
    }

    [Theory]
    [InlineData(-5, 0, 0, 50)]    // offset negativo → 0; límite 0 → por omisión
    [InlineData(10, 25, 10, 25)]
    [InlineData(0, 9999, 0, 200)] // tope
    [InlineData(0, -1, 0, 50)]
    public void Normalizar_acota_offset_y_limite(int offset, int limit, int esperadoOffset, int esperadoLimit)
        => PaginacionExtensions.Normalizar(offset, limit).Should().Be((esperadoOffset, esperadoLimit));

    [Fact]
    public async Task Alcanza_filas_mas_alla_de_las_primeras_200_y_cuenta_el_total_real()
    {
        using var db = await SembrarAsync(450);

        var pagina = await db.Filas.OrderBy(f => f.Id).ToPaginaAsync(400, 50, CancellationToken.None);

        pagina.Total.Should().Be(450);
        pagina.Items.Should().HaveCount(50);
        pagina.Items[0].Id.Should().Be(401);
        pagina.Items[^1].Id.Should().Be(450);
    }

    [Fact]
    public async Task Offset_fuera_de_rango_devuelve_pagina_vacia_con_el_total()
    {
        using var db = await SembrarAsync(3);

        var pagina = await db.Filas.OrderBy(f => f.Id).ToPaginaAsync(10, 5, CancellationToken.None);

        pagina.Total.Should().Be(3);
        pagina.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("10448856", "%10448856%")]
    [InlineData("50%_off", "%50\\%\\_off%")]
    [InlineData("a\\b", "%a\\\\b%")]
    public void PatronContiene_escapa_comodines_del_usuario(string texto, string esperado)
        => PaginacionExtensions.PatronContiene(texto).Should().Be(esperado);
}
