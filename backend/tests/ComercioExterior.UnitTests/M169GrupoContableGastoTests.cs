using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;

/// <summary>
/// M-169 (Contabilidad, 10-nov): el gasto de importación se carga por grupo contable del concepto (honorarios aduanales separados
/// en persona física y moral por el proveedor; fletes y maniobras; almacenajes; otros). El grupo es configurable por concepto y el
/// ajuste por grupo de cada versión suma lo mismo que el ajuste por partida.
/// </summary>
public sealed class M169GrupoContableGastoTests
{
    private static readonly Guid AgenteFisica = Guid.NewGuid(), AgenteMoral = Guid.NewGuid(), Transportista = Guid.NewGuid();

    private static Expediente Importacion() => new(Guid.NewGuid(), Guid.NewGuid(), "IMP-M169", TipoExpediente.Importacion, Guid.NewGuid(),
        "Gasto por concepto", Guid.NewGuid(), Guid.NewGuid(), "USD", Guid.NewGuid(), []);

    private static BasePartidaCosto[] Partidas() =>
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), null, "Vidrio claro 6 mm", 200000, 2000, 1000, Existencia: 1000, ExistenciaTotal: 1000),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), null, "Vidrio filtrasol 6 mm", 300000, 6000, 1000, Existencia: 1000, ExistenciaTotal: 1000),
    ];

    private static void Gasto(Expediente e, ConceptoGasto concepto, Guid proveedor, decimal importe, Guid? id = null) =>
        e.GuardarGasto(id ?? Guid.NewGuid(), new(concepto.Id, proveedor, "FAC-" + Random.Shared.Next(1000, 9999), "MXN", importe, new(2026, 10, 20)),
            concepto, 1, "MXN");

    private static ConceptoGasto[] Catalogo(Guid empresa) => ConceptoGasto.CrearCatalogo(empresa)
        .Select(c => { c.Configurar(c.BaseImportacion, c.BaseExportacion, requiereOrdenServicio: false); return c; }).ToArray();

    [Fact]
    public void Grupo_por_omision_del_catalogo_de_conceptos()
    {
        var grupos = ConceptoGasto.CrearCatalogo(Guid.NewGuid()).ToDictionary(c => c.Codigo, c => c.GrupoContable);

        grupos["HONORARIOS"].Should().Be(GrupoContableGasto.HonorariosAduanales);
        grupos["FLETE_INT"].Should().Be(GrupoContableGasto.FletesManiobras);
        grupos["FLETE_NAC"].Should().Be(GrupoContableGasto.FletesManiobras);
        grupos["MANIOBRAS"].Should().Be(GrupoContableGasto.FletesManiobras);
        grupos["ALMACENAJE"].Should().Be(GrupoContableGasto.Almacenajes);
        foreach (var otro in new[] { "SEGURO", "ADUANALES", "TERCEROS", "IGI", "DTA", "PRV", "IVA", "OTROS" })
            grupos[otro].Should().Be(GrupoContableGasto.OtrosGastosImportacion, otro);
    }

    [Fact]
    public void Honorarios_12000_y_flete_8000_van_a_su_grupo_y_suman_el_ajuste()
    {
        var e = Importacion(); var conceptos = Catalogo(e.EmpresaId);
        Gasto(e, conceptos.Single(c => c.Codigo == "HONORARIOS"), AgenteMoral, 12000);
        Gasto(e, conceptos.Single(c => c.Codigo == "FLETE_INT"), Transportista, 8000);
        Gasto(e, conceptos.Single(c => c.Codigo == "IVA"), AgenteMoral, 1600);

        var d = e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, incrementablesAlInventario: false,
            proveedoresPersonaFisica: new HashSet<Guid> { AgenteFisica });

        d.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoDistribuido(GrupoGastoImportacion.FletesManiobras, 8000),
            new GastoGrupoDistribuido(GrupoGastoImportacion.HonorariosPersonaMoral, 12000),
        });
        d.GastosPorGrupo.Sum(g => g.ImporteMxn).Should().Be(d.Partidas.Sum(p => p.GastoImportacionMxn)).And.Be(20000, "el IVA no entra al costo");
        d.Partidas.Should().OnlyContain(p => p.InventarioMxn == 0);
        d.Renglones.Should().OnlyContain(r => r.Grupo != null);
    }

    [Fact]
    public void Honorarios_de_persona_fisica_y_moral_se_separan_por_el_proveedor()
    {
        var e = Importacion(); var conceptos = Catalogo(e.EmpresaId); var honorarios = conceptos.Single(c => c.Codigo == "HONORARIOS");
        Gasto(e, honorarios, AgenteFisica, 5000);
        Gasto(e, honorarios, AgenteMoral, 7000);

        var d = e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false, new HashSet<Guid> { AgenteFisica });

        d.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoDistribuido(GrupoGastoImportacion.HonorariosPersonaFisica, 5000),
            new GastoGrupoDistribuido(GrupoGastoImportacion.HonorariosPersonaMoral, 7000),
        });
    }

    [Fact]
    public void Sin_tipo_de_persona_conocido_los_honorarios_van_a_persona_moral()
    {
        var e = Importacion(); var conceptos = Catalogo(e.EmpresaId);
        Gasto(e, conceptos.Single(c => c.Codigo == "HONORARIOS"), AgenteFisica, 5000);

        e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false).GastosPorGrupo
            .Should().ContainSingle().Which.Should().Be(new GastoGrupoDistribuido(GrupoGastoImportacion.HonorariosPersonaMoral, 5000));
    }

    [Fact]
    public void La_segunda_version_manda_solo_el_ajuste_de_cada_grupo()
    {
        var e = Importacion(); var conceptos = Catalogo(e.EmpresaId); var flete = Guid.NewGuid();
        Gasto(e, conceptos.Single(c => c.Codigo == "HONORARIOS"), AgenteMoral, 12000);
        Gasto(e, conceptos.Single(c => c.Codigo == "FLETE_INT"), Transportista, 8000, flete);
        e.ConfirmarDistribucion(e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false));
        Gasto(e, conceptos.Single(c => c.Codigo == "FLETE_INT"), Transportista, 9500, flete);
        Gasto(e, conceptos.Single(c => c.Codigo == "ALMACENAJE"), Transportista, 700);

        var dos = e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false);

        dos.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoDistribuido(GrupoGastoImportacion.Almacenajes, 700),
            new GastoGrupoDistribuido(GrupoGastoImportacion.FletesManiobras, 1500),
        });
        dos.GastosPorGrupo.Sum(g => g.ImporteMxn).Should().Be(dos.Partidas.Sum(p => p.DiferenciaMxn));
    }

    [Fact]
    public void Contabilidad_mueve_el_concepto_de_grupo_y_la_nueva_version_reclasifica()
    {
        var e = Importacion(); var conceptos = Catalogo(e.EmpresaId); var maniobras = conceptos.Single(c => c.Codigo == "MANIOBRAS");
        Gasto(e, maniobras, Transportista, 3000);
        e.ConfirmarDistribucion(e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false));
        maniobras.Configurar(maniobras.BaseImportacion, maniobras.BaseExportacion, grupoContable: GrupoContableGasto.OtrosGastosImportacion);
        e.MarcarCambioFinanciero(true);

        var dos = e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow, false);

        dos.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoDistribuido(GrupoGastoImportacion.FletesManiobras, -3000),
            new GastoGrupoDistribuido(GrupoGastoImportacion.Otros, 3000),
        });
        dos.Partidas.Sum(p => p.DiferenciaMxn).Should().Be(0);
    }

    [Fact]
    public void Exportacion_no_lleva_desglose_y_el_grupo_invalido_se_rechaza()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "EXP-M169", TipoExpediente.Exportacion, Guid.NewGuid(), "Exportación",
            null, Guid.NewGuid(), "USD", Guid.NewGuid(), ["Cliente"]);
        var conceptos = Catalogo(e.EmpresaId);
        Gasto(e, conceptos.Single(c => c.Codigo == "FLETE_INT"), Transportista, 8000);
        e.PrepararDistribucion(conceptos, Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow).GastosPorGrupo.Should().BeEmpty();

        var act = () => conceptos[0].Configurar(BaseDistribucion.ValorAduana, BaseDistribucion.Peso, grupoContable: (GrupoContableGasto)9);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CE_GRUPO_CONTABLE_INVALIDO");
    }
}
