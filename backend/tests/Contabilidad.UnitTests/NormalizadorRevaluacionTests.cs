using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// CXP-12 · lectura de los eventos de la revaluación de cierre: la revaluación se contabiliza el último día del mes y su reversa el
/// día 1 (fecha del propio evento, no la de ocurrencia), por proveedor y siempre en pesos. Payload serializado como el outbox.
/// </summary>
public class NormalizadorRevaluacionTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Proveedor = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Revaluacion = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 2, 1, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void La_revaluacion_se_contabiliza_el_ultimo_dia_del_mes_con_la_perdida_por_proveedor()
    {
        var e = N.Normalizar(N.RevaluacionCierreRegistrada, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, RevaluacionId = Revaluacion, ProveedorId = Proveedor, SucursalId = Sucursal,
            FechaCierre = new DateOnly(2026, 1, 31), Perdida = 500m, Utilidad = 0m,
        }));

        e.FechaContable.Should().Be(new DateOnly(2026, 1, 31));
        e.ProveedorId.Should().Be(Proveedor);
        e.SucursalId.Should().Be(Sucursal);
        e.Moneda.Should().Be("MXN");
        e.Importes["Perdida"].Should().Be(500m);
        e.Importes["Utilidad"].Should().Be(0m);
        e.Documento.Should().Match<Millet.Contabilidad.Domain.DocumentoOrigenPoliza>(d =>
            d.Modulo == "cuentas_por_pagar" && d.Tipo == "revaluacion_cierre" && d.Id == Revaluacion);
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void La_reversa_se_contabiliza_el_dia_1_con_su_propio_tipo_de_documento_y_llave()
    {
        var id = Guid.NewGuid();
        var e = N.Normalizar(N.RevaluacionCierreRevertida, id, JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, RevaluacionId = Revaluacion, ProveedorId = Proveedor, SucursalId = Sucursal,
            FechaReversa = new DateOnly(2026, 2, 1), Perdida = 0m, Utilidad = 200m,
        }));

        e.FechaContable.Should().Be(new DateOnly(2026, 2, 1));
        e.Importes["Utilidad"].Should().Be(200m);
        e.Documento.Tipo.Should().Be("revaluacion_cierre_reversa");
        e.Llave.Should().Be($"{N.RevaluacionCierreRevertida}:{id}");
    }

    [Fact]
    public void Un_evento_sin_fecha_es_ilegible_y_no_se_inventa_una()
    {
        var sinFecha = () => N.Normalizar(N.RevaluacionCierreRegistrada, Guid.NewGuid(), JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = Ocurrido, RevaluacionId = Revaluacion, ProveedorId = Proveedor, SucursalId = Sucursal, Perdida = 1m, Utilidad = 0m,
        }));

        sinFecha.Should().Throw<EventoIlegibleException>();
    }
}
