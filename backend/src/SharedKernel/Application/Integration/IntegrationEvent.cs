namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Base record para eventos de integración: contratos cross-bounded-context
/// que eventualmente viajan a un broker externo (Service Bus). Distinto
/// de <c>MediatR.INotification</c> (in-proc, best-effort): los eventos
/// de integración requieren entrega garantizada vía Outbox transaccional
/// (ADR-0009).
///
/// <para>
/// Cada módulo subclasea para sus tipos concretos. El campo
/// <see cref="EventType"/> es la clave estable que el consumer usa para
/// despachar; convención: <c>compras.requisicion.autorizada.v1</c> (módulo
/// + recurso + acción + versión, separado por puntos).
/// </para>
/// <para>
/// F6-PR1 declara la base + infra de outbox; F6-PR3 wirea los 6 tipos
/// concretos del módulo Compras.
/// </para>
/// </summary>
public abstract record IntegrationEvent(
    string EventType,
    Guid EmpresaId,
    DateTimeOffset OcurridoEn);
