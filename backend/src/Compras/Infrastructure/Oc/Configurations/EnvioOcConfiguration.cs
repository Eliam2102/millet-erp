using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

public sealed class EnvioOcConfiguration : IEntityTypeConfiguration<EnvioOc>
{
    public void Configure(EntityTypeBuilder<EnvioOc> builder)
    {
        builder.ToTable("envios_oc", t =>
        {
            t.HasCheckConstraint("ck_envios_oc_modo", "modo IN ('correo', 'manual')");
            t.HasCheckConstraint("ck_envios_oc_resultado", "resultado IN ('pendiente', 'registrado', 'simulado', 'aceptado', 'fallido')");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Destinatario).HasMaxLength(254).IsRequired();
        builder.Property(e => e.UsuarioNombre).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Modo).HasMaxLength(12).IsRequired();
        builder.Property(e => e.Resultado).HasMaxLength(16).IsRequired();
        builder.Property(e => e.PdfBlobUrl).HasColumnType("text").IsRequired();
        // El PDF puede regenerarse: conservar su identidad y blob sin FK al registro reemplazable.
        builder.HasOne<OrdenCompra>().WithMany().HasForeignKey(e => e.OrdenCompraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.EmpresaId, e.OrdenCompraId, e.Fecha });
    }
}
