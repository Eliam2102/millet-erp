namespace Millet.Compras.Application.Integration.AlmCompra;

/// <summary>
/// Puerto de transporte ERP → Almacenes (tabla <c>alm_compra</c> acordada con Jorge el 29-oct: el ERP escribe el
/// mensaje, Almacenes escribe el acuse en la misma fila). Interfaz en Compras (dueño del outbox); adaptadores en
/// Integraciones.Aw: simulado (por omisión) y SQL (apagado hasta que exista la tabla).
/// </summary>
public interface IAlmCompraTransporte
{
    /// <summary>Escribe el mensaje. Idempotente por <see cref="AlmCompraMensaje.MensajeId"/>: repetirlo no duplica ni falla.</summary>
    Task EscribirAsync(AlmCompraMensaje mensaje, CancellationToken ct);

    /// <summary>Acuse de Almacenes para ese mensaje; <c>null</c> mientras Almacenes no lo haya procesado.</summary>
    Task<AlmCompraAcuse?> LeerAcuseAsync(string mensajeId, CancellationToken ct);
}

public sealed record AlmCompraMensaje(
    string MensajeId, string Tipo, int Version, DateTimeOffset Fecha, string Referencia, string Sobre);

/// <summary>Estado: PROCESADO, DUPLICADO o ERROR (contrato v6 §9). <c>CompraId</c> es el id de la compra en Almacenes.</summary>
public sealed record AlmCompraAcuse(string Estado, string? CompraId, string? Error, DateTimeOffset? Fecha);

/// <summary>
/// Sección <c>Integraciones:AlmCompra</c>. <see cref="Habilitado"/> apaga el worker (la bandeja se sigue llenando);
/// <see cref="Transporte"/> elige el adaptador: <c>Simulado</c> (por omisión) o <c>Sql</c>.
/// </summary>
public sealed class AlmCompraOptions
{
    public const string SectionName = "Integraciones:AlmCompra";

    public bool Habilitado { get; init; }
    public string Transporte { get; init; } = "Simulado";
    public int IntervaloSegundos { get; init; } = 10;
    public int MaxIntentos { get; init; } = 8;
}
