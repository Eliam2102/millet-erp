using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Compras.Application.PublicPorts;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;

public sealed class OrdenesServicioTests
{
    private static Expediente Nuevo() => new(Guid.NewGuid(), Guid.NewGuid(), "CE-P6", TipoExpediente.Importacion,
        Guid.NewGuid(), "P6", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
    [Theory]
    [InlineData("ADUANALES", true)]
    [InlineData("HONORARIOS", true)]
    [InlineData("FLETE_INT", false)]
    public void Obligatoriedad_por_concepto(string codigo, bool requerida)
    {
        var e = Nuevo(); var c = ConceptoGasto.CrearCatalogo(e.EmpresaId).Single(c => c.Codigo == codigo);
        Assert.Equal(requerida, c.RequiereOrdenServicio);
        void Guardar() => e.GuardarGasto(Guid.NewGuid(), new(c.Id, Guid.NewGuid(), "F1", "MXN", 100, new(2026, 10, 1)), c, 1, "MXN");
        if (requerida) Assert.Equal("CE_ORDEN_SERVICIO_REQUERIDA", Assert.Throws<BusinessRuleException>(Guardar).Code);
        else { Guardar(); Assert.Single(e.Gastos); }
        c.Configurar(c.BaseImportacion, c.BaseExportacion, false); Guardar();
    }
    [Fact]
    public void Dos_facturas_suman_52000_y_exceden_2000_sin_duplicar_asignacion()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "HONORARIOS", "Honorarios");
        var oc = new OrdenServicioCopia(Guid.NewGuid(), "OC-P6", 50000, "MXN", 50000, 1);
        foreach (var importe in new[] { 30000m, 22000m })
            e.GuardarGasto(Guid.NewGuid(), new(c.Id, Guid.NewGuid(), "F" + importe, "MXN", importe, new(2026, 10, 1), Cotizado: 1, OrdenServicioId: oc.Id), c, 1, "MXN", oc);
        var detalle = new FinanzasDetalle(1, "", [], [c], e.Gastos, [], [], [], [], new Dictionary<Guid, string>());
        var resumen = Assert.Single(detalle.OrdenesServicio);
        Assert.Equal(52000, resumen.Facturado); Assert.Equal(2000, resumen.Diferencia); Assert.Equal(50000, resumen.Asignado);
        Assert.All(e.Gastos, g => Assert.Equal(50000, g.Cotizado));
    }
    [Fact]
    public void Rechaza_otro_proveedor_sucursal_empresa_cancelada_o_sin_servicios()
    {
        var empresa = Guid.NewGuid(); var sucursal = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        var oc = new OrdenServicioLectura(Guid.NewGuid(), empresa, sucursal, "OC-P6", proveedor, "Agente", "MXN", 50000,
            EstadoOrdenCompra.Autorizada, Guid.NewGuid(), "CONT1", [new(Guid.NewGuid(), "Servicio", 50000)]);
        OrdenesServicioServicio.ValidarOrden(oc, empresa, sucursal, proveedor);
        foreach (var invalida in new[] { oc with { ProveedorId = Guid.NewGuid() }, oc with { SucursalId = Guid.NewGuid() },
            oc with { EmpresaId = Guid.NewGuid() }, oc with { Estado = EstadoOrdenCompra.Cancelada }, oc with { Lineas = [] } })
            Assert.Throws<BusinessRuleException>(() => OrdenesServicioServicio.ValidarOrden(invalida, empresa, sucursal, proveedor));
    }
    [Fact]
    public void Reparto_explicito_y_limite_entre_expedientes()
    {
        Assert.Equal(30000, OrdenesServicioServicio.ValidarAsignacion(50000, 30000, null, [20000]));
        Assert.Throws<BusinessRuleException>(() => OrdenesServicioServicio.ValidarAsignacion(50000, 30000.01m, null, [20000]));
        Assert.Throws<BusinessRuleException>(() => OrdenesServicioServicio.ValidarAsignacion(50000, null, null, [20000]));
        Assert.Equal(20000, OrdenesServicioServicio.ValidarAsignacion(50000, null, 20000, [30000]));
        Assert.Throws<BusinessRuleException>(() => OrdenesServicioServicio.ValidarAsignacion(50000, 0, null, []));
    }
    [Fact]
    public void Conversion_usa_tc_del_gasto_y_preserva_moneda_de_oc()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "HONORARIOS", "Honorarios");
        var oc = new OrdenServicioCopia(Guid.NewGuid(), "OC-USD", 2500, "USD", 2500, 20);
        e.GuardarGasto(Guid.NewGuid(), new(c.Id, Guid.NewGuid(), "F1", "MXN", 52000, new(2026, 10, 1), OrdenServicioId: oc.Id), c, 1, "MXN", oc);
        var gasto = Assert.Single(e.Gastos); Assert.Equal(50000, gasto.Cotizado); Assert.Equal("USD", gasto.OrdenServicioMoneda);
        var resumen = Assert.Single(new FinanzasDetalle(1, "", [], [c], e.Gastos, [], [], [], [], new Dictionary<Guid, string>()).OrdenesServicio);
        Assert.Equal(2600, resumen.Facturado); Assert.Equal(100, resumen.Diferencia);
    }
}
