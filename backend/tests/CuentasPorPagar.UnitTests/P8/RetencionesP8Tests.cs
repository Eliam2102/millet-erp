using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Application.Catalogos.Retenciones;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.P8;

public sealed class RetencionesP8Tests
{
    [Fact]
    public void Honorarios_propone_ISR_e_IVA_y_alerta_incluso_si_total_cuadra_pero_impuesto_no()
    {
        var reglas = new[] { RetencionConcepto.Crear("HONORARIOS_PF", "FIX honorarios", "001", .10m, "FIX fuente", "FIX propuesta fiscal"),
            RetencionConcepto.Crear("HONORARIOS_PF", "FIX honorarios IVA", "002", .10666667m, "FIX fuente", "FIX propuesta fiscal") };
        var propuesta = ComparadorRetenciones.Proponer(1000, reglas);
        propuesta.Sum(r => r.Importe).Should().Be(206.67m);
        ComparadorRetenciones.Alerta("HONORARIOS_PF", 1000, 206.67m, propuesta, reglas).Should().BeNull();
        ComparadorRetenciones.Alerta("HONORARIOS_PF", 1000, 206.67m, [new("001", .206667m, 206.67m)], reglas).Should().Contain("difieren");
    }
    [Fact]
    public async Task Alerta_no_bloquea_captura_y_queda_visible_en_factura()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); f.AsignarDatosP8("FIX-OBRA", "HONORARIOS_PF");
        a.Db.Add(f); await a.Db.SaveChangesAsync();
        f.AlertaRetenciones.Should().Contain("difieren").And.Contain("Fiscal");
        a.Db.ChangeTracker.Clear(); (await a.Db.FacturasProveedor.FindAsync(f.Id))!.AlertaRetenciones.Should().Be(f.AlertaRetenciones);
    }
    [Fact]
    public async Task Catalogo_administrable_propuesta_y_control_de_version()
    {
        await using var a = new P8Fixture(); var h = new GuardarRetencionHandler(a.Db);
        var r = await h.Handle(new(null, null, "FIX-P8", "FIX regla", "001", .0125m, "FIX fuente", true, "FIX supuesto para Fiscal"), default);
        var p = await new ProponerRetencionesHandler(a.Db).Handle(new("FIX-P8", 1000), default);
        p.Single().Importe.Should().Be(12.5m);
        Func<Task> actualizar = async () => await h.Handle(new(r.Id, r.Version+1, r.Concepto, r.Descripcion, r.Impuesto, .10m, r.Fuente, true, "FIX modificación fiscal"), default);
        await actualizar.Should().ThrowAsync<ConcurrencyException>();
    }
}
