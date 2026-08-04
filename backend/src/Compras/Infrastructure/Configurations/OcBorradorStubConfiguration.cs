using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Infrastructure.Stubs;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="OcBorradorStub"/>. Tabla
/// <c>compras.oc_borrador_stub</c>. <see cref="OcBorradorStub.LineasJson"/>
/// se mapea como columna <c>jsonb</c> de Postgres para que un test pueda
/// inspeccionar el contenido (queries con <c>->></c>) si lo necesita.
///
/// PLATFORM-TODO(<![CDATA[<StubsTeardown>]]>): borrar este archivo +
/// la migración de drop cuando OC real exista.
/// </summary>
public sealed class OcBorradorStubConfiguration : IEntityTypeConfiguration<OcBorradorStub>
{
    public void Configure(EntityTypeBuilder<OcBorradorStub> builder)
    {
        builder.ToTable("oc_borrador_stub");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.OrigenRequisicionId).IsRequired();

        builder.Property(s => s.LineasJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(s => s.OrigenRequisicionId);
    }
}
