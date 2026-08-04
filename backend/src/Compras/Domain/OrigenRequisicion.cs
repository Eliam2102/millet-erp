namespace Millet.Compras.Domain;

/// <summary>
/// Origen de una <see cref="Requisicion"/>: capturada por un humano o generada por
/// el motor de reorden (ADR-0047 PR5). El default es <see cref="Manual"/> — toda RQ
/// existente y toda captura humana queda Manual. El motor (PR5.C) marca sus RQ
/// automáticas como <see cref="Sistema"/>; el cálculo del faltante (PR5.B) cuenta
/// como "lo vivo" SOLO los documentos de origen <see cref="Sistema"/>.
/// </summary>
public enum OrigenRequisicion : short
{
    /// <summary>RQ capturada por un humano (default).</summary>
    Manual = 0,

    /// <summary>RQ generada automáticamente por el motor de reorden.</summary>
    Sistema = 1,
}
