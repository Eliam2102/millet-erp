using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Revision;

/// <summary>F1-MP-01 S3: lo reservado para dictamen se lleva por (bin, artículo, variante de medida).</summary>
public sealed class ReservaDictamenVarianteTests
{
    [Fact]
    public async Task Pendiente_se_separa_por_variante_y_la_fila_sin_variante_es_propia()
    {
        var empresa = Guid.NewGuid();
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AlmacenDbContext(opts, new FakeEmpresa(empresa));
        var bin = Guid.NewGuid();
        var articulo = Guid.NewGuid();
        var varA = Guid.NewGuid();
        var varB = Guid.NewGuid();
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, empresa, new DateOnly(2027, 4, 1));
        mov.VincularRecepcionVarianteA(Guid.NewGuid(), null, Guid.NewGuid(), null);
        LineaMovimiento L(int pos, decimal cant, Guid? v, string? lote = null) => new(Guid.NewGuid(), mov.Id, pos, articulo, cant, "M2", 1m,
            ubicacionId: bin, productoAwVarianteId: v, anchoMm: v is null ? null : 2440m, altoMm: v is null ? null : 3660m, lote: lote);
        mov.AgregarLinea(L(1, 17.8608m, varA));
        mov.AgregarLinea(L(2, 7.062m, varB));
        mov.AgregarLinea(L(3, 8.9304m, varA));
        mov.AgregarLinea(L(4, 2m, null));
        mov.AgregarLinea(L(5, 3m, varB, " L-1 "));
        mov.AgregarLinea(L(6, 4m, varB, "L-2"));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2027, 1), Guid.NewGuid());
        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();

        var pendiente = await ReservaDictamen.PendientePorBinAsync(db, [bin], default);

        pendiente[(bin, articulo, varA, (string?)null)].Should().Be(26.7912m);
        pendiente[(bin, articulo, varB, (string?)null)].Should().Be(7.062m);
        pendiente[(bin, articulo, (Guid?)null, (string?)null)].Should().Be(2m);
        // El lote es parte de la llave (y se guarda recortado).
        pendiente[(bin, articulo, varB, "L-1")].Should().Be(3m);
        pendiente[(bin, articulo, varB, "L-2")].Should().Be(4m);
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
