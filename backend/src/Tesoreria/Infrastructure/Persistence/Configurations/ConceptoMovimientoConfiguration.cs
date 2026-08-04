using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="ConceptoMovimiento"/> + seed
/// provisional del catálogo (§5.2 del levantamiento). ⚠️ Catálogo inicial
/// pendiente de validación de Javier contra su plantilla de flujo de
/// efectivo — corregir es editar filas, no código. La clasificación es
/// default por concepto, editable por movimiento (§5.2).
/// </summary>
public sealed class ConceptoMovimientoConfiguration : IEntityTypeConfiguration<ConceptoMovimiento>
{
    public void Configure(EntityTypeBuilder<ConceptoMovimiento> builder)
    {
        builder.ToTable("concepto_movimiento", t =>
        {
            t.HasCheckConstraint("ck_concepto_movimiento_clasificacion", "clasificacion_flujo IN (1, 2, 3)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.ClasificacionFlujo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => e.Nombre)
            .HasDatabaseName("ux_concepto_movimiento_nombre")
            .IsUnique();

        var seedTime = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(SeedConceptos.Select(c => new
        {
            Id = c.Id,
            Nombre = c.Nombre,
            ClasificacionFlujo = c.Clasificacion,
            Activo = true,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        }).ToArray());
    }

    /// <summary>
    /// Catálogo provisional (§5.2 del levantamiento; Javier valida).
    /// GUIDs deterministas para idempotencia — bloque <c>0000000b-1001-*</c>
    /// (mismo criterio que los seeds de CxP <c>00000007-1001-*</c> y
    /// CxC <c>0000000a-1001-*</c>).
    /// </summary>
    public static readonly IReadOnlyList<(Guid Id, string Nombre, ClasificacionFlujo Clasificacion)> SeedConceptos =
    [
        (Guid.Parse("0000000b-1001-0000-0000-000000000001"), "Pago a proveedor",              ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000002"), "Cobro de cliente",              ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000003"), "Depósito de caja",              ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000004"), "Comisión bancaria",             ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000005"), "Impuestos",                     ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000006"), "Nómina",                        ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000007"), "Traspaso entre cuentas propias", ClasificacionFlujo.Operacion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000008"), "Compra de activo fijo",         ClasificacionFlujo.Inversion),
        (Guid.Parse("0000000b-1001-0000-0000-000000000009"), "Venta de activo fijo",          ClasificacionFlujo.Inversion),
        (Guid.Parse("0000000b-1001-0000-0000-00000000000a"), "Intereses ganados",             ClasificacionFlujo.Financiamiento),
        (Guid.Parse("0000000b-1001-0000-0000-00000000000b"), "Intereses y gastos financieros", ClasificacionFlujo.Financiamiento),
        (Guid.Parse("0000000b-1001-0000-0000-00000000000c"), "Disposición de crédito",        ClasificacionFlujo.Financiamiento),
        (Guid.Parse("0000000b-1001-0000-0000-00000000000d"), "Pago de crédito",               ClasificacionFlujo.Financiamiento),
    ];
}
