using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="MotivoRevision"/> + seed
/// inicial con los 14 motivos del §5.3 del 00-levantamiento. SLA
/// único de 5 días excepto "Disputa contractual" (15) e "Indicación
/// expresa" (NULL).
/// </summary>
public sealed class MotivoRevisionConfiguration : IEntityTypeConfiguration<MotivoRevision>
{
    public void Configure(EntityTypeBuilder<MotivoRevision> builder)
    {
        builder.ToTable("motivos_revision");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Codigo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(400);
        builder.Property(e => e.SlaDias);
        builder.Property(e => e.DependenciaRevisoraDefaultCodigo).HasMaxLength(40);
        builder.Property(e => e.AreaAvisadaCodigo).HasMaxLength(40);
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => e.Codigo)
            .HasDatabaseName("ux_motivos_revision_codigo")
            .IsUnique();

        var seedTime = new DateTimeOffset(2026, 5, 22, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(SeedMotivos.Select(m => new
        {
            Id = m.Id,
            Codigo = m.Codigo,
            Nombre = m.Nombre,
            Descripcion = (string?)m.Descripcion,
            SlaDias = (int?)m.SlaDias,
            DependenciaRevisoraDefaultCodigo = (string?)m.Dependencia,
            AreaAvisadaCodigo = m.AreaAvisada,
            Activo = true,
            Version = 1,
            CreatedAt = seedTime,
            UpdatedAt = seedTime,
            CreatedBy = (string?)"seed",
            UpdatedBy = (string?)"seed",
            DeletedAt = (DateTimeOffset?)null,
        }).ToArray());
    }

    /// <summary>14 motivos canónicos del §5.3. GUIDs deterministas para idempotencia.</summary>
    public static readonly IReadOnlyList<(Guid Id, string Codigo, string Nombre, string Descripcion, int? SlaDias, string Dependencia, string? AreaAvisada)> SeedMotivos =
    [
        (Guid.Parse("00000007-1001-0000-0000-000000000001"), "DISCREPANCIA_OC",       "Diferencia con la OC (no de monto)", "Diferencias distintas al monto: artículo, cantidad, condiciones; las diferencias de monto las resuelve la tolerancia",                   5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000002"), "DANOS_MERCANCIA",       "Daños o defectos en mercancía",            "Compras gestiona la nota de crédito por daño y la entrega a Cuentas por pagar",    5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000003"), "DIFERENCIA_PRECIO",     "Diferencia de precio (solo sin OC)",                      "Precio de factura diferente al acordado; solo aplica a facturas sin OC",                          5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000004"), "DEVOLUCION_PENDIENTE",  "Devolución pendiente",                      "Compras gestiona la nota de crédito; Almacén registra la salida por devolución", 5,  "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000005"), "NO_CONFORMIDAD",        "No conformidad con servicio o producto",   "El área solicitante valida; si no está satisfecha, avisa a Compras",                                5,    "AREA_USUARIA", "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000006"), "TIEMPO_RESPUESTA",      "Tiempo de respuesta del proveedor",        "Entrega fuera de plazo",                                            5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000007"), "CALIDAD_SERVICIO",      "Calidad de servicio insuficiente",         "El área solicitante valida la calidad; si no está satisfecha, avisa a Compras",                                5,    "AREA_USUARIA", "COMPRAS"),
        (Guid.Parse("00000007-1001-0000-0000-000000000008"), "GARANTIA_ABIERTA",      "Reclamo de garantía abierto",              "Reclamo no resuelto",                                              5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-000000000009"), "FALTA_REPP",            "Falta complemento de pago + 5 días",        "Compras solicita el complemento de pago al proveedor y lo envía a Tesorería",                          5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-00000000000a"), "DOCUMENTACION_FISCAL", "Documentación fiscal incompleta",          "Cuentas por pagar detecta datos fiscales incorrectos; Compras gestiona la corrección con el proveedor",          5,    "COMPRAS", null),
        (Guid.Parse("00000007-1001-0000-0000-00000000000b"), "DISPUTA_CONTRACTUAL",  "Disputa contractual",                       "Disputa con proveedor atendida por Dirección de Finanzas (15 días hábiles)",                                                15,   "DIRECCION_F", null),
        (Guid.Parse("00000007-1001-0000-0000-00000000000c"), "FIRMA_PENDIENTE",      "Falta de firma (solo sin OC y gastos aduanales)",            "Autorización formal pendiente; una OC autorizada ya autoriza su factura",                                     5,    "DIRECCION_F", null),
        (Guid.Parse("00000007-1001-0000-0000-00000000000d"), "INDICACION_EXPRESA",   "Indicación expresa",                        "Bloqueo manual por instrucción (sin SLA — libera el solicitante)",  null, "CXP", null),
        (Guid.Parse("00000007-1001-0000-0000-00000000000e"), "OTROS",                "Otros motivos",                             "Caso no clasificable",                                              5,    "CXP", null),
    ];
}
