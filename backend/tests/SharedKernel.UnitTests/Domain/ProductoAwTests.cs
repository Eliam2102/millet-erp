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

    // ── Variantes y baja (ADM-07) ───────────────────────────────────────

    private static ProductoAwVarianteDato V(string clave, decimal? alto = null, decimal? ancho = null,
        decimal? espesor = null, string? comp = null) => new(clave, alto, ancho, espesor, comp);

    [Fact]
    public void Should_AplicarVariantes_DosMedidasMismoCodigo()
    {
        var p = Crear();
        p.AplicarVariantes([V("A", 1000, 500, 6), V("B", 2000, 1000, 6, "6mm+PVB+6mm")]);
        Assert.Equal(2, p.Variantes.Count);
        Assert.All(p.Variantes, v => Assert.Equal(p.Id, v.ProductoAwId));
    }

    [Fact]
    public void Should_Upsert_ActualizaExistenteSinDuplicar_YConservaLasNoIncluidas()
    {
        var p = Crear();
        p.AplicarVariantes([V("A", 1000, 500, 6), V("B", 2000, 1000, 6)]);
        var idA = p.Variantes.Single(v => v.ClaveVariante == "A").Id;

        p.AplicarVariantes([V("A", 1100, 500, 8)]);

        Assert.Equal(2, p.Variantes.Count);
        var a = p.Variantes.Single(v => v.ClaveVariante == "A");
        Assert.Equal(idA, a.Id);
        Assert.Equal(1100, a.AltoMm);
    }

    [Fact]
    public void Should_Rechazar_ClaveVarianteDuplicadaEnLista_SinMutar()
    {
        var p = Crear();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            p.AplicarVariantes([V("A", 1), V("A", 2)]));
        Assert.Equal("PRODUCTO_AW_VARIANTE_DUPLICADA", ex.Code);
        Assert.Empty(p.Variantes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Should_Rechazar_MedidaCeroONegativa(double medida)
    {
        var p = Crear();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            p.AplicarVariantes([V("A", alto: (decimal)medida)]));
        Assert.Equal("PRODUCTO_AW_VARIANTE_MEDIDA_INVALIDA", ex.Code);
        Assert.Empty(p.Variantes);
    }

    [Fact]
    public void Should_ConservarNulo_ComoNoInformado_NoCero()
    {
        var p = Crear();
        p.AplicarVariantes([V("A", alto: null, ancho: 500)]);
        var v = p.Variantes.Single();
        Assert.Null(v.AltoMm);
        Assert.Equal(500, v.AnchoMm);
        Assert.Null(v.EspesorMm);
    }

    [Fact]
    public void Should_Rechazar_ClaveVacia_YComposicionLarga()
    {
        var p = Crear();
        Assert.Equal("PRODUCTO_AW_VARIANTE_CLAVE_INVALIDA",
            Assert.Throws<BusinessRuleException>(() => p.AplicarVariantes([V(" ")])).Code);
        Assert.Equal("PRODUCTO_AW_VARIANTE_COMPOSICION_INVALIDA",
            Assert.Throws<BusinessRuleException>(() => p.AplicarVariantes([V("A", comp: new string('x', 201))])).Code);
    }

    [Fact]
    public void Should_DarDeBaja_Idempotente_ConservaDatosYFechaOriginal()
    {
        var p = Crear(claveProdServSat: "43211701", claveUnidadSat: "MTK");
        p.AplicarVariantes([V("A", 1000)]);
        var f1 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        p.DarDeBaja(f1);
        p.DarDeBaja(f1.AddDays(5));

        Assert.Equal(EstatusCatalogo.Inactivo, p.Estatus);
        Assert.Equal(f1, p.FechaBaja);
        Assert.Equal("43211701", p.ClaveProdServSat);
        Assert.Single(p.Variantes);
    }

    [Fact]
    public void Should_Reactivar_LimpiaFechaBaja()
    {
        var p = Crear();
        p.DarDeBaja(DateTime.UtcNow);
        p.Reactivar();
        Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
        Assert.Null(p.FechaBaja);
    }

    // ── Composición y clasificación ─────────────────────────────────────

    private static ProductoAwComponenteDato C(int orden, int nivel, int? padre, string r = "DEMO-C") =>
        new(orden, nivel, padre, r, "DEMO", "Vidrio plano", null);

    [Fact]
    public void Should_ReemplazarComponentes_ActualizaPorOrden_AgregaYQuitaLosQueNoVienen()
    {
        var p = Crear();
        p.ReemplazarComponentes([C(1, 1, null, "A"), C(2, 2, 1, "B"), C(3, 1, null, "C")]);
        var id2 = p.Componentes.Single(c => c.Orden == 2).Id;

        p.ReemplazarComponentes([C(1, 1, null, "A2"), C(2, 2, 1, "B")]);

        Assert.Equal(2, p.Componentes.Count);
        Assert.Equal("A2", p.Componentes.Single(c => c.Orden == 1).ComponenteRef);
        Assert.Equal(id2, p.Componentes.Single(c => c.Orden == 2).Id);
        Assert.All(p.Componentes, c => Assert.Equal(p.Id, c.ProductoAwId));
        p.ReemplazarComponentes([]);
        Assert.Empty(p.Componentes);
    }

    [Theory]
    [InlineData(1, 1, 1)]   // padre = sí mismo
    [InlineData(2, 2, 3)]   // padre posterior
    [InlineData(1, 0, null)] // nivel 0
    [InlineData(0, 1, null)] // orden 0
    public void Should_RechazarComponente_ConPosicionInvalida_SinMutar(int orden, int nivel, int? padre)
    {
        var p = Crear();
        p.ReemplazarComponentes([C(1, 1, null, "A")]);
        Assert.Throws<BusinessRuleException>(() => p.ReemplazarComponentes([C(orden, nivel, padre)]));
        Assert.Equal("A", Assert.Single(p.Componentes).ComponenteRef);
    }

    [Fact]
    public void Should_RechazarComponentes_ConOrdenDuplicado()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear().ReemplazarComponentes([C(1, 1, null), C(1, 1, null)]));
        Assert.Equal("PRODUCTO_AW_COMPONENTE_ORDEN_DUPLICADO", ex.Code);
    }

    [Fact]
    public void Should_AplicarClasificacion_YRechazarExcesoDeLongitud()
    {
        var p = Crear();
        p.AplicarClasificacion("VT6", "Vidrio templado claro", "VTE", "370", "VIDRIO TEMPLADO CONTROL SOLAR");
        Assert.Equal(("VT6", "Vidrio templado claro", "VTE"), (p.CodigoModelo, p.Grupo, p.Tipo));
        Assert.Equal(("370", "VIDRIO TEMPLADO CONTROL SOLAR"), (p.Wgr, p.WgrDescripcion));
        Assert.Throws<BusinessRuleException>(() => p.AplicarClasificacion(null, null, null, new string('x', 11)));
        Assert.Throws<BusinessRuleException>(() => p.AplicarClasificacion(new string('x', 51), null, null));
    }
}
