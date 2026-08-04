using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="CuentaBancaria"/> (§5 del
/// 01-diseño). Sin seed HasData: las cuentas reales se cargan por script
/// one-shot con placeholders (<c>backend/scripts/seed-cuentas-bancarias-tesoreria.sql</c>)
/// porque los datos bancarios reales no van en el repo (cuidados-infra
/// §3.3) y el <c>empresa_id</c> es dato runtime.
/// </summary>
public sealed class CuentaBancariaConfiguration : IEntityTypeConfiguration<CuentaBancaria>
{
    public void Configure(EntityTypeBuilder<CuentaBancaria> builder)
    {
        builder.ToTable("cuenta_bancaria");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Banco).HasMaxLength(120).IsRequired();
        builder.Property(e => e.NumeroCuenta).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Clabe).HasMaxLength(18);
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.CuentaContableRef).HasMaxLength(40);
        builder.Property(e => e.PerfilExtracto).HasMaxLength(40);
        builder.Property(e => e.Activa).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.NumeroCuenta })
            .HasDatabaseName("ux_cuenta_bancaria_empresa_numero")
            .IsUnique();
    }
}
