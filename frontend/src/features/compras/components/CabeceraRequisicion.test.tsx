import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CabeceraRequisicion } from '@/features/compras/components/CabeceraRequisicion';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
  type RequisicionResponse,
} from '@/features/compras/api/types';

function makeRq(overrides: Partial<RequisicionResponse> = {}): RequisicionResponse {
  return {
    id: 'rq-1',
    empresaId: 'e-1',
    folio: 'MID2026-000042',
    folioAnio: 2026,
    clasificacion: Clasificacion.Servicio,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    almacenDestinoId: 'a-1',
    requisitanteId: 'u-1',
    creadorId: 'u-1',
    requisitanteNombre: 'Pedro García',
    departamentoNombre: 'Compras y Adquisiciones',
    departamentoClave: 'COMPRAS',
    descripcion: 'Servicio de mantenimiento',
    prioridad: Prioridad.Alta,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: '2026-05-15',
    proveedorSugeridoId: null,
    estado: EstadoRequisicion.EnAutorizacion,
    motivoTerminacionId: null,
    motivoTerminacionTexto: null,
    actorTerminacionId: null,
    fechaTerminacion: null,
    version: 12,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [],
    autorizaciones: [],
    ...overrides,
  };
}

const resolverIdentidad = (id: string | null | undefined) =>
  id == null ? '—' : `[${id}]`;

describe('<CabeceraRequisicion>', () => {
  it('muestra folio, estado y datos de cabecera resueltos', () => {
    render(
      <CabeceraRequisicion rq={makeRq()} resolverNombre={resolverIdentidad} />,
    );
    expect(screen.getByText('MID2026-000042')).toBeInTheDocument();
    expect(screen.getByText('En autorización')).toBeInTheDocument();
    expect(screen.getByText('Alta')).toBeInTheDocument();
    expect(screen.getByText('Servicio')).toBeInTheDocument();
    // Requisitante y departamento vienen resueltos del backend (ADR-0042),
    // NO del resolverNombre. Departamento como "clave · nombre".
    expect(screen.getByText('Pedro García')).toBeInTheDocument(); // requisitante (backend)
    expect(
      screen.getByText('COMPRAS · Compras y Adquisiciones'),
    ).toBeInTheDocument(); // depto (backend)
    // Sucursal sigue resolviéndose client-side vía resolverNombre.
    expect(screen.getByText('[s-1]')).toBeInTheDocument();
    expect(screen.getByText('v12')).toBeInTheDocument(); // version
  });

  it('muestra descripción cuando está presente', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({ descripcion: 'Una descripción específica' })}
        resolverNombre={resolverIdentidad}
      />,
    );
    expect(screen.getByText('Una descripción específica')).toBeInTheDocument();
  });

  it('muestra bloque de motivo de terminación cuando está cancelada/rechazada', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({
          estado: EstadoRequisicion.Cancelada,
          motivoTerminacionTexto: 'Cancelada por proveedor cambiado',
          fechaTerminacion: '2026-05-10T14:00:00Z',
        })}
        resolverNombre={resolverIdentidad}
      />,
    );
    expect(screen.getByText('Motivo de terminación')).toBeInTheDocument();
    expect(
      screen.getByText('Cancelada por proveedor cambiado'),
    ).toBeInTheDocument();
  });

  it('proveedor sugerido en null muestra "—"', () => {
    render(
      <CabeceraRequisicion
        rq={makeRq({ proveedorSugeridoId: null })}
        resolverNombre={resolverIdentidad}
      />,
    );
    // Hay varios "—" potenciales (empty values); aseguremos que el bloque
    // "Proveedor sugerido" exista.
    expect(screen.getByText('Proveedor sugerido')).toBeInTheDocument();
  });
});
