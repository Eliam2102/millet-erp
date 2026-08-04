using MediatR;

namespace Millet.Integraciones.Aw.Application.Commands.RegistrarCotizacionEdi;

/// <summary>
/// Comando para registrar una nueva cotización EDI enviada por el Glass
/// Agent al ERP. El handler persiste la entidad raíz en estado
/// <c>Submitted</c> y emite el evento <c>AwCotizacionRecibida</c> al
/// Outbox para que <c>AwDropWorker</c> (PR C) ejecute el drop al
/// on-prem asíncronamente.
///
/// <para>
/// <b>Idempotency:</b> el <c>IdempotencyMiddleware</c> en SharedKernel
/// garantiza atomicidad por <c>(sub, current_empresa_id, idempotency_key)</c>
/// vía UNIQUE constraint en <c>core.idempotency_keys</c> antes de que
/// la request llegue al handler. NO se almacena la idempotency_key en
/// <c>entidad_externa</c> — la unicidad de negocio (no aceptar dos
/// cotizaciones con el mismo <c>QuoteReference</c>) la valida el UNIQUE
/// constraint <c>uq_tipo_referencia</c>.
/// </para>
///
/// <para>
/// <b>EmpresaId:</b> el handler la resuelve desde
/// <c>ICurrentEmpresaContext.Current</c> (claim <c>current_empresa_id</c>
/// del JWT, unificado por PR A entre humanos y SPs). NO viene en el
/// payload — los SPs en Entra están ligados 1:1 con empresa por diseño.
/// </para>
/// </summary>
public sealed record RegistrarCotizacionEdiCommand(
    string QuoteReference,
    string Sucursal,
    string EdiContent,
    string CustomerTaxId,
    string CustomerName,
    string Source,
    int ItemsCount,
    string PayloadOriginalJson
) : IRequest<RegistrarCotizacionEdiResponse>;
