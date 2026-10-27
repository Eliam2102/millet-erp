import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AvisoCobrarEnCaja, CobrarEnCajaLink } from './CobrarEnCajaLink';
const navigate = vi.hoisted(() => vi.fn());
vi.mock('@tanstack/react-router', () => ({ useNavigate: () => navigate }));
const estado = vi.hoisted(() => ({ permiso: true, abierta: true, anterior: false, total: 116, id: 'f-1' }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => estado.permiso }));
const sesionOpts = vi.hoisted(() => vi.fn());
vi.mock('../api/useCajaSesiones', () => ({
  useSesionActual: (opts?: { enabled?: boolean }) =>
    sesionOpts(opts) ?? {
      data: {
        sesion: { estado: estado.abierta ? 'Abierta' : 'Cerrada' },
        diaAnteriorPendiente: estado.anterior,
      },
    },
  useComprobantesCobrables: () => ({ data: [{ comprobanteId: estado.id, total: estado.total }] }),
}));
beforeEach(() => Object.assign(estado, { permiso: true, abierta: true, anterior: false, total: 116, id: 'f-1' }));
it('navega con el router a Mi caja conservando comprobante y folio (sin recargar)', () => {
  render(<CobrarEnCajaLink comprobanteId="f-1" folio="FA 20" />);
  fireEvent.click(screen.getByRole('button', { name: 'Cobrar en caja' }));
  expect(navigate).toHaveBeenCalledWith({ href: '/facturacion/caja?cobrar=f-1&folio=FA+20' });
});
// `id` = el comprobante no está entre los cobrables (PPD, USD, ya cobrado o sin timbrar).
it.each(['permiso', 'abierta', 'anterior', 'total', 'id'] as const)('oculta el botón si no cumple %s', (campo) => {
  if (campo === 'id') estado.id = 'otro';
  else if (campo === 'total') estado.total = 0;
  else estado[campo] = campo === 'anterior';
  render(<CobrarEnCajaLink comprobanteId="f-1" folio="FA 20" />);
  expect(screen.queryByRole('button', { name: 'Cobrar en caja' })).not.toBeInTheDocument();
});
it('sin permiso de caja no consulta la sesión (evita el 403)', () => {
  estado.permiso = false;
  render(<CobrarEnCajaLink comprobanteId="f-1" folio="FA 20" />);
  expect(sesionOpts).toHaveBeenLastCalledWith({ enabled: false });
});
it('aviso tras emitir: muestra el folio y navega a Mi caja con el router', () => {
  render(<AvisoCobrarEnCaja comprobanteId="f-1" folio="FA 20" />);
  expect(screen.getByRole('status')).toHaveTextContent('Factura FA 20 timbrada');
  fireEvent.click(screen.getByRole('button', { name: 'Cobrar en caja' }));
  expect(navigate).toHaveBeenCalledWith({ href: '/facturacion/caja?cobrar=f-1&folio=FA+20' });
});
it('aviso tras emitir: no se muestra si el cobro no aplica', () => {
  estado.abierta = false;
  const { container } = render(<AvisoCobrarEnCaja comprobanteId="f-1" folio="FA 20" />);
  expect(container).toBeEmptyDOMElement();
});
