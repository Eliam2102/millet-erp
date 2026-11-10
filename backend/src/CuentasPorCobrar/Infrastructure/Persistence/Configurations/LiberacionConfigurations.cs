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
            // C7.3: 4 = PendienteAutorizacion (resultado) / Cobertura (regla).
            t.HasCheckConstraint("ck_decision_liberacion_resultado", "resultado IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_decision_liberacion_regla", "regla_aplicada IN (1, 2, 3, 4)");
            t.HasCheckConstraint(
                "ck_decision_liberacion_override_consistente",
                "(resultado = 3 AND override_id IS NOT NULL) OR (resultado != 3 AND override_id IS NULL)");
            t.HasCheckConstraint(
                "ck_decision_liberacion_cobertura_rango",
                "porcentaje_cobertura IS NULL OR (porcentaje_cobertura >= 0 AND porcentaje_cobertura <= 100)");
            t.HasCheckConstraint(
                "ck_decision_liberacion_pendiente_cobertura",
                "resultado != 4 OR (regla_aplicada = 4 AND porcentaje_cobertura >= 50 AND porcentaje_cobertura < 100)");
            // C7.3: la resolución de una pendiente (autorizar → 1/2/3 según
            // el crédito recalculado; rechazar → 2) nunca es otra pendiente.
            t.HasCheckConstraint(
                "ck_decision_liberacion_origen_resolucion",
                "decision_origen_id IS NULL OR resultado IN (1, 2, 3)");
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
        builder.Property(e => e.PorcentajeCobertura).HasPrecision(5, 2);
        builder.Property(e => e.DecisionOrigenId);
        builder.Property(e => e.Motivo).HasMaxLength(254);

        // §5.1 del 01-diseño.
        builder.HasIndex(e => e.PedidoRef)
            .HasDatabaseName("ix_decision_liberacion_pedido");

        builder.HasIndex(e => new { e.ClienteId, e.DecididoEn })
            .HasDatabaseName("ix_decision_liberacion_cliente_fecha");

        // C7.3: bandeja filtrada por resultado (p. ej. pendientes) ordenada por fecha.
        builder.HasIndex(e => new { e.Resultado, e.DecididoEn })
            .HasDatabaseName("ix_decision_liberacion_resultado_fecha");

        // C7.3: una pendiente se resuelve una sola vez (la carrera → 23505 → 409).
        builder.HasIndex(e => e.DecisionOrigenId)
            .HasDatabaseName("ux_decision_liberacion_origen")
            .IsUnique()
            .HasFilter("decision_origen_id IS NOT NULL");
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

        // C7.2: las series de Millet son datos (las carga la migración
        // ReglasSerieMillet y CxC las administra en su pantalla); no hay
        // HasData para que un cambio de regla no se revierta con un deploy.
    }

    /// <summary>
    /// Series de Millet (Cuestionario 04, R2 del plano Módulo 07). Las carga
    /// la migración C7.2 con GUIDs deterministas; se exponen para las pruebas.
    /// </summary>
    public static readonly IReadOnlyList<(Guid Id, string Prefijo, ComportamientoSerie Comportamiento)> SeriesMillet =
    [
        (Guid.Parse("0000000a-1001-0000-0000-000000000001"), "5000", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000006"), "5500", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000007"), "5600", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000002"), "7000", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000008"), "7200", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-000000000009"), "7500", ComportamientoSerie.SiempreLibera),
        (Guid.Parse("0000000a-1001-0000-0000-00000000000a"), "5800", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-00000000000b"), "5900", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-00000000000c"), "7800", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-00000000000d"), "7900", ComportamientoSerie.NuncaLibera),
        (Guid.Parse("0000000a-1001-0000-0000-00000000000e"), "9000", ComportamientoSerie.NuncaLibera),
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
        // M-085: autorización otorgada por Dirección (exceso arriba del tope de Crédito y Cobranza).
        builder.Property(e => e.AutorizadaPorDireccion).HasDefaultValue(false);

        // Bandeja de vigentes por beneficiario.
        builder.HasIndex(e => new { e.BeneficiarioUsuarioId, e.Estado })
            .HasDatabaseName("ix_autorizacion_credito_beneficiario_estado");
    }
}
