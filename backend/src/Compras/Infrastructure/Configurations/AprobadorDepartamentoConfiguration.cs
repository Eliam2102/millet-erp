using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// EF config de <see cref="AprobadorDepartamento"/> en
/// <c>compras.aprobadores_departamento</c> (F9-PR1).
///
/// <para>
/// PK: <c>id</c>. UNIQUE filtered index sobre
/// <c>(empresa_id, departamento_id, rol)</c> con <c>vigente_hasta IS NULL</c>
/// garantiza la invariante "a lo más un aprobador vigente por
/// (empresa, depto, rol)" a nivel base de datos. El handler de Designar
/// cierra la previa antes de insertar la nueva en una sola TX EF; sin la
/// TX, dos requests concurrentes podrían ambos cerrar la misma fila e
/// insertar dos vigentes — el unique index actúa como guardia.
/// </para>
/// </summary>
internal sealed class AprobadorDepartamentoConfiguration : IEntityTypeConfiguration<AprobadorDepartamento>
{
    public void Configure(EntityTypeBuilder<AprobadorDepartamento> builder)
    {
        builder.ToTable("aprobadores_departamento", t =>
        {
            t.HasCheckConstraint(
                "ck_aprobadores_departamento_rol",
                "rol BETWEEN 0 AND 2");
            t.HasCheckConstraint(
                "ck_aprobadores_departamento_vigencia",
                "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id);
        builder.Property(a => a.EmpresaId).IsRequired();
        builder.Property(a => a.DepartamentoId).IsRequired();
        builder.Property(a => a.Rol).HasConversion<short>().IsRequired();
        builder.Property(a => a.UsuarioId).IsRequired();
        builder.Property(a => a.VigenteDesde).IsRequired();
        builder.Property(a => a.VigenteHasta);
        builder.Property(a => a.DesignadoPor).IsRequired();
        builder.Property(a => a.Motivo).HasMaxLength(500);
        builder.Property(a => a.CreatedAt).IsRequired();

        // Filtered UNIQUE: solo una fila vigente por (empresa, depto, rol).
        builder.HasIndex(a => new { a.EmpresaId, a.DepartamentoId, a.Rol })
            .HasFilter("vigente_hasta IS NULL")
            .IsUnique()
            .HasDatabaseName("ux_aprobadores_departamento_vigente");

        // "Soy aprobador en qué deptos" — query frecuente.
        builder.HasIndex(a => new { a.EmpresaId, a.UsuarioId })
            .HasFilter("vigente_hasta IS NULL")
            .HasDatabaseName("ix_aprobadores_departamento_usuario_vigente");

        // Histórico: queries por (empresa, depto, rol) ordenadas por fecha.
        builder.HasIndex(a => new { a.EmpresaId, a.DepartamentoId, a.Rol, a.VigenteDesde })
            .IsDescending(false, false, false, true)
            .HasDatabaseName("ix_aprobadores_departamento_historico");
    }
}
