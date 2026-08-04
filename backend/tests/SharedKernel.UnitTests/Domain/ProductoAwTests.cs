using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.UnitTests.Domain;

public class ProductoAwTests
{
    private static ProductoAw Crear(
        string referenciaExterna = "5137",
        string descripcion = "VIDRIO CLARO 6MM TEMPLADO",
        string unidadMedida = "M2",
        string? claveProdServSat = null,
        string? claveUnidadSat = null,
        string? objetoImp = null,
        decimal? tasaIvaTraslado = null) =>
        new(
            id: Guid.CreateVersion7(),
            referenciaExterna: referenciaExterna,
            descripcion: descripcion,
            unidadMedida: unidadMedida,
            claveProdServSat: claveProdServSat,
            claveUnidadSat: claveUnidadSat,
            objetoImp: objetoImp,
            tasaIvaTraslado: tasaIvaTraslado);

    [Fact]
    public void Should_Create_SinClavesSat_ConOrigenAwDefault()
    {
        // Las claves SAT no existen en A+W: la auto-provisión crea el
        // producto incompleto y el operador lo completa (bloquea timbrado,
        // no alta).
        var p = Crear();
        Assert.Equal(OrigenMaster.Aw, p.Origen);
        Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
        Assert.False(p.DatosFiscalesCompletos);
    }

    [Fact]
    public void Should_ReportarFiscalesCompletos_ConAmbasClaves()
    {
        var p = Crear(claveProdServSat: "43211701", claveUnidadSat: "MTK");
        Assert.True(p.DatosFiscalesCompletos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REF-DEMASIADO-LARGA-QUE-EXCEDE-CINCUENTA-CARACTERES-X")]
    public void Should_Throw_When_ReferenciaInvalida(string referencia)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(referenciaExterna: referencia));
        Assert.Equal("PRODUCTO_AW_REFERENCIA_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("1234567")]   // 7 dígitos
    [InlineData("123456789")] // 9 dígitos
    public void Should_Throw_When_ClaveProdServNoEs8(string clave)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(claveProdServSat: clave));
        Assert.Equal("PRODUCTO_AW_CLAVE_PRODSERV_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("09")]
    [InlineData("4")]
    [InlineData("004")]
    public void Should_Throw_When_ObjetoImpInvalido(string objetoImp)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(objetoImp: objetoImp));
        Assert.Equal("PRODUCTO_AW_OBJETO_IMP_INVALIDO", ex.Code);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("04")] // claves 04–08 vigentes en c_ObjetoImp (FAC-DET-PR1)
    [InlineData("08")]
    public void Should_Aceptar_ObjetoImp_01_a_08(string objetoImp)
    {
        var p = Crear(objetoImp: objetoImp);
        Assert.Equal(objetoImp, p.ObjetoImp);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Should_Throw_When_TasaFueraDeRango(decimal tasa)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(tasaIvaTraslado: tasa));
        Assert.Equal("PRODUCTO_AW_TASA_INVALIDA", ex.Code);
    }

    [Fact]
    public void AsignarDatosFiscales_Completa_YLimpiaTasasConFlags()
    {
        var p = Crear();
        p.AsignarDatosFiscales(
            claveProdServSat: "43211701",
            claveUnidadSat: "MTK",
            objetoImp: "02",
            tasaIvaTraslado: 0.16m);
        Assert.True(p.DatosFiscalesCompletos);
        Assert.Equal(0.16m, p.TasaIvaTraslado);

        p.AsignarDatosFiscales(limpiarTasaIvaTraslado: true);
        Assert.Null(p.TasaIvaTraslado);
        Assert.True(p.DatosFiscalesCompletos); // las claves no se limpian
    }

    [Theory]
    [InlineData("7007")]      // muy corta
    [InlineData("700711000A")] // con letra
    public void Should_Throw_When_FraccionArancelariaInvalida(string fraccion)
    {
        var p = Crear();
        var ex = Assert.Throws<BusinessRuleException>(() => p.AsignarDatosAduana(fraccionArancelaria: fraccion));
        Assert.Equal("PRODUCTO_AW_FRACCION_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_PesoNegativo()
    {
        var p = Crear();
        var ex = Assert.Throws<BusinessRuleException>(() => p.AsignarDatosAduana(pesoUnitarioKg: -1m));
        Assert.Equal("PRODUCTO_AW_PESO_INVALIDO", ex.Code);
    }

    [Fact]
    public void AsignarDatosAduana_Fija_YLimpiaConFlags()
    {
        var p = Crear();
        p.AsignarDatosAduana(fraccionArancelaria: "7007110099", unidadAduana: "06", pesoUnitarioKg: 12.5m);
        Assert.Equal("7007110099", p.FraccionArancelaria);
        Assert.Equal("06", p.UnidadAduana);
        Assert.Equal(12.5m, p.PesoUnitarioKg);

        p.AsignarDatosAduana(limpiarFraccionArancelaria: true, limpiarPesoUnitarioKg: true);
        Assert.Null(p.FraccionArancelaria);
        Assert.Null(p.PesoUnitarioKg);
        Assert.Equal("06", p.UnidadAduana); // no tocada
    }

    [Fact]
    public void AsignarUnidadMedida_SincronizaSnapshot()
    {
        var p = Crear(unidadMedida: "M2");
        var umId = Guid.CreateVersion7();
        p.AsignarUnidadMedida(umId, "PZA");
        Assert.Equal(umId, p.UnidadMedidaId);
        Assert.Equal("PZA", p.UnidadMedida);
    }
}
