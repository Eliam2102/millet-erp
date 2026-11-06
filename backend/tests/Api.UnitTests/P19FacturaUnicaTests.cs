using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.UnitTests;
public sealed class P19FacturaUnicaTests
{
    [Fact]
    public async Task Misma_factura_en_otro_expediente_rechaza_con_folio_y_editar_mismo_gasto_se_admite()
    {
        await using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var empresa = Guid.NewGuid(); var factura = Guid.NewGuid(); var gasto = Guid.NewGuid();
        var e = new Expediente(Guid.NewGuid(), empresa, "CE-OTRO-P19", TipoExpediente.Importacion,
            Guid.NewGuid(), "Prueba", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        var c = new ConceptoGasto(empresa, "FLETE", "Flete");
        e.GuardarGasto(gasto, new(c.Id, Guid.NewGuid(), Guid.NewGuid().ToString(), "MXN", 100, new(2026, 10, 1), FacturaProveedorId: factura), c, 1, "MXN");
        db.Expedientes.Add(e); db.ConceptosGasto.Add(c); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ValidacionesFinancieras.FacturaUnicaAsync(db, empresa, factura, Guid.NewGuid(), default));
        Assert.Equal("Esta factura ya está en el expediente CE-OTRO-P19", error.Message);
        await ValidacionesFinancieras.FacturaUnicaAsync(db, empresa, factura, gasto, default);
        var indice = db.Model.FindEntityType(typeof(GastoExpediente))!.GetIndexes().Single(i => i.Properties.Count == 2 && i.Properties[0].Name == "EmpresaId" && i.Properties[1].Name == "FacturaProveedorId");
        Assert.True(indice.IsUnique);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null; public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada(); private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
