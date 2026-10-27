using Millet.Almacen.Domain.Ports;
using Millet.Compras.Application.Oc.Lineas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>
/// F1-MP-01 S1 (A4.1-b): la regla de medida que usan los handlers de agregar y editar línea de OC, con los mismos códigos que
/// Almacén. El cableado de los handlers (decimales derivados, persistencia, 422) se prueba por HTTP en
/// <c>OcLineaMedidaTests</c>: los handlers leen la OC con una propiedad compleja que el proveedor InMemory no soporta.
/// </summary>
public sealed class OcLineaMedidaResolverTests
{
    private static readonly Guid Articulo = Guid.NewGuid();
    private static readonly Guid Variante = Guid.NewGuid();

    [Fact]
    public async Task Vidrio_ligado_sin_medida_da_VARIANTE_VIDRIO_REQUERIDA()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OcLineaMedida.ResolverAsync(true, new Variantes(), Articulo, null, default));

        ex.Code.Should().Be("VARIANTE_VIDRIO_REQUERIDA");
    }

    [Fact]
    public async Task Articulo_sin_liga_con_medida_da_VARIANTE_NO_APLICA()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OcLineaMedida.ResolverAsync(false, new Variantes(), Articulo, Variante, default));

        ex.Code.Should().Be("VARIANTE_NO_APLICA");
    }

    [Fact]
    public async Task Medida_de_otro_producto_da_VARIANTE_NO_PERTENECE_AL_ARTICULO()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OcLineaMedida.ResolverAsync(true, new Variantes(), Articulo, Guid.NewGuid(), default));

        ex.Code.Should().Be("VARIANTE_NO_PERTENECE_AL_ARTICULO");
    }

    [Fact]
    public async Task Medida_sin_ancho_o_alto_en_A_W_da_VARIANTE_SIN_MEDIDA()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            OcLineaMedida.ResolverAsync(true, new Variantes(ancho: null), Articulo, Variante, default));

        ex.Code.Should().Be("VARIANTE_SIN_MEDIDA");
    }

    [Fact]
    public async Task Medida_valida_devuelve_ancho_y_alto_de_A_W_y_sin_liga_ni_medida_no_devuelve_nada()
    {
        var v = await OcLineaMedida.ResolverAsync(true, new Variantes(), Articulo, Variante, default);
        (v!.VarianteId, v.AnchoMm, v.AltoMm, v.AreaHojaM2).Should().Be((Variante, 2440m, 3660m, 8.9304m));

        (await OcLineaMedida.ResolverAsync(false, null, Articulo, null, default)).Should().BeNull();
    }

    private sealed class Variantes(decimal? ancho = 2440m) : IVarianteVidrioReadPort
    {
        public Task<VarianteVidrioLectura> ObtenerAsync(Guid articuloId, Guid varianteId, CancellationToken cancellationToken) =>
            varianteId == Variante
                ? Task.FromResult(new VarianteVidrioLectura(Variante, "2440x3660", ancho, 3660m, 6m))
                : throw new BusinessRuleException("VARIANTE_NO_PERTENECE_AL_ARTICULO", "La medida no pertenece al artículo.");

        public Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> varianteIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articuloId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
