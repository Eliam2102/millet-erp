using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="UmbralAprobacionDepartamento"/>
/// (tabla <c>compras.umbrales_aprobacion_departamento</c>, diseño
/// §3.bis.2). PK compuesta para soportar histórico por vigencia.
/// CHECK sobre formato ISO de moneda.
/// </summary>
public sealed class UmbralAprobacionDepartamentoConfiguration : IEntityTypeConfiguration<UmbralAprobacionDepartamento>
{
    public void Configure(EntityTypeBuilder<UmbralAprobacionDepartamento> builder)
    {
        builder.ToTable("umbrales_aprobacion_departamento", t =>
        {
            t.HasCheckConstraint("ck_umbrales_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_umbrales_monto_no_negativo", "umbral_monto >= 0");
        });

        builder.HasKey(u => new { u.EmpresaId, u.DepartamentoId, u.VigenteDesde });

        builder.Property(u => u.EmpresaId).IsRequired();
        builder.Property(u => u.DepartamentoId).IsRequired();
        builder.Property(u => u.VigenteDesde).IsRequired();
        builder.Property(u => u.VigenteHasta);

        builder.Property(u => u.UmbralMonto).HasPrecision(15, 2).IsRequired();
        builder.Property(u => u.Moneda).HasMaxLength(3).HasDefaultValue("MXN").IsRequired();
    }
}
