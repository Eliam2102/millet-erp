using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.CuentasPorPagar.Application.PublicPorts;
namespace Millet.ComercioExterior.UnitTests;
public sealed class CierreServiciosCeTests
{
    [Theory]
    [InlineData(100, true, 1)]
    [InlineData(50, true, 1)]
    [InlineData(0, true, 0)]
    [InlineData(0, false, 1)]
    [InlineData(-1, true, 1)]
    public async Task Importacion_requiere_servicio_autorizado_y_saldado(decimal saldo, bool autorizada, int faltantes)
    {
        var e = Nuevo(TipoExpediente.Importacion); var concepto = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete");
        var id = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        e.GuardarGasto(Guid.NewGuid(), new(concepto.Id, proveedor, "MUESTRA-CE03", "MXN", 100, new(2026, 11, 11), FacturaProveedorId: id), concepto, 1, "MXN");
        var port = new Facturas([new(id, e.EmpresaId, proveedor, e.SucursalId, "MUESTRA-CE03", saldo, autorizada)]);
        var motivos = await new CierreServiciosCeServicio(port).MotivosAsync(e, default);
        Assert.Equal(faltantes, motivos.Count);
        if (autorizada && saldo != 0) Assert.Contains("MXN", motivos.Single());
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Factura_ausente_de_otra_empresa_o_con_pago_sin_conciliar_bloquea(bool otraEmpresa, bool conciliando)
    {
        var e = Nuevo(TipoExpediente.Importacion); var concepto = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete");
        var id = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        e.GuardarGasto(Guid.NewGuid(), new(concepto.Id, proveedor, "PRUEBA-SALDO", "MXN", 100, new(2026, 11, 11), FacturaProveedorId: id), concepto, 1, "MXN");
        var port = new Facturas(otraEmpresa || conciliando
            ? [new(id, otraEmpresa ? Guid.NewGuid() : e.EmpresaId, proveedor, e.SucursalId, "PRUEBA-SALDO", 0, true, conciliando)] : []);
        Assert.Single(await new CierreServiciosCeServicio(port).MotivosAsync(e, default));
    }
    [Theory]
    [InlineData("proveedor")]
    [InlineData("sucursal")]
    [InlineData("moneda")]
    public async Task Factura_saldada_con_vinculo_incompatible_no_permite_cierre(string diferencia)
    {
        var e = Nuevo(TipoExpediente.Importacion); var concepto = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete");
        var id = Guid.NewGuid(); var proveedor = Guid.NewGuid();
        e.GuardarGasto(Guid.NewGuid(), new(concepto.Id, proveedor, "PRUEBA-VINCULO", "MXN", 100, new(2026, 11, 11), FacturaProveedorId: id), concepto, 1, "MXN");
        var port = new Facturas([new(id, e.EmpresaId, diferencia == "proveedor" ? Guid.NewGuid() : proveedor,
            diferencia == "sucursal" ? Guid.NewGuid() : e.SucursalId, "PRUEBA-VINCULO", 0, true, Moneda: diferencia == "moneda" ? "USD" : "MXN")]);
        Assert.Single(await new CierreServiciosCeServicio(port).MotivosAsync(e, default));
    }
    [Fact]
    public async Task Exportacion_no_adquiere_regla_de_pago_de_importacion()
    {
        var e = Nuevo(TipoExpediente.Exportacion);
        Assert.Empty(await new CierreServiciosCeServicio(new Facturas([])).MotivosAsync(e, default));
    }

    private static Expediente Nuevo(TipoExpediente tipo) => new(Guid.NewGuid(), Guid.NewGuid(), "CE-PRUEBA", tipo,
        Guid.NewGuid(), "Prueba cierre financiero", tipo == TipoExpediente.Importacion ? Guid.NewGuid() : null, Guid.NewGuid(), "MXN", Guid.NewGuid(), tipo == TipoExpediente.Exportacion ? ["Cliente ficticio"] : []);
    private sealed class Facturas(IReadOnlyList<FacturaServicioSaldo> saldos) : IFacturaGastoReadPort
    {
        public Task<IReadOnlyList<FacturaServicioSaldo>> ObtenerSaldosAsync(Guid empresa, IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult(saldos);
        public Task<FacturaGastoLectura?> BuscarAsync(Guid proveedor, string referencia, Guid? id, CancellationToken ct) => throw new NotSupportedException();
    }
}
