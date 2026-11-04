namespace Millet.Tesoreria.Domain.Ports.DatosMaestros;

/// <summary>Razón social y RFC vigentes de la empresa para documentos impresos de Tesorería.</summary>
public sealed record EmpresaActaDto(string RazonSocial, string Rfc);

/// <summary>Lectura de la empresa en Compartido (ADR-0042); null si no existe.</summary>
public interface IEmpresaActaReadPort
{
    Task<EmpresaActaDto?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken);
}
