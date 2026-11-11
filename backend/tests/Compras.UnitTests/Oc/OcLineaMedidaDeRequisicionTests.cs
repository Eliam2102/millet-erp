using Millet.Almacen.Domain.Ports;
using Millet.Compras.Application.Oc.Lineas;

namespace Millet.Compras.UnitTests.Oc;

/// <summary>La partida de la OC hereda la medida de la hoja que trae la RQ de la app de Almacenes (cualquier orientación).</summary>
public sealed class OcLineaMedidaDeRequisicionTests
{
    private static readonly Guid Articulo = Guid.CreateVersion7();
    private static readonly VarianteVidrioLectura H1200x2600 = new(Guid.CreateVersion7(), "2509", 1200m, 2600m, 6m);
    private static readonly VarianteVidrioLectura H1800x2100 = new(Guid.CreateVersion7(), "4549", 1800m, 2100m, 6m);

    [Theory]
    [InlineData(1200, 2600)]
    [InlineData(2600, 1200)]
    public async Task Hereda_la_medida_del_catalogo_en_cualquier_orientacion(int ancho, int alto)
    {
        var v = await OcLineaMedida.DeRequisicionAsync(new Variantes(), Articulo, ancho, alto, default);
        Assert.Equal(H1200x2600.VarianteId, v!.VarianteId);
    }

    [Fact]
    public async Task Sin_medida_sin_puerto_o_sin_coincidencia_no_hereda()
    {
        Assert.Null(await OcLineaMedida.DeRequisicionAsync(new Variantes(), Articulo, null, null, default));
        Assert.Null(await OcLineaMedida.DeRequisicionAsync(null, Articulo, 1200, 2600, default));
        Assert.Null(await OcLineaMedida.DeRequisicionAsync(new Variantes(), Articulo, 3302, 6100, default));
    }

    private sealed class Variantes : IVarianteVidrioReadPort
    {
        public Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VarianteVidrioLectura>>(articuloId == Articulo ? [H1800x2100, H1200x2600] : []);
    }
}
