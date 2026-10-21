import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { IncidenciasPanel } from './IncidenciasPanel';
import type { ExpedienteDetalle, Incidencia } from '../api/types';
const mocks = vi.hoisted(() => ({ gestionar: true, guardar: vi.fn() }));
vi.mock('@/lib/auth/useHasPermission', () => ({
  useHasPermission: (p: string) => p.endsWith('incidencias.gestionar') && mocks.gestionar,
}));
vi.mock('../api/hooks', () => ({
  CE_BASE: '/ce',
  useAccionExpediente: () => ({ mutateAsync: mocks.guardar, isPending: false }),
  useContenedores: () => ({}),
}));
vi.mock('@/components/erp/adjuntos/api/hooks', () => ({
  useAdjuntos: () => ({ data: [] }),
  useSubirAdjunto: () => ({}),
  useEnlaceAdjunto: () => ({}),
}));
const incidencia: Incidencia = {
  id: 'inc-1',
  tipo: 5,
  estado: 1,
  fecha: '2026-10-18',
  responsableUsuarioId: 'u',
  responsableNombre: 'Operador DEMO',
  motivo: 'Daño al descargar',
  descripcion: null,
  participanteId: null,
  documentosIds: [],
  evidenciasIds: [],
  accionCorrectiva: null,
  efectoPosterior: null,
  hitoClave: 'entregado',
  contenedorId: null,
  partidaId: null,
  pedimentoAnterior: null,
  pedimentoNuevo: null,
  motivoCancelacion: null,
  antiguedadDias: 2,
  historial: [
    { estado: 1, actorId: 'u', fecha: '2026-10-18T12:00:00Z', motivo: 'Daño al descargar' },
  ],
};
const data = {
  expediente: { id: 'ce', estado: 1, tipo: 1, version: 1, responsableNombre: 'Operador DEMO' },
  responsableUsuarioId: 'u',
  incidencias: [incidencia],
  participantes: [],
  hitos: [],
  documentos: [],
  requisitos: [],
} as unknown as ExpedienteDetalle;
beforeEach(() => {
  mocks.gestionar = true;
  mocks.guardar.mockReset().mockResolvedValue({});
});
describe('Incidencias del expediente', () => {
  it('muestra bloqueo, historial y antigüedad', () => {
    render(<IncidenciasPanel data={data} />);
    expect(screen.getByRole('note')).toHaveTextContent('cierre físico, documental y financiero');
    expect(screen.getByText(/Operador DEMO · 2 días/)).toBeInTheDocument();
    expect(screen.getByText('Historial y referencias')).toBeInTheDocument();
  });
  it('sin permiso conserva consulta y oculta mutaciones', () => {
    mocks.gestionar = false;
    render(<IncidenciasPanel data={data} />);
    expect(screen.queryByRole('button', { name: 'Registrar incidencia' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Atender o resolver' })).not.toBeInTheDocument();
  });
  it('cancelar por error envía motivo e incidencia seleccionada', async () => {
    render(<IncidenciasPanel data={data} />);
    fireEvent.click(screen.getByRole('button', { name: 'Atender o resolver' }));
    expect(screen.queryByRole('option', { name: 'Resolver' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Acción *'), { target: { value: '4' } });
    fireEvent.change(screen.getByLabelText('Motivo de cancelación *'), {
      target: { value: 'Registro duplicado' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar incidencia' }));
    await waitFor(() =>
      expect(mocks.guardar).toHaveBeenCalledWith(
        expect.objectContaining({
          accion: 'incidencias/inc-1/estado',
          body: expect.objectContaining({ estado: 4, motivo: 'Registro duplicado' }),
        }),
      ),
    );
  });
  it('resolver exige acción y presenta nueva evidencia del pedimento', () => {
    render(<IncidenciasPanel data={{ ...data, incidencias: [{ ...incidencia, tipo: 2, estado: 2 }] }} />);
    fireEvent.click(screen.getByRole('button', { name: 'Atender o resolver' }));
    fireEvent.change(screen.getByLabelText('Acción *'), { target: { value: '3' } });
    expect(screen.getByLabelText('Acción tomada *')).toBeRequired();
    expect(screen.getByLabelText('Nuevo número de pedimento *')).toBeRequired();
    expect(screen.getByLabelText('Archivo del nuevo pedimento *')).toBeRequired();
  });
});
