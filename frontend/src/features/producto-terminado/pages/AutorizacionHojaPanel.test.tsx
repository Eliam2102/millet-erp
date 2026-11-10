import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { AutorizacionHojaPanel } from './AutorizacionHojaPanel';
import type { HojaSalidaDto } from '../api/types';

const autorizar = vi.hoisted(() => ({ mutateAsync: vi.fn() }));
vi.mock('../api/hooks', () => ({ useAutorizarHojaSalida: () => ({ mutateAsync: autorizar.mutateAsync, isPending: false }) }));

const hoja = {
  id: 'h1', folio: 'HS-20261105-A1B2C', sucursalId: 'suc-1', destino: 'Obra Mérida norte', foranea: false, vehiculoId: null, operadorId: null, estado: 'Borrador',
  creadaAt: '2026-11-05T12:00:00Z', emitidaAt: null, cartaPorteFolio: null, pedidos: 1, piezas: 8, yaEmitida: false, albaranes: [],
  reglas: { sinFactura: ['10425068:8'], parciales: ['10425068:8'], excepcionFactura: null, parcial: null, excepcionFacturaVigente: false, parcialVigente: false },
} as HojaSalidaDto;

beforeEach(() => {
  vi.clearAllMocks();
  autorizar.mutateAsync.mockResolvedValue(hoja);
});

describe('AutorizacionHojaPanel', () => {
  it('muestra cada regla pendiente y solo los niveles que el usuario tiene', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoSalidaAprobarCredito] });
    render(<AutorizacionHojaPanel hoja={hoja} />);
    expect(screen.getByText('Sin factura timbrada del día o anterior')).toBeInTheDocument();
    expect(screen.getByText('Salen incompletos')).toBeInTheDocument();
    expect(screen.getAllByText('10425068 (8 piezas)')).toHaveLength(2);
    const opciones = Array.from(screen.getByLabelText('Nivel de autorización').querySelectorAll('option')).map((o) => o.textContent);
    expect(opciones).toEqual(['Seleccione una autorización', 'Crédito y Cobranza']);
  });

  it('un suplente pide por qué no autorizó el nivel anterior y lo manda al endpoint', async () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoSalidaAprobarPlaneacion] });
    render(<AutorizacionHojaPanel hoja={hoja} />);
    fireEvent.change(screen.getByLabelText('Nivel de autorización'), { target: { value: 'planeacion' } });
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'Cliente con obra detenida' } });
    fireEvent.change(screen.getByLabelText('Por qué no pudo autorizar el nivel anterior'), { target: { value: 'Director de Administración de viaje' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar autorización' }));
    await waitFor(() => expect(autorizar.mutateAsync).toHaveBeenCalledWith({
      id: 'h1', body: { nivel: 'planeacion', motivo: 'Cliente con obra detenida', suplencia: 'Director de Administración de viaje' },
    }));
  });

  it('sin nada que autorizar no se muestra', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoSalidaAprobarAdministracion] });
    const { container } = render(<AutorizacionHojaPanel hoja={{ ...hoja, reglas: { ...hoja.reglas!, sinFactura: [], parciales: [] } }} />);
    expect(container).toBeEmptyDOMElement();
  });
});
