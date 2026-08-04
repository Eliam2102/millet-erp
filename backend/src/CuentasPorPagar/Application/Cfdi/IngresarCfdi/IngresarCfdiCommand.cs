using MediatR;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;

/// <summary>
/// Comando para ingresar un CFDI al ERP (F1-PR1). Cubre los 3 canales
/// MVP: carga manual (endpoint HTTP multipart), mailbox (worker F2-PR2),
/// descarga SAT (worker F2-PR1). En todos los casos el flujo es el
/// mismo: parsear → dedupe por UUID → guardar XML/PDF en blob →
/// persistir <see cref="CfdiRecibido"/>.
///
/// <para>
/// <see cref="EmpresaId"/> no se acepta del cliente HTTP — los handlers
/// lo resuelven del JWT (<see cref="Millet.SharedKernel.Application.ICurrentEmpresaContext"/>).
/// Los workers de fondo lo resuelven del RFC receptor del XML mapeado a
/// la empresa correspondiente.
/// </para>
/// </summary>
public sealed record IngresarCfdiCommand(
    Stream Xml,
    Stream? Pdf,
    CanalOrigenCfdi Canal,
    Guid? EmpresaIdOverride = null) : IRequest<IngresarCfdiResponse>;

public sealed record IngresarCfdiResponse(
    Guid Id,
    string UuidCfdi,
    EstadoCfdiRecibido Estado,
    Guid? CfdiOriginalId);
