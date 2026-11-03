using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.UnitTests.Infrastructure;

public sealed class ModeloPersistenceTests
{
    private static ProductoTerminadoDbContext Crear() =>
        new(new DbContextOptionsBuilder<ProductoTerminadoDbContext>().UseNpgsql("Host=invalid;Database=pt_test").UseSnakeCaseNamingConvention().Options, new Empresa());

    private static bool Indice(Microsoft.EntityFrameworkCore.Metadata.IEntityType t, bool unique, params string[] props) =>
        t.GetIndexes().Any(i => i.IsUnique == unique && i.Properties.Select(p => p.Name).SequenceEqual(props));

    [Fact]
    public void Entidades_tienen_tenant_auditoria_y_concurrencia()
    {
        using var db = Crear();
        foreach (var type in new[] { typeof(PedidoPt), typeof(PedidoPtPosicion), typeof(PiezaPt), typeof(MovimientoPieza), typeof(EscaneoPt) })
        {
            Assert.True(typeof(IAuditable).IsAssignableFrom(type));
            Assert.True(typeof(IPerteneceAEmpresa).IsAssignableFrom(type));
            Assert.True(db.Model.FindEntityType(type)!.FindProperty("Version")!.IsConcurrencyToken);
        }
    }

    [Fact]
    public void Indices_unicos_y_parcial_de_la_matriz()
    {
        using var db = Crear();
        var piezas = db.Model.FindEntityType(typeof(PiezaPt))!;
        Assert.True(Indice(piezas, true, "PedidoPtId", "Posicion", "NumeroPieza"));
        Assert.True(Indice(piezas, true, "EmpresaId", "Etiqueta"));
        var esc = db.Model.FindEntityType(typeof(EscaneoPt))!;
        Assert.True(Indice(esc, true, "EmpresaId", "Origen", "Etiqueta", "RegistradoAt"));
        Assert.Contains(esc.GetIndexes(), i => i.GetFilter() == "resultado IN ('Pospuesto','Excepcion','Reposicion')");
        Assert.Equal("timestamp without time zone", esc.FindProperty("RegistradoAt")!.GetColumnType());
    }

    [Fact]
    public void Pieza_no_guarda_estatus_de_aw_y_no_hay_fk_fuera_del_esquema()
    {
        using var db = Crear();
        var piezas = db.Model.FindEntityType(typeof(PiezaPt))!;
        Assert.DoesNotContain(piezas.GetProperties(), p => p.Name.Contains("Estatus", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Aw", StringComparison.Ordinal));
        foreach (var fk in db.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            Assert.Equal(ProductoTerminadoDbContext.SchemaName, fk.PrincipalEntityType.GetSchema());
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => Guid.Empty;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
