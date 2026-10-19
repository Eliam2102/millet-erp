import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { createQueryWrapper } from '@/test/test-query-client';
import { CierreMesPage } from '@/features/almacen/pages/CierreMesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ApiError } from '@/lib/api/error';

const cierre = vi.hoisted(() => ({
  mutate: vi.fn(),
  revision: undefined as unknown,
  periodos: [] as unknown[],
}));
const toastMock = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));

vi.mock('sonner', () => ({ toast: toastMock }));
vi.mock('@/features/almacen/api/useSaldosCierreReportes', () => ({
  useEjecutarCierreMes: () => ({ mutate: cierre.mutate, isPending: false }),
  usePeriodosCierre: () => ({ data: cierre.periodos }),
  useRevisionPreviaCierre: () => ({ data: cierre.revision, isError: false }),
}));

function revisionOk(extra: object = {}) {
  return {
    anio: 2026,
    mes: 9,
    yaCerrado: false,
    periodoNoConcluido: false,
    puedeCerrar: true,
    checks: [
      { codigo: 'CIERRE_CONTEOS_PENDIENTES', titulo: 'Conteos del periodo aplicados o rechazados', ok: true, cantidad: 0, detalle: 'Sin conteos pendientes.' },
    ],
    ...extra,
  };
}

/** Abre el diálogo de confirmación y pulsa "Cerrar periodo". */
function confirmarCierre() {
  fireEvent.click(screen.getByRole('button', { name: /Ejecutar cierre/i }));
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar periodo' }));
}

beforeEach(() => {
  vi.clearAllMocks();
  cierre.revision = revisionOk();
  cierre.periodos = [];
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.cierre-mes.ejecutar'],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<CierreMesPage> — cierre y toasts', () => {
  it('propone el mes anterior por defecto', () => {
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    const hoy = new Date();
    const anterior = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
    confirmarCierre();
    expect(cierre.mutate.mock.calls[0][0].command).toEqual({
      anio: anterior.getFullYear(),
      mes: anterior.getMonth() + 1,
    });
  });

  it('éxito: toast con el periodo cerrado', () => {
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    confirmarCierre();
    const opciones = cierre.mutate.mock.calls[0][1];
    opciones.onSuccess({
      periodoId: 'p-1',
      anio: 2026,
      mes: 9,
      cerradoAt: '2026-10-14T10:00:00Z',
    });
    expect(toastMock.success).toHaveBeenCalledWith(
      'Cierre ejecutado',
      expect.objectContaining({
        description: expect.stringContaining('Periodo 2026-09 cerrado'),
      }),
    );
  });

  it('CIERRE_CONTEOS_PENDIENTES: toast "Periodo con pendientes" con el detalle', () => {
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    confirmarCierre();
    const opciones = cierre.mutate.mock.calls[0][1];
    opciones.onError(
      new ApiError(
        {
          type: 'about:blank',
          title: 'Cierre bloqueado',
          status: 422,
          code: 'CIERRE_CONTEOS_PENDIENTES',
          detail: '2 conteos pendientes en el periodo.',
        },
        422,
      ),
    );
    expect(toastMock.error).toHaveBeenCalledWith('Periodo con pendientes', {
      description: '2 conteos pendientes en el periodo.',
    });
  });
});

describe('<CierreMesPage> — revisión previa', () => {
  it('con un bloqueo deshabilita el primario y dice por qué', () => {
    cierre.revision = revisionOk({
      puedeCerrar: false,
      checks: [
        { codigo: 'CIERRE_CONTEOS_PENDIENTES', titulo: 'Conteos del periodo aplicados o rechazados', ok: false, cantidad: 2, detalle: '2 conteo(s) sin aplicar ni rechazar.' },
      ],
    });
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: /Ejecutar cierre/i })).toBeDisabled();
    expect(screen.getByText(/Resuelve 1 pendiente de la revisión previa/)).toBeInTheDocument();
    expect(screen.getByText('2 conteo(s) sin aplicar ni rechazar.')).toBeInTheDocument();
  });

  it('periodo ya cerrado o no concluido: deshabilitado con motivo', () => {
    cierre.revision = revisionOk({ yaCerrado: true, puedeCerrar: false });
    const { unmount } = render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: /Ejecutar cierre/i })).toBeDisabled();
    expect(screen.getByText(/ya está cerrado/)).toBeInTheDocument();
    unmount();
    cierre.revision = revisionOk({ periodoNoConcluido: true, puedeCerrar: false });
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/aún no termina/)).toBeInTheDocument();
  });
});

describe('<CierreMesPage> — smoke', () => {
  it('renderiza header + revisión previa + selector de periodo + botón ejecutar', () => {
    render(<CierreMesPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /Cierre de mes/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { name: 'Revisión previa' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Periodo a cerrar/i)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Ejecutar cierre/i }),
    ).toBeInTheDocument();
  });

  it('axe-core: cero violations', async () => {
    const { container } = render(<CierreMesPage />, {
      wrapper: createQueryWrapper(),
    });
    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
