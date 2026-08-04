namespace Millet.Compartido.Application.Ports;

/// <summary>
/// Port que mapea un RFC de empresa a sus datos mínimos (Id + RazonSocial).
/// Vive en <c>Millet.Compartido.Application</c> porque la entidad
/// <c>Empresa</c> es propiedad de Compartido — los consumers (Identidad
/// bootstrap de SPs, futuros módulos que necesiten resolver empresas)
/// referencian este port sin acoplar a la implementación concreta.
///
/// <para>
/// Decisión de ubicación: el prompt PR A pedía el port en
/// <c>Identidad.Application</c>, pero Compartido no referencia a Identidad
/// (al revés sí), lo que generaría dependencia circular si el adapter
/// vive en Compartido. Mover el port a Compartido.Application resuelve
/// la dirección sin perder el aislamiento — Identidad ya referencia
/// Compartido, así que su bootstrap consume el port sin problema.
/// </para>
///
/// <para>
/// Devuelve <c>null</c> si el RFC no corresponde a ninguna empresa
/// existente en <c>compartido.empresas</c>. El bootstrap de SPs usa eso
/// para hacer skip + LogError de un SP mal configurado sin tirar el host
/// (D-BOOTSTRAP del prompt PR A).
/// </para>
/// </summary>
public interface IEmpresaResolverPort
{
    Task<EmpresaResolution?> ResolveByRfcAsync(string rfc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Datos mínimos de una empresa que el bootstrap necesita conocer:
/// <see cref="Id"/> (FK destino) y <see cref="RazonSocial"/> (para logs
/// legibles). NO se devuelve la entidad <c>Empresa</c> completa para
/// evitar acoplar consumers a tipos de Compartido.Domain (Empresa
/// vive en Administracion.Domain).
/// </summary>
public sealed record EmpresaResolution(Guid Id, string Rfc, string RazonSocial);
