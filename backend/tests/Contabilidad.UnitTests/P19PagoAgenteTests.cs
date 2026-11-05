using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
namespace Millet.Contabilidad.UnitTests;
public sealed class P19PagoAgenteTests
{
    [Fact]
    public void Pago_del_anticipo_usa_plantilla_propia_sin_exigir_factura_ficticia()
    {
        var id = Guid.NewGuid(); var sucursal = Guid.NewGuid();
        var p = JsonSerializer.Serialize(new { EmpresaId = Guid.NewGuid(), PagoId = id, FacturaProveedorId = Guid.NewGuid(),
            Monto = 50000, FechaPago = new DateOnly(2026, 10, 1), ReferenciaBancaria = "BANCO-REAL", Moneda = "MXN",
            EsAnticipoAgente = true, SucursalId = sucursal, ProveedorId = Guid.NewGuid() });
        var e = NormalizadorEventos.Normalizar(NormalizadorEventos.PagoProveedorAplicado, Guid.NewGuid(), p);
        Assert.Equal(NormalizadorEventos.PagoAnticipoAgente, e.Tipo); Assert.Equal(50000, e.Importes["Total"]);
        Assert.Equal(sucursal, e.SucursalId); Assert.Null(e.FacturaOrigenId);
        Assert.Equal("BANCO-REAL", e.Documento.Folio);
        Assert.Equal(e.Llave, NormalizadorEventos.Normalizar(NormalizadorEventos.PagoProveedorAplicado, Guid.NewGuid(), p).Llave);
    }
    [Fact]
    public void Devolucion_usa_fecha_bancaria_importe_confirmado_y_clave_unica()
    {
        var p = JsonSerializer.Serialize(new { EmpresaId = Guid.NewGuid(), DevolucionId = Guid.NewGuid(), Monto = 3000,
            FechaValor = new DateOnly(2026, 10, 3), ReferenciaBancaria = "DEP-REAL", SucursalId = Guid.NewGuid(), ProveedorId = Guid.NewGuid() });
        var e = NormalizadorEventos.Normalizar(NormalizadorEventos.DevolucionAnticipoAgente, Guid.NewGuid(), p);
        Assert.Equal(new DateOnly(2026, 10, 3), e.FechaContable); Assert.Equal(3000, e.Importes["Total"]);
        Assert.Equal(e.Llave, NormalizadorEventos.Normalizar(NormalizadorEventos.DevolucionAnticipoAgente, Guid.NewGuid(), p).Llave);
    }
}
