using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Infrastructure.CostoPromedio;
using Millet.Integraciones.Aw.UnitTests.Application;

namespace Millet.Integraciones.Aw.UnitTests.CostoPromedio;

/// <summary>
/// M-447 (contrato del script 19 enviado a TI): una fila por llave artículo A+W + sucursal + tipo + línea + medida, con la medida
/// sin orientación (ancho = lado menor) y todos los lotes/variantes de la llave en un solo promedio ponderado por existencia.
/// </summary>
public sealed class CostoPromedioLectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static SaldoParaCosto S(decimal cant, decimal costo, int ancho = 1800, int alto = 2600, int min = 0,
        string tipo = "OBRAS", string linea = "HEGLA 1") =>
        new(4711, "CONKAL", tipo, linea, ancho, alto, cant, costo, T0.AddMinutes(min));

    [Fact]
    public void Agrupar_PonderaLosLotesDeUnaLlave()
    {
        var f = CostoPromedioLector.Agrupar([S(10, 100), S(30, 200)]).Should().ContainSingle().Subject;
        (f.CostoM2, f.CostoPieza, f.AlmacenTipo, f.Linea).Should().Be((175m, (decimal?)null, "OBRAS", "HEGLA 1"));
    }

    [Fact]
    public void Agrupar_ConExistenciaCero_ConservaElUltimoPromedio()
    {
        CostoPromedioLector.Agrupar([S(0, 100, min: 1), S(0, 250, min: 5)])
            .Should().ContainSingle().Which.CostoM2.Should().Be(250m);
    }

    [Fact]
    public void Agrupar_MedidaEnCualquierOrientacion_EsLaMismaLlave_AnchoMenorAltoMayor()
    {
        var f = CostoPromedioLector.Agrupar([S(10, 100, ancho: 2600, alto: 1800), S(30, 200, ancho: 1800, alto: 2600)])
            .Should().ContainSingle().Subject;
        (f.AnchoMm, f.AltoMm, f.CostoM2).Should().Be(((int?)1800, (int?)2600, (decimal?)175m));
    }

    [Fact]
    public void Agrupar_SinMedida_UsaCostoPieza()
    {
        var f = CostoPromedioLector.Agrupar([new SaldoParaCosto(2002, "CIRCUITO", null, null, null, null, 4, 5m, T0)])
            .Should().ContainSingle().Subject;
        (f.AnchoMm, f.AltoMm, f.CostoM2, f.CostoPieza).Should().Be(((int?)null, (int?)null, (decimal?)null, (decimal?)5m));
    }

    [Fact]
    public void ConFilaDeSucursal_SinLista_SoloFilaDeSucursalConTipoYLineaNull()
    {
        var f = CostoPromedioLector.Agrupar(CostoPromedioLector.ConFilaDeSucursal(S(10, 100, tipo: "X", linea: "Y"), null, null))
            .Should().ContainSingle().Subject;
        (f.AlmacenTipo, f.Linea, f.CostoM2).Should().Be(((string?)null, (string?)null, (decimal?)100m));
    }

    [Fact]
    public void ConFilaDeSucursal_Herraje_LineaVaciaViajaNull_NuncaCadenaVacia()
    {
        var filas = CostoPromedioLector.Agrupar(CostoPromedioLector.ConFilaDeSucursal(S(10, 100), "GENERAL", ""));
        filas.Should().HaveCount(2).And.OnlyContain(f => f.Linea == null && f.AlmacenTipo != "");
        filas.Should().ContainSingle(f => f.AlmacenTipo == "GENERAL");
    }

    [Fact]
    public void ConLista_PublicaCadaAlmacenYUnaFilaDeSucursalPonderadaPorExistencia()
    {
        // Misma hoja en dos almacenes de CONKAL: HEGLA 1 (10 m² a 100) y HEGLA 2 (30 m² a 200, girada).
        var filas = CostoPromedioLector.Agrupar(
            CostoPromedioLector.ConFilaDeSucursal(S(10, 100), "OBRAS", "HEGLA 1")
                .Concat(CostoPromedioLector.ConFilaDeSucursal(S(30, 200, ancho: 2600, alto: 1800), "OBRAS", "HEGLA 2")));

        filas.Select(f => (f.AlmacenTipo, f.Linea, f.CostoM2)).Should().BeEquivalentTo(new (string?, string?, decimal?)[]
        {
            ("OBRAS", "HEGLA 1", 100m), ("OBRAS", "HEGLA 2", 200m), (null, null, 175m),
        });
        filas.Should().OnlyContain(f => f.AnchoMm == 1800 && f.AltoMm == 2600);
    }

    [Fact]
    public void FilaDeSucursal_ConExistenciaCero_ConservaElUltimoPromedioDeSusAlmacenes()
    {
        var f = CostoPromedioLector.Agrupar(
                CostoPromedioLector.ConFilaDeSucursal(S(0, 100, min: 1), "OBRAS", "HEGLA 1")
                    .Concat(CostoPromedioLector.ConFilaDeSucursal(S(0, 250, min: 5), "OBRAS", "HEGLA 2")))
            .Single(x => x.AlmacenTipo is null);
        f.CostoM2.Should().Be(250m);
    }

    [Fact]
    public void Agrupar_LlavesDistintas_NoSeMezclan()
    {
        CostoPromedioLector.Agrupar([S(10, 100), S(10, 300, linea: "HEGLA 2"), S(10, 50, ancho: 1200)])
            .Should().HaveCount(3);
    }

    [Fact]
    public async Task DosVariantesDelErpConLaMismaLlave_PublicanUnSoloCostoPonderado_YNoVersionanCadaCiclo()
    {
        // Dos variantes del ERP (misma hoja en distinta orientación, distinto costo) caen en la misma llave del contrato.
        SaldoParaCosto[] ciclo = [S(10, 100, ancho: 2600, alto: 1800), S(30, 200)];
        var tr = new SimuladoCostoPromedioTransporte();
        var pub = new CostoPromedioPublicador(await InMemoryDb.CreateAsync(), tr, TimeProvider.System);

        for (var i = 0; i < 3; i++)
            await pub.PublicarAsync(CostoPromedioLector.Agrupar(ciclo), default);

        var e = tr.Filas.Should().ContainSingle().Subject;
        (e.Version, e.Fila.CostoM2, e.Fila.AnchoMm, e.Fila.AltoMm).Should().Be((1, (decimal?)175m, (int?)1800, (int?)2600));
    }

    [Fact]
    public void CabeEnContrato_RespetaLosLargosDelScript19()
    {
        var ok = new CostoPromedioFila(1, new string('S', 30), new string('T', 50), new string('L', 50), null, null, null, 1m);
        CostoPromedioLector.CabeEnContrato(ok).Should().BeTrue();
        CostoPromedioLector.CabeEnContrato(ok with { Sucursal = new string('S', 31) }).Should().BeFalse();
        CostoPromedioLector.CabeEnContrato(ok with { AlmacenTipo = new string('T', 51) }).Should().BeFalse();
        CostoPromedioLector.CabeEnContrato(ok with { Linea = new string('L', 51) }).Should().BeFalse();
        CostoPromedioLector.CabeEnContrato(ok with { AlmacenTipo = null, Linea = null }).Should().BeTrue();
    }

    [Fact]
    public void Registro_PorOmisionEsSimuladoYApagado_YTransporteInvalidoFalla()
    {
        var sp = new ServiceCollection().AddCostoPromedio(new ConfigurationBuilder().Build()).BuildServiceProvider();
        sp.GetRequiredService<ICostoPromedioTransporte>().Should().BeOfType<SimuladoCostoPromedioTransporte>();
        new CostoPromedioOptions().Habilitado.Should().BeFalse();

        var malo = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:CostoPromedio:Transporte"] = "Http" }).Build();
        FluentActions.Invoking(() => new ServiceCollection().AddCostoPromedio(malo)).Should().Throw<InvalidOperationException>();
    }
}
