using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// M-169 (Contabilidad, 10-nov): la póliza del costo de importación carga el gasto por grupo contable (honorarios aduanales PF/PM,
/// fletes y maniobras, almacenajes, otros). Un evento sin desglose (anterior a M-169) sigue cargando todo a GASTO_IMPORTACION.
/// Los campos de grupo sin importe valen 0 y el motor no hace partida con importe 0.
/// </summary>
public sealed class M169GastoImportacionPorGrupoTests
{
    private static EventoContable Costo(decimal gasto, object? grupos) => N.Normalizar(N.CostoImportacionAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new
    {
        EmpresaId = Guid.NewGuid(), OcurridoEn = new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero), ExpedienteId = Guid.NewGuid(),
        SucursalId = Guid.NewGuid(), NumeroVersion = 1,
        Partidas = new[] { new { PartidaId = Guid.NewGuid(), ArticuloId = Guid.NewGuid(), DiferenciaMxn = gasto, InventarioMxn = 0m, GastoImportacionMxn = gasto } },
        GastosPorGrupo = grupos,
    }));

    private static decimal Importe(EventoContable e, string campo) =>
        GenerarPolizaDesdeEventoHandler.Importes(new LineaPlantillaContable { Rol = "X", Lado = LadoPartida.Cargo, Campo = campo }, e).Single().Importe;

    [Fact]
    public void Honorarios_y_flete_van_cada_uno_a_su_campo_y_el_gasto_sin_grupo_queda_en_cero()
    {
        var e = Costo(20000, new[] { new { Grupo = "HONORARIOS_PM", ImporteMxn = 12000m }, new { Grupo = "FLETES_MANIOBRAS", ImporteMxn = 8000m } });

        Importe(e, "GastoHonorariosPm").Should().Be(12000);
        Importe(e, "GastoFletesManiobras").Should().Be(8000);
        Importe(e, "Total").Should().Be(20000);
        foreach (var cero in new[] { "GastoImportacion", "GastoHonorariosPf", "GastoAlmacenajes", "GastoOtros", "Inventario" })
            Importe(e, cero).Should().Be(0, cero + " no genera partida");
    }

    [Fact]
    public void Honorarios_de_persona_fisica_y_moral_van_a_campos_distintos()
    {
        var e = Costo(5000, new[] { new { Grupo = "HONORARIOS_PF", ImporteMxn = 2000m }, new { Grupo = "HONORARIOS_PM", ImporteMxn = 3000m } });

        (Importe(e, "GastoHonorariosPf"), Importe(e, "GastoHonorariosPm")).Should().Be((2000m, 3000m));
    }

    [Fact]
    public void Evento_viejo_sin_desglose_carga_todo_a_gasto_de_importacion()
    {
        var e = Costo(20000, null);

        Importe(e, "GastoImportacion").Should().Be(20000);
        foreach (var campo in N.CamposGastoImportacion.Values) Importe(e, campo).Should().Be(0);
    }

    [Fact]
    public void Desglose_que_no_cuadra_o_grupo_desconocido_queda_en_gasto_de_importacion()
    {
        var noCuadra = Costo(20000, new[] { new { Grupo = "HONORARIOS_PM", ImporteMxn = 12000m } });
        Importe(noCuadra, "GastoImportacion").Should().Be(20000);
        Importe(noCuadra, "GastoHonorariosPm").Should().Be(0);

        var desconocido = Costo(20000, new[] { new { Grupo = "HONORARIOS_PM", ImporteMxn = 12000m }, new { Grupo = "SEGUROS", ImporteMxn = 8000m } });
        Importe(desconocido, "GastoHonorariosPm").Should().Be(12000);
        Importe(desconocido, "GastoImportacion").Should().Be(8000);
    }

    [Fact]
    public void Reclasificacion_entre_grupos_carga_uno_y_abona_el_otro()
    {
        var e = Costo(0, new[] { new { Grupo = "FLETES_MANIOBRAS", ImporteMxn = -3000m }, new { Grupo = "OTROS", ImporteMxn = 3000m } });

        (Importe(e, "GastoFletesManiobras"), Importe(e, "GastoOtros"), Importe(e, "Total")).Should().Be((-3000m, 3000m, 0m));
    }
}
