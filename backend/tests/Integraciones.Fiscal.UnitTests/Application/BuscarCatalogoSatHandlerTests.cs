using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Millet.Integraciones.Fiscal.Application.CatalogosSat;
using Millet.Integraciones.Fiscal.Domain.Exceptions;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

public sealed class BuscarCatalogoSatHandlerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Sin_empresa_en_contexto_lanza_forbidden()
    {
        var (handler, _, _) = Build(empresaId: null);

        var act = () => handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.ClaveProdServ, "vidrio"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("EMPRESA_NO_SELECCIONADA");
    }

    [Fact]
    public async Task Busqueda_por_texto_va_al_puerto_y_cachea()
    {
        var (handler, puerto, _) = Build();
        puerto.ResultadosBusqueda = [new CatalogoSatItem("43211701", "Computadoras de escritorio")];

        var query = new BuscarCatalogoSatQuery(CatalogoSat.ClaveProdServ, "computadora");
        var primera = await handler.Handle(query, CancellationToken.None);
        var segunda = await handler.Handle(query, CancellationToken.None);

        primera.Should().ContainSingle(i => i.Codigo == "43211701");
        segunda.Should().BeEquivalentTo(primera);
        puerto.Busquedas.Should().HaveCount(1, "la segunda llamada debe salir del caché");
    }

    [Fact]
    public async Task Texto_corto_hace_lookup_exacto_por_codigo_en_mayusculas()
    {
        var (handler, puerto, _) = Build();
        puerto.ResultadoPorCodigo = new CatalogoSatItem("MTK", "Metro cuadrado");

        var items = await handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.ClaveUnidad, "mtk"), CancellationToken.None);

        items.Should().ContainSingle(i => i.Codigo == "MTK");
        puerto.Busquedas.Should().BeEmpty();
        puerto.LookupsPorCodigo.Should().ContainSingle().Which.Codigo.Should().Be("MTK");
    }

    [Theory]
    [InlineData("T")]
    [InlineData("TEM")]
    [InlineData("432")]
    public async Task ClaveProdServ_con_texto_corto_devuelve_vacio_sin_llamar_al_pac(string buscar)
    {
        // Las claves de c_ClaveProdServ son de 8 dígitos: 1–3 caracteres es
        // el operador a media palabra — antes se disparaba un lookup con
        // HTTP 400 garantizado por cada tecleo (FAC-DET-PR8).
        var (handler, puerto, _) = Build();

        var items = await handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.ClaveProdServ, buscar), CancellationToken.None);

        items.Should().BeEmpty();
        puerto.Busquedas.Should().BeEmpty();
        puerto.LookupsPorCodigo.Should().BeEmpty();
    }

    [Fact]
    public async Task Codigo_inexistente_devuelve_vacio_y_no_cachea_el_miss()
    {
        var (handler, puerto, _) = Build();
        puerto.ResultadoPorCodigo = null;

        var query = new BuscarCatalogoSatQuery(CatalogoSat.ClaveUnidad, "MT");
        (await handler.Handle(query, CancellationToken.None)).Should().BeEmpty();

        puerto.ResultadoPorCodigo = new CatalogoSatItem("MT", "algo");
        (await handler.Handle(query, CancellationToken.None)).Should().ContainSingle();
        puerto.LookupsPorCodigo.Should().HaveCount(2, "el miss no debe quedar cacheado");
    }

    [Fact]
    public async Task Objeto_imp_sin_texto_lista_el_catalogo_completo()
    {
        var (handler, puerto, _) = Build();
        puerto.ResultadosBusqueda =
        [
            new CatalogoSatItem("01", "No objeto de impuesto."),
            new CatalogoSatItem("02", "Sí objeto de impuesto."),
        ];

        var items = await handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.ObjetoImp, ""), CancellationToken.None);

        items.Should().HaveCount(2);
        puerto.Busquedas.Should().ContainSingle()
            .Which.SearchText.Should().Be("impuesto", "default para listar c_ObjetoImp completo");
    }

    [Fact]
    public async Task Pac_no_disponible_propaga_la_excepcion_503()
    {
        var (handler, puerto, _) = Build();
        puerto.Explota = true;

        var act = () => handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.ClaveProdServ, "vidrio"), CancellationToken.None);

        await act.Should().ThrowAsync<CatalogoSatNoDisponibleException>();
    }

    [Fact]
    public void Validator_exige_texto_salvo_para_objeto_imp()
    {
        var validator = new BuscarCatalogoSatValidator();

        validator.Validate(new BuscarCatalogoSatQuery(CatalogoSat.ClaveProdServ, ""))
            .IsValid.Should().BeFalse();
        validator.Validate(new BuscarCatalogoSatQuery(CatalogoSat.ObjetoImp, ""))
            .IsValid.Should().BeTrue();
        validator.Validate(new BuscarCatalogoSatQuery(CatalogoSat.ClaveUnidad, "metro", Limit: 0))
            .IsValid.Should().BeFalse();
        validator.Validate(new BuscarCatalogoSatQuery(CatalogoSat.ClaveUnidad, "metro", Limit: 51))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Nombres_remotos_default_por_catalogo()
    {
        var opts = new FiscalApiSdkAdapterOptions();
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.ClaveProdServ, opts).Should().Be("SatProductCodes");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.ClaveUnidad, opts).Should().Be("SatUnitMeasurements");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.ObjetoImp, opts).Should().Be("SatTaxObjects");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.FraccionArancelaria, opts).Should().Be("SatFraccionArancelaria");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.UnidadAduana, opts).Should().Be("SatUnidadAduana");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.Pais, opts).Should().Be("SatCountries");
        SatCatalogosSearchAdapter.NombreRemoto(CatalogoSat.ClavePedimento, opts).Should().Be("SatClavePedimento");
    }

    [Fact]
    public async Task FraccionArancelaria_con_texto_corto_devuelve_vacio_sin_llamar_al_pac()
    {
        // Las fracciones son de 8-10 dígitos: 1–3 caracteres no matchean por
        // lookup exacto, igual que c_ClaveProdServ.
        var (handler, puerto, _) = Build();

        var items = await handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.FraccionArancelaria, "700"), CancellationToken.None);

        items.Should().BeEmpty();
        puerto.Busquedas.Should().BeEmpty();
        puerto.LookupsPorCodigo.Should().BeEmpty();
    }

    [Fact]
    public async Task UnidadAduana_con_texto_corto_hace_lookup_exacto()
    {
        // c_UnidadAduana tiene códigos cortos (06 = kg) → conserva el lookup
        // exacto, igual que c_ClaveUnidad.
        var (handler, puerto, _) = Build();
        puerto.ResultadoPorCodigo = new CatalogoSatItem("06", "Kilogramo");

        var items = await handler.Handle(
            new BuscarCatalogoSatQuery(CatalogoSat.UnidadAduana, "06"), CancellationToken.None);

        items.Should().ContainSingle(i => i.Codigo == "06");
        puerto.LookupsPorCodigo.Should().ContainSingle().Which.Codigo.Should().Be("06");
    }

    private static (BuscarCatalogoSatHandler Handler, FakePuerto Puerto, IMemoryCache Cache) Build(
        Guid? empresaId)
    {
        var puerto = new FakePuerto();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new BuscarCatalogoSatHandler(
            puerto, new InMemoryFiscalDb.FakeEmpresaContext(empresaId), cache);
        return (handler, puerto, cache);
    }

    private static (BuscarCatalogoSatHandler Handler, FakePuerto Puerto, IMemoryCache Cache) Build()
        => Build(EmpresaId);

    private sealed class FakePuerto : ISatCatalogosSearchPort
    {
        public IReadOnlyList<CatalogoSatItem> ResultadosBusqueda { get; set; } = [];
        public CatalogoSatItem? ResultadoPorCodigo { get; set; }
        public bool Explota { get; set; }

        public List<(CatalogoSat Catalogo, string SearchText, int PageSize)> Busquedas { get; } = new();
        public List<(CatalogoSat Catalogo, string Codigo)> LookupsPorCodigo { get; } = new();

        public Task<IReadOnlyList<CatalogoSatItem>> BuscarAsync(
            Guid empresaId, CatalogoSat catalogo, string searchText, int pageSize,
            CancellationToken cancellationToken)
        {
            if (Explota) throw new CatalogoSatNoDisponibleException("fake");
            Busquedas.Add((catalogo, searchText, pageSize));
            return Task.FromResult(ResultadosBusqueda);
        }

        public Task<CatalogoSatItem?> ObtenerPorCodigoAsync(
            Guid empresaId, CatalogoSat catalogo, string codigo,
            CancellationToken cancellationToken)
        {
            if (Explota) throw new CatalogoSatNoDisponibleException("fake");
            LookupsPorCodigo.Add((catalogo, codigo));
            return Task.FromResult(ResultadoPorCodigo);
        }
    }
}
