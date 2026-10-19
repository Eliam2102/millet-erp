import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoOrdenCompra, type OrdenCompraDetalleResponse, type RespuestaProveedorOc } from '../api/types';
import { RegistrarRespuestaProveedorSheet } from './RegistrarRespuestaProveedorSheet';
import { RespuestaProveedorPanel } from './RespuestaProveedorPanel';

const permiso = vi.hoisted(() => ({ value: true }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => permiso.value }));
vi.mock('@/features/catalogos/api', () => ({
  useUsuarios: () => ({ data: { items: [{ id: 'u-1', nombre: 'Ana Compras' }] } }),
  mapById: (items: { id: string }[] | undefined) => new Map((items ?? []).map((i) => [i.id, i])),
}));

vi.mock('@/components/erp/adjuntos/DocumentoAdjuntosSection', () => ({
  DocumentoAdjuntosSection: ({ base, tipo, permisos }: { base: string; tipo: string; permisos?: { baja?: string } }) => (
    <div data-testid="adjuntos" data-base={base} data-tipo={tipo} data-con-baja={String(permisos?.baja !== undefined)} />
  ),
}));

const LINEA = '0198a7c2-1111-7000-8000-000000000001';

function ocDePrueba(respuestas: RespuestaProveedorOc[] = []): OrdenCompraDetalleResponse {
  return {
    id: 'oc-1',
    folio: 'OC-MID2026-000987',
    proveedorRazonSocial: 'Vidrios del Norte',
    proveedorClave: 'P-1',
    estado: EstadoOrdenCompra.Autorizada,
    fechaEntregaEsperada: '2026-10-25',
    estadoComercialProveedor: respuestas.length ? 1 : 0,
    fechaCompromisoProveedor: respuestas.length ? '2026-10-22' : null,
    respuestasProveedor: respuestas,
    lineas: [
      { id: LINEA, posicion: 1, articuloNombre: 'Silicón', articuloClave: 'SIL', unidadMedida: 'PZA', cantidad: 10, cantidadRecibida: 0 },
    ],
  } as unknown as OrdenCompraDetalleResponse;
}

const respuesta = (numero: number, vigente: boolean): RespuestaProveedorOc => ({
  id: `r-${numero}`,
  numero,
  tipo: 1,
  fechaRespuesta: '2026-10-17',
  evidencia: `Correo folio P-88${numero}`,
  contactoProveedor: null,
  motivo: null,
  tieneDiferencias: false,
  registradaPorId: 'u-1',
  registradaEn: '2026-10-17T15:00:00Z',
  vigente,
  compromisos: [{ id: `c-${numero}`, lineaOrdenCompraId: LINEA, cantidad: 10, fechaPrometida: '2026-10-22' }],
});

function abrirSheet(onSubmit = vi.fn().mockResolvedValue(undefined)) {
  render(<RegistrarRespuestaProveedorSheet oc={ocDePrueba()} open onOpenChange={vi.fn()} onSubmit={onSubmit} />);
  return onSubmit;
}

describe('Respuesta del proveedor (F1-COM-11)', () => {
  beforeEach(() => {
    permiso.value = true;
  });

  it('confirmación envía un compromiso por línea pendiente con la cantidad pendiente', async () => {
    const submit = abrirSheet();
    fireEvent.change(screen.getByLabelText(/^Evidencia/), { target: { value: 'Correo P-881' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar respuesta' }));
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    expect(submit.mock.calls[0][0]).toMatchObject({
      tipo: 1,
      evidencia: 'Correo P-881',
      compromisos: [{ lineaOrdenCompraId: LINEA, cantidad: 10, fechaPrometida: '2026-10-25' }],
    });
  });

  it('avisa que la orden no cambia cuando se confirma menos del pendiente', () => {
    abrirSheet();
    fireEvent.change(screen.getByLabelText(/^Cantidad \(línea 1, parcialidad 1\)/), { target: { value: '8' } });
    expect(screen.getByRole('note')).toHaveTextContent('la orden no cambia; quedará marcada con diferencias');
  });

  it('agregar parcialidad envía dos compromisos de la misma línea', async () => {
    const submit = abrirSheet();
    fireEvent.change(screen.getByLabelText(/^Evidencia/), { target: { value: 'Correo' } });
    fireEvent.change(screen.getByLabelText(/^Cantidad \(línea 1, parcialidad 1\)/), { target: { value: '6' } });
    fireEvent.click(screen.getByRole('button', { name: /Agregar parcialidad/ }));
    fireEvent.change(screen.getByLabelText(/^Cantidad \(línea 1, parcialidad 2\)/), { target: { value: '4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar respuesta' }));
    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1));
    const { compromisos } = submit.mock.calls[0][0];
    expect(compromisos).toHaveLength(2);
    expect(compromisos.every((c: { lineaOrdenCompraId: string }) => c.lineaOrdenCompraId === LINEA)).toBe(true);
  });

  it('rechazo sin motivo muestra alerta y no envía', () => {
    const submit = abrirSheet();
    fireEvent.click(screen.getByLabelText('El proveedor rechaza'));
    fireEvent.change(screen.getByLabelText(/^Evidencia/), { target: { value: 'Correo' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar respuesta' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Indica el motivo del rechazo del proveedor.');
    expect(submit).not.toHaveBeenCalled();
  });

  it('el panel muestra estado, vigente, reemplazada y evidencia', () => {
    render(<RespuestaProveedorPanel oc={ocDePrueba([respuesta(2, true), respuesta(1, false)])} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('Confirmada por el proveedor')).toBeInTheDocument();
    expect(screen.getByText('Vigente')).toBeInTheDocument();
    expect(screen.getByText('Reemplazada')).toBeInTheDocument();
    expect(screen.getByText('Evidencia: Correo folio P-882')).toBeInTheDocument();
  });

  it('el panel monta la evidencia en archivo de cada respuesta con su base', () => {
    render(<RespuestaProveedorPanel oc={ocDePrueba([respuesta(1, true)])} />, { wrapper: createQueryWrapper() });
    const adjuntos = screen.getByTestId('adjuntos');
    expect(adjuntos).toHaveAttribute('data-base', '/api/v1/compras/ordenes/respuestas-proveedor/r-1');
    expect(adjuntos).toHaveAttribute('data-tipo', 'oc_respuesta_proveedor');
    // La evidencia es historial inmutable: sin permiso de baja.
    expect(adjuntos).toHaveAttribute('data-con-baja', 'false');
  });

  it('sin permiso no aparece el botón de registrar', () => {
    permiso.value = false;
    render(<RespuestaProveedorPanel oc={ocDePrueba()} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: 'Registrar respuesta del proveedor' })).not.toBeInTheDocument();
  });
});
