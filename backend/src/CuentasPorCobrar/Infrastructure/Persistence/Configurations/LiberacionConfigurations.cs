using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Liberacion;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class DecisionLiberacionConfiguration : IEntityTypeConfiguration<DecisionLiberacion>
{
    public void Configure(EntityTypeBuilder<DecisionLiberacion> builder)
    {
        builder.ToTable("decision_liberacion", t =>
        {
            t.HasCheckConstraint("ck_decision_liberacion_monto_positivo", "monto_pedido > 0");
            t.HasCheckConstraint("ck_decision_liberacion_resultado", "resultado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_decision_liberacion_regla", "regla_aplicada IN (1, 2, 3)");
            t.HasCheckConstraint(
                "ck_decision_liberacion_override_consistente",
                "(resultado = 3 AND override_id IS NOT NULL) OR (resultado != 3 AND override_id IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.PedidoRef).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoPedido).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.CreditoDisponibleSnapshot).HasPrecision(14, 2).IsRequired();

        builder.Property(e => e.Resultado).HasConversion<short>().IsRequired();
        builder.Property(e => e.ReglaAplicada).HasConversion<short>().IsRequired();
        builder.Property(e => e.OverrideId);
        builder.Property(e => e.DecididoPor).IsRequired();
        builder.Property(e => e.DecididoEn).IsRequired();

        // §5.1 del 01-diseño.
        builder.HasIndex(e => e.PedidoRef)
            .HasDatabaseName("ix_decision_liberacion_pedido");

        builder.HasIndex(e => new { e.ClienteId, e.DecididoEn })
            .HasDatabaseName("ix_decision_liberacion_cliente_fecha");
    }
}

public sealed class ReglaLiberacionSerieConfiguration : IEntityTypeConfiguration<ReglaLiberacionSerie>
{
    public void Configure(EntityTypeBuilder<ReglaLiberacionSerie> builder)
    {
        builder.ToTable("regla_liberacion_serie", t =>
        {
            t.HasCheckConstraint("ck_regla_liberacion_comportamiento", "comportamiento IN (1, 2, 3)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Prefijo).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Comportamiento).HasConversion<short>().IsRequired();
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => e.Prefijo)
            .HasDatabaseName("ux_regla_liberacion_prefijo")
            .IsUnique();

        // Seed provisional del documento internacional §8.3 (levantamiento §2):
        // 5000/7000 siempre liberan; 3000/4000/8000 nunca. ⚠️ Gate suave —
        // confirmar con Prida si la cartera nacional comparte estas series;
        // corregir es editar filas, no código.
        var seedTime = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(SeedReglas.Select(r => new
        {
            Id = r.Id,
            Prefijo = r.Prefijo,
            Comportamiento = r.Comportamiento,
            Activo = true,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        }).ToArray());
    }

    /// <summary>Series del doc internacional §8.3. GUIDs deterministas para idempotencia.</summary>
    public static readonly IReadOnlyList<(Guid Id, string Prefijo, ComportamientoSerie Comportamiento)> SeedReglas =
    [
        (Guid.Parse("0000000a-1001-0000-0000-000000000001"), "5000", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000002"), "7000", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000003"), "3000", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000004"), "4000", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000005"), "8000", ComportamientoSerie.NuncaLibera),
    ];
}

public sealed class AutorizacionCreditoConfiguration : IEntityTypeConfiguration<AutorizacionCredito>
{
    public void Configure(EntityTypeBuilder<AutorizacionCredito> builder)
    {
        builder.ToTable("autorizacion_credito", t =>
        {
            t.HasCheckConstraint("ck_autorizacion_credito_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint(
                "ck_autorizacion_credito_no_autoconsumo",
                "supervisor_usuario_id != beneficiario_usuario_id");
            t.HasCheckConstraint(
                "ck_autorizacion_credito_uso_consistente",
                "(estado = 2 AND decision_liberacion_id IS NOT NULL) OR (estado != 2 AND decision_liberacion_id IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SupervisorUsuarioId).IsRequired();
        builder.Property(e => e.BeneficiarioUsuarioId).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(254).IsRequired();
        builder.Property(e => e.ClienteOPedidoRef).HasMaxLength(80).IsRequired();
        builder.Property(e => e.FechaAutorizacion).IsRequired();
        builder.Property(e => e.VigenteHasta).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.DecisionLiberacionId);

        // Bandeja de vigentes por beneficiario.
        builder.HasIndex(e => new { e.BeneficiarioUsuarioId, e.Estado })
            .HasDatabaseName("ix_autorizacion_credito_beneficiario_estado");
    }
}
