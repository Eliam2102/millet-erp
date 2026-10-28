using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// Punto de extensión de las pólizas automáticas (C1.4 ↔ K10.5). <see cref="IPolizaContablePort"/> lo llama dentro de la MISMA
/// transacción, después de guardar la póliza (registro) o su anulación en negativo: si el observador falla, tampoco se guarda la
/// póliza. K10.5 lo implementa para registrar los movimientos de la cuenta puente sin tocar el puerto ni RegistroPolizas.
/// Sin implementaciones registradas no hace nada.
/// </summary>
public interface IPolizaAutomaticaObservador
{
    Task PolizaRegistradaAsync(PolizaAutomaticaRegistrada poliza, CancellationToken ct);
}

/// <summary>
/// <paramref name="PolizaAnuladaId"/>: con valor, es la anulación en negativo de esa póliza (K10.3); sus partidas no traen
/// <see cref="ReferenciaPuente"/> (no se guarda), el observador revierte por la póliza original.
/// </summary>
public sealed record PolizaAutomaticaRegistrada(Guid PolizaId, string Folio, Guid EmpresaId, DateOnly Fecha, DocumentoOrigenPoliza Documento,
    Guid? PolizaAnuladaId, IReadOnlyList<PartidaAutomaticaRegistrada> Partidas);

public sealed record PartidaAutomaticaRegistrada(int Linea, Guid CuentaId, decimal Cargo, decimal Abono, ReferenciaPuente? Puente);
