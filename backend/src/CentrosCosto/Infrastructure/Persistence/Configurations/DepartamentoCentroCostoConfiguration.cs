using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CentrosCosto.Domain;
namespace Millet.CentrosCosto.Infrastructure.Persistence.Configurations;
public sealed class DepartamentoCentroCostoConfiguration : IEntityTypeConfiguration<DepartamentoCentroCosto>
{
    public void Configure(EntityTypeBuilder<DepartamentoCentroCosto> builder)
    {
        builder.ToTable("departamento_centros_costo");
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Observaciones).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.DepartamentoId }).IsUnique();
    }
}
