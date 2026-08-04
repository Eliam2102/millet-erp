using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cajas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de la Capa B de Cajas (CAJAS-PR3, 12-cajas.md §7):
/// <c>facturacion.caja_sesion</c> / <c>caja_sesion_corte</c> /
/// <c>caja_movimiento</c> / <c>autorizacion_apertura_caja</c> /
/// <c>caja_ajuste_pendiente</c>. FK física a <c>caja</c> (mismo esquema);
/// referencias a sucursales/usuarios lógicas, como en el agregado Caja.
/// </summary>
public sealed class CajaSesionConfiguration : IEntityTypeConfiguration<CajaSesion>
{
    public void Configure(EntityTypeBuilder<CajaSesion> builder)
    {
        builder.ToTable("caja_sesion");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CajaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.ResponsableUsuarioId).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.DiaOperacion).IsRequired();
        builder.Property(e => e.FechaApertura).IsRequired();
        builder.Property(e => e.FechaCierre);
        builder.Property(e => e.FondoApertura).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.AutorizacionAperturaId);
        builder.Property(e => e.EfectivoDeclarado).HasPrecision(18, 2);
        builder.Property(e => e.EfectivoTeorico).HasPrecision(18, 2);
        builder.Property(e => e.Diferencia).HasPrecision(18, 2);
        builder.Property(e => e.CierreExtemporaneo).IsRequired();
        builder.Property(e => e.NotasCierre).HasMaxLength(500);

        builder.HasOne<Caja>()
            .WithMany()
            .HasForeignKey(e => e.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Cortes)
            .WithOne()
            .HasForeignKey(c => c.CajaSesionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Cortes).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Una caja no admite dos sesiones no-cerradas (§5.1 → 409).
        builder.HasIndex(e => e.CajaId)
            .IsUnique()
            .HasFilter("estado IN (1, 2)")
            .HasDatabaseName("ux_caja_sesion_no_cerrada");

        // Sesión vigente del responsable (query sesion-actual + bloqueo §5.2).
        builder.HasIndex(e => new { e.ResponsableUsuarioId, e.Estado })
            .HasDatabaseName("ix_caja_sesion_responsable_estado");

        builder.HasIndex(e => new { e.CajaId, e.DiaOperacion })
            .HasDatabaseName("ix_caja_sesion_caja_dia");
    }
}

public sealed class CajaSesionCorteConfiguration : IEntityTypeConfiguration<CajaSesionCorte>
{
    public void Configure(EntityTypeBuilder<CajaSesionCorte> builder)
    {
        builder.ToTable("caja_sesion_corte");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.FormaPago).HasMaxLength(2).IsRequired();
        builder.Property(e => e.MontoSistema).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.MontoDeclarado).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.CajaSesionId, e.FormaPago })
            .IsUnique()
            .HasDatabaseName("ux_caja_sesion_corte");
    }
}

public sealed class CajaMovimientoConfiguration : IEntityTypeConfiguration<CajaMovimiento>
{
    public void Configure(EntityTypeBuilder<CajaMovimiento> builder)
    {
        builder.ToTable("caja_movimiento");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CajaSesionId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.FormaPago).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.CobroMostradorId);
        builder.Property(e => e.Referencia).HasMaxLength(100);
        builder.Property(e => e.Descripcion).HasMaxLength(254).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();

        builder.HasOne<CajaSesion>()
            .WithMany()
            .HasForeignKey(e => e.CajaSesionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CajaSesionId).HasDatabaseName("ix_caja_movimiento_sesion");
        builder.HasIndex(e => e.CobroMostradorId)
            .HasDatabaseName("ix_caja_movimiento_cobro")
            .HasFilter("cobro_mostrador_id IS NOT NULL");
    }
}

public sealed class AutorizacionAperturaCajaConfiguration : IEntityTypeConfiguration<AutorizacionAperturaCaja>
{
    public void Configure(EntityTypeBuilder<AutorizacionAperturaCaja> builder)
    {
        builder.ToTable("autorizacion_apertura_caja");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CajaId).IsRequired();
        builder.Property(e => e.CajeroUsuarioId).IsRequired();
        builder.Property(e => e.SupervisorUsuarioId).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(254).IsRequired();
        builder.Property(e => e.FechaAutorizacion).IsRequired();
        builder.Property(e => e.VigenteHasta).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.CajaSesionId);

        builder.HasOne<Caja>()
            .WithMany()
            .HasForeignKey(e => e.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.CajaId, e.CajeroUsuarioId, e.Estado })
            .HasDatabaseName("ix_autorizacion_apertura_lookup");
    }
}

public sealed class CajaAjustePendienteConfiguration : IEntityTypeConfiguration<CajaAjustePendiente>
{
    public void Configure(EntityTypeBuilder<CajaAjustePendiente> builder)
    {
        builder.ToTable("caja_ajuste_pendiente");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CajaId).IsRequired();
        builder.Property(e => e.CobroMostradorId).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.FormaPago).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(254).IsRequired();
        builder.Property(e => e.CreadoPor).IsRequired();
        builder.Property(e => e.AplicadoEnSesionId);

        builder.HasOne<Caja>()
            .WithMany()
            .HasForeignKey(e => e.CajaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Drenado en apertura: solo los pendientes de la caja.
        builder.HasIndex(e => e.CajaId)
            .HasDatabaseName("ix_caja_ajuste_pendiente_caja")
            .HasFilter("aplicado_en_sesion_id IS NULL");
    }
}
