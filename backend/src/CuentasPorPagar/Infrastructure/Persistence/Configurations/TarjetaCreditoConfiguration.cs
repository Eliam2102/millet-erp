using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class TarjetaConfiguration : IEntityTypeConfiguration<Tarjeta>
{
    public void Configure(EntityTypeBuilder<Tarjeta> builder)
    {
        builder.ToTable("tarjetas_credito", t =>
        {
            t.HasCheckConstraint("ck_tarjeta_dia_corte", "dia_corte BETWEEN 1 AND 31");
            t.HasCheckConstraint("ck_tarjeta_limite_positivo", "limite_credito_mxn > 0");
            t.HasCheckConstraint(
                "ck_tarjeta_bloqueo_consistente",
                "(estado = 2 AND fecha_bloqueo IS NOT NULL) OR (estado != 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Emisora).HasMaxLength(60).IsRequired();
        builder.Property(e => e.PerfilParser).HasMaxLength(40).IsRequired();

        builder.Property(e => e.Numero)
            .HasConversion(v => v.Valor, s => NumeroTarjetaEnmascarado.Parse(s))
            .HasColumnName("numero_enmascarado")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(e => e.NombreAlias).HasColumnName("nombre_alias").HasMaxLength(120).IsRequired();
        builder.Property(e => e.TitularId).IsRequired();
        builder.Property(e => e.BancoProveedorId).IsRequired();

        builder.Property(e => e.LimiteCreditoMxn).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.MonedaDefault).HasMaxLength(3).IsRequired();

        builder.Property(e => e.DiaCorte).IsRequired();
        builder.Property(e => e.DiaLimitePago).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaBloqueo);
        builder.Property(e => e.MotivoBloqueo).HasMaxLength(400);
        builder.Property(e => e.VigenciaDesde).IsRequired();
        builder.Property(e => e.VigenciaHasta);

        builder.HasMany(e => e.UsuariosAutorizados)
            .WithOne()
            .HasForeignKey(u => u.TarjetaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Tarjeta.UsuariosAutorizados))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.TitularId, e.Estado })
            .HasDatabaseName("ix_tarjetas_titular_estado");

        builder.HasIndex(e => e.BancoProveedorId)
            .HasDatabaseName("ix_tarjetas_banco");
    }
}

public sealed class TarjetaUsuarioAutorizadoConfiguration : IEntityTypeConfiguration<TarjetaUsuarioAutorizado>
{
    public void Configure(EntityTypeBuilder<TarjetaUsuarioAutorizado> builder)
    {
        builder.ToTable("tarjeta_usuarios_autorizados");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.TarjetaId).IsRequired();
        builder.Property(e => e.EmpleadoId).IsRequired();
        builder.Property(e => e.VigenciaDesde).IsRequired();
        builder.Property(e => e.VigenciaHasta);
        builder.Property(e => e.MontoMaxMensualMxn).HasPrecision(14, 2);

        builder.HasIndex(e => new { e.TarjetaId, e.EmpleadoId, e.VigenciaDesde })
            .HasDatabaseName("ix_tc_usuarios_tarjeta_empleado");
    }
}

public sealed class MovimientoTarjetaCreditoConfiguration : IEntityTypeConfiguration<MovimientoTarjetaCredito>
{
    public void Configure(EntityTypeBuilder<MovimientoTarjetaCredito> builder)
    {
        builder.ToTable("movimientos_tarjeta_credito", t =>
        {
            t.HasCheckConstraint("ck_mov_tc_monto_positivo", "monto_original > 0");
            t.HasCheckConstraint(
                "ck_mov_tc_cfdi_consistente",
                "(tipo = 1 AND cfdi_recibido_id IS NOT NULL AND factura_proveedor_id IS NOT NULL) OR " +
                "(tipo != 1 AND cfdi_recibido_id IS NULL)");
            t.HasCheckConstraint(
                "ck_mov_tc_refund_consistente",
                "(tipo = 3 AND movimiento_original_id IS NOT NULL) OR (tipo != 3)");
            t.HasCheckConstraint(
                "ck_mov_tc_moneda_consistente",
                "(moneda_original = 'MXN' AND tipo_cambio_captura IS NULL) OR " +
                "(moneda_original != 'MXN' AND tipo_cambio_captura IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.TarjetaId).IsRequired();
        builder.Property(e => e.UsuarioQueUsoId).IsRequired();
        builder.Property(e => e.FechaMovimiento).IsRequired();
        builder.Property(e => e.FechaAplicacionBanco);

        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        builder.Property(e => e.MontoOriginal).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.MonedaOriginal).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambioCaptura).HasPrecision(10, 4);
        builder.Property(e => e.MontoMxn).HasPrecision(14, 2).IsRequired();

        builder.Property(e => e.MerchantRaw).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MerchantNormalizado).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DescripcionLibre).HasMaxLength(1000);

        builder.Property(e => e.CfdiRecibidoId);
        builder.Property(e => e.FacturaProveedorId);
        builder.Property(e => e.ProveedorId);
        builder.Property(e => e.ConceptoContable).HasMaxLength(120).IsRequired();

        builder.Property(e => e.MovimientoOriginalId);

        builder.Property(e => e.EstadoCuentaTcId);
        builder.Property(e => e.EstadoCuentaTcLineaId);
        builder.Property(e => e.CapturaRetroactiva).IsRequired();

        builder.Property(e => e.TicketBlobRef).HasMaxLength(400);
        builder.Property(e => e.EnDisputa).IsRequired();
        builder.Property(e => e.MotivoDisputa).HasMaxLength(1000);
        builder.Property(e => e.FechaInicioDisputa);

        builder.HasIndex(e => new { e.TarjetaId, e.FechaMovimiento, e.Estado })
            .HasDatabaseName("ix_mov_tc_tarjeta_fecha_estado");

        builder.HasIndex(e => new { e.TarjetaId, e.EstadoCuentaTcId })
            .HasDatabaseName("ix_mov_tc_tarjeta_estado_cuenta")
            .HasFilter("estado_cuenta_tc_id IS NULL");

        builder.HasIndex(e => e.UsuarioQueUsoId)
            .HasDatabaseName("ix_mov_tc_usuario");

        builder.HasIndex(e => e.MerchantNormalizado)
            .HasDatabaseName("ix_mov_tc_merchant_norm");

        // FK opcional a CfdiRecibido (Flujo A) — solo informativa, sin
        // cascade ni nav property (CxP no edita CFDIs por aquí).
        builder.Property(e => e.CfdiRecibidoId).HasColumnName("cfdi_recibido_id");
    }
}
