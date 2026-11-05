using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// F1-ALM-13 (Contabilidad, Fernando Alejos 4-nov): cuenta de inventario por tipo de artículo para toda la empresa. El grupo del
/// artículo llega como clasificador de la línea y el motor (y el cuadre de Almacén) usan INVENTARIO_{grupo} con cuenta o, si no,
/// INVENTARIO. Datos FIX.
/// </summary>
public class InventarioPorGrupoTests
{
    private static readonly Guid Sucursal = Guid.NewGuid(), Otra = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 11, 30);

    [Theory]
    [InlineData("VIDRIO FLOTADO", "VIDRIO_FLOTADO")]
    [InlineData("PINT.ELEC.QUÍM. (MP)", "PINT_ELEC_QUIM_MP")]
    [InlineData("HERRAJES IMPORTACIÓN", "HERRAJES_IMPORTACION")]
    [InlineData("  INSUMOS  PRODUCCION ", "INSUMOS_PRODUCCION")]
    [InlineData("PT", "PT")]
    public void El_grupo_SAP_se_normaliza_al_sufijo_del_rol(string grupo, string sufijo) =>
        GenerarPolizaDesdeEventoHandler.Clasificador(grupo).Should().Be(sufijo);

    [Fact]
    public void Usa_el_rol_del_grupo_con_cuenta_y_si_no_el_rol_INVENTARIO_y_si_tampoco_sin_cuenta()
    {
        Guid vidrio = Guid.NewGuid(), general = Guid.NewGuid();
        var roles = new List<RolCuenta>
        {
            RolCuenta.Crear(Guid.NewGuid(), "INVENTARIO", "Inventario", general, null, null, new(2000, 1, 1)),
            RolCuenta.Crear(Guid.NewGuid(), "INVENTARIO_VIDRIO_FLOTADO", "Vidrio", vidrio, null, null, new(2000, 1, 1)),
            RolCuenta.Crear(Guid.NewGuid(), "INVENTARIO_ACCESORIOS", "Accesorios sin cuenta", null, null, null, new(2000, 1, 1)),
        };
        Guid? Resolver(string? grupo, List<RolCuenta> rs) =>
            GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(rs, "INVENTARIO", grupo, Sucursal, "MXN", Fecha);

        Resolver("VIDRIO FLOTADO", roles).Should().Be(vidrio);
        Resolver("ACCESORIOS", roles).Should().Be(general, "el rol del grupo existe pero sin cuenta");
        Resolver("ALUMINIO", roles).Should().Be(general);
        Resolver(null, roles).Should().Be(general);
        Resolver("ALUMINIO", roles.Where(r => r.Codigo != "INVENTARIO").ToList()).Should().BeNull("sin cuenta configurada, no se adivina");
    }

    [Fact]
    public void Ajuste_AS_lleva_una_linea_con_signo_y_articulo_por_movimiento()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var e = N.Normalizar(N.AjusteInventarioAplicado, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Guid.NewGuid(), OcurridoEn = DateTimeOffset.UtcNow, ConteoId = Guid.NewGuid(), SucursalId = Sucursal,
            MovimientosGenerados = new object[]
            {
                new { Tipo = "AjusteNegativo", MontoMxn = -300m, ArticuloId = a },
                new { Tipo = "AjustePositivo", MontoMxn = 120m, ArticuloId = b },
            },
        }));

        e.Lineas.Select(l => (l.Importe, l.ArticuloId)).Should().Equal((-300m, (Guid?)a), (120m, b));
        e.Importes["Neto"].Should().Be(-180m);
    }

    [Fact]
    public async Task El_enriquecedor_pone_el_grupo_del_articulo_como_clasificador()
    {
        Guid conGrupo = Guid.NewGuid(), sinGrupo = Guid.NewGuid();
        var e = N.Normalizar(N.SalidaRequisicionRegistrada, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Guid.NewGuid(), SalidaId = Guid.NewGuid(), FolioSalida = "SAL-FIX", FechaMovimiento = Fecha, SucursalId = Otra,
            Lineas = new object[]
            {
                new { LineaSalidaId = Guid.NewGuid(), ArticuloId = conGrupo, MontoTotalMxn = 800m, CentroCostoId = Guid.NewGuid() },
                new { LineaSalidaId = Guid.NewGuid(), ArticuloId = sinGrupo, MontoTotalMxn = 200m, CentroCostoId = Guid.NewGuid() },
            },
        }));

        var r = await new EnriquecedorGrupoArticulo(new Grupos(new() { [conGrupo] = "VIDRIO FLOTADO" })).EnriquecerAsync(e, default);

        r.Lineas.Select(l => l.Clasificador).Should().Equal("VIDRIO FLOTADO", null);
    }

    private sealed class Grupos(Dictionary<Guid, string> grupos) : IArticuloContabilidadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> GruposAsync(IReadOnlyCollection<Guid> articuloIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(grupos.Where(g => articuloIds.Contains(g.Key)).ToDictionary());
    }
}
