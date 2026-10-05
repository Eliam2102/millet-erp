namespace Millet.Integraciones.Aw.Application.Cambios;

public enum AwEntidadCambio { Cliente, Producto }

public enum AwTipoCambio { Upsert, Eliminado }

/// <summary>Referencia (ID de cliente / BA_PRODUKT) cuyo último cambio capturado fue <paramref name="Tipo"/>.</summary>
public sealed record AwCambio(string Referencia, AwTipoCambio Tipo);

/// <summary>
/// <paramref name="SiguienteLsn"/> es el LSN (hex) hasta el que ya se procesó: se guarda y se pasa como <c>desdeLsn</c> (exclusivo).
/// <paramref name="Completo"/> = false si el lote llenó el tamaño y hay que seguir leyendo pasando <paramref name="SiguienteLsn"/>.
/// </summary>
public sealed record AwCambiosLote(IReadOnlyList<AwCambio> Cambios, string SiguienteLsn, bool Completo);

/// <summary>
/// Puerto de lectura de cambios de A+W vía SQL Server CDC (solo SELECT sobre <c>cdc.*</c>).
/// Solo dice QUÉ referencias cambiaron; el contenido se relee con <c>IAw*Origen.LeerPorReferenciaAsync</c>.
/// Una línea de composición (<c>BA_STUKL</c>) cambiada cuenta como cambio de su producto.
/// <c>Eliminado</c> = borrado físico: informativo, nunca baja automática (doc 05 §8).
/// </summary>
public interface IAwCambiosOrigen
{
    /// <summary>LSN máximo actual (hex). Punto de partida de un watermark nuevo tras un barrido completo.</summary>
    Task<string> ObtenerLsnActualAsync(CancellationToken ct);

    /// <summary><paramref name="desdeLsn"/> = último LSN ya procesado (exclusivo); null = desde el mínimo disponible. LSN anterior al mínimo (limpieza de CDC) → <c>AwReaderException(kind: "cdc_lsn_expirado")</c>: hacer barrido completo.</summary>
    Task<AwCambiosLote> LeerCambiosAsync(AwEntidadCambio entidad, string? desdeLsn, int tamano, CancellationToken ct);
}
