namespace Millet.Compras.Application.MigracionEntrega;

/// <summary>
/// Resultado de una corrida (dry-run o real) del job de migración de
/// históricas (ADR-0043 PR #4). En dry-run los conteos son "lo que haría";
/// en real, lo aplicado.
/// </summary>
public sealed record ReporteMigracion
{
    public bool DryRun { get; init; }

    /// <summary>RQs <c>Cerrada</c> con entrega incompleta → reabiertas a <c>EnSurtido</c>.</summary>
    public int Movidas { get; init; }

    /// <summary>RQs <c>Cerrada</c> totalmente entregadas → quedan <c>Cerrada</c> (solo backfill).</summary>
    public int QuedanCerradas { get; init; }

    /// <summary>RQs <c>EnSurtido</c> → solo backfill de <c>cant_entregada</c> (sin reclasificar).</summary>
    public int BackfillEnSurtido { get; init; }

    /// <summary>Líneas cuyo <c>cant_entregada</c> se escribió (capado al techo).</summary>
    public int LineasBackfilleadas { get; init; }

    /// <summary>RQs ambiguas (artículo repetido + salida sin <c>LineaRqId</c>) — NO tocadas, para revisión manual.</summary>
    public IReadOnlyList<RqAmbigua> Ambiguas { get; init; } = [];
}

/// <summary>RQ que el job dejó intacta por atribución ambigua.</summary>
public sealed record RqAmbigua(Guid RqId, string Folio);
