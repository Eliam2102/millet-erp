import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ErrorState } from '@/components/erp/feedback/ErrorState';

describe('<ErrorState>', () => {
  it('muestra title del problem cuando se pasa', () => {
    render(
      <ErrorState
        problem={{
          type: 'about:blank',
          title: 'Servicio no disponible',
          status: 503,
          detail: 'El backend no responde.',
          traceId: '00-abc-def-01',
        }}
      />,
    );

    expect(
      screen.getByText('Servicio no disponible'),
    ).toBeInTheDocument();
    expect(screen.getByText('El backend no responde.')).toBeInTheDocument();
    expect(screen.getByText(/00-abc-def-01/)).toBeInTheDocument();
  });

  it('cae a un mensaje genérico sin problem', () => {
    render(<ErrorState />);
    expect(screen.getByText('Algo salió mal')).toBeInTheDocument();
    expect(
      screen.getByText(/si persiste, avisa a soporte/i),
    ).toBeInTheDocument();
  });

  it('renderiza el botón Reintentar e invoca onRetry al click', () => {
    const onRetry = vi.fn();
    render(<ErrorState onRetry={onRetry} />);

    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('omite el botón Reintentar cuando NO se pasa onRetry', () => {
    render(<ErrorState />);
    expect(screen.queryByRole('button', { name: /reintentar/i })).not.toBeInTheDocument();
  });

  it('expone role="alert" para screen readers', () => {
    const { container } = render(<ErrorState />);
    expect(container.querySelector('[role="alert"]')).not.toBeNull();
  });
});

it('muestra la causa del servidor aunque la pantalla tenga su propio título', () => {
  render(<ErrorState title="No se pudo cargar la OC" problem={{ type: 'about:blank', title: 'No tienes acceso a esta sucursal', status: 403 }} onRetry={() => {}} />);
  expect(screen.getByText('No tienes acceso a esta sucursal')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
});


describe('UI-03 · mensaje único y código copiable', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('deduplica título y detalle iguales con espacios exteriores', () => {
    render(<ErrorState problem={{ type: 'about:blank', title: ' Servicio interrumpido ', detail: 'Servicio interrumpido  ', status: 503 }} />);
    expect(screen.getAllByText('Servicio interrumpido')).toHaveLength(1);
  });

  it('conserva detalle distinto con título personalizado', () => {
    render(<ErrorState title="No se pudo cargar la OC" problem={{ type: 'about:blank', title: 'Servicio interrumpido', detail: 'El servicio fiscal no responde. Contacta a soporte.', status: 503 }} />);
    expect(screen.getByText('No se pudo cargar la OC')).toBeInTheDocument();
    expect(screen.getByText('El servicio fiscal no responde. Contacta a soporte.')).toBeInTheDocument();
  });

  it('mantiene la causa del servidor bajo un título personalizado', () => {
    render(<ErrorState title="No se pudo cargar la OC" problem={{ type: 'about:blank', title: 'Servicio interrumpido', detail: 'Servicio interrumpido', status: 503 }} />);
    expect(screen.getAllByText('Servicio interrumpido')).toHaveLength(1);
  });

  it('mantiene fallbacks con mensajes vacíos', () => {
    render(<ErrorState title=" " problem={{ type: 'about:blank', title: ' ', detail: ' ', status: 500 }} />);
    expect(screen.getByText('Algo salió mal')).toBeInTheDocument();
    expect(screen.getByText(/si persiste, avisa a soporte/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copiar código' })).not.toBeInTheDocument();
  });

  it('omite traceId literal del mensaje genérico y copia sólo el valor del código', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    const mensaje = 'Ocurrió un error inesperado. Reporta el código de seguimiento traceId a soporte.';
    render(<ErrorState problem={{ type: 'about:blank', title: mensaje, detail: mensaje, status: 500, traceId: 'FIX-UI03-001' }} />);
    expect(screen.queryByText(/traceId/)).not.toBeInTheDocument();
    expect(screen.getAllByText('Ocurrió un error inesperado. Reporta el código de seguimiento a soporte.')).toHaveLength(1);
    expect(screen.getByText('Código: FIX-UI03-001')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Copiar código' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('FIX-UI03-001'));
    expect(await screen.findByRole('status')).toHaveTextContent('Código copiado.');
  });

  it('conserva código visible e informa el fallo del portapapeles', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } });
    render(<ErrorState problem={{ type: 'about:blank', title: 'Servicio interrumpido', status: 500, traceId: 'FIX-UI03-002' }} />);
    fireEvent.click(screen.getByRole('button', { name: 'Copiar código' }));
    expect(await screen.findByRole('status')).toHaveTextContent('No se pudo copiar. Selecciona el código y cópialo.');
    expect(screen.getByText('Código: FIX-UI03-002')).toBeInTheDocument();
  });

  it('preserva404 sin retry ni código de negocio4xx', () => {
    render(<ErrorState onRetry={vi.fn()} problem={{ type: 'about:blank', title: 'No existe el registro', detail: 'Detalle interno', status: 404, traceId: 'FIX-UI03-404' }} />);
    expect(screen.getByText('El registro no existe o ya no está disponible.')).toBeInTheDocument();
    expect(screen.queryByText('Detalle interno')).not.toBeInTheDocument();
    expect(screen.queryByText(/FIX-UI03-404/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});


it('preserva la causa distinta si el detalle coincide con el título personalizado', () => {
  render(<ErrorState title="No se pudo cargar la OC" problem={{ type: 'about:blank', title: 'Servicio fiscal interrumpido', detail: 'No se pudo cargar la OC', status: 503 }} />);
  expect(screen.getAllByText('No se pudo cargar la OC')).toHaveLength(1);
  expect(screen.getByText('Servicio fiscal interrumpido')).toBeInTheDocument();
});

it('informa si el navegador no ofrece portapapeles y mantiene el código visible', async () => {
  vi.stubGlobal('navigator', {});
  try {
    render(<ErrorState problem={{ type: 'about:blank', title: 'Servicio interrumpido', status: 500, traceId: 'FIX-UI03-003' }} />);
    fireEvent.click(screen.getByRole('button', { name: 'Copiar código' }));
    expect(await screen.findByRole('status')).toHaveTextContent('No se pudo copiar. Selecciona el código y cópialo.');
    expect(screen.getByText('Código: FIX-UI03-003')).toBeInTheDocument();
  } finally { vi.unstubAllGlobals(); }
});

it('no conserva una confirmación de copia al cambiar el código mostrado', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  vi.stubGlobal('navigator', { clipboard: { writeText } });
  try {
    const problem = { type: 'about:blank', title: 'Servicio interrumpido', status: 500, traceId: 'FIX-UI03-004' };
    const { rerender } = render(<ErrorState problem={problem} />);
    fireEvent.click(screen.getByRole('button', { name: 'Copiar código' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Código copiado.');
    rerender(<ErrorState problem={{ ...problem, traceId: 'FIX-UI03-005' }} />);
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
    fireEvent.click(screen.getByRole('button', { name: 'Copiar código' }));
    await waitFor(() => expect(writeText).toHaveBeenLastCalledWith('FIX-UI03-005'));
  } finally { vi.unstubAllGlobals(); }
});
