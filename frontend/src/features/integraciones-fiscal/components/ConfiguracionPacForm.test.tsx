import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { createQueryWrapper } from '@/test/test-query-client';
import { mswServer } from '@/test/mocks/server';
import { ConfiguracionPacForm } from '@/features/integraciones-fiscal/components/ConfiguracionPacForm';
import type { ConfiguracionPacResponse } from '@/features/integraciones-fiscal/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

const existing = {
  id: 'cfg-1',
  empresaId: 'e-1',
  proveedor: 1,
  proveedorNombre: 'FiscalAPI',
  baseUrl: 'https://test.fiscalapi.com',
  apiKey: '••••',
  apiKeyConfigured: true,
  activo: true,
  ultimaRotacionAt: null,
  ultimaTestConexionAt: null,
  ultimaTestConexionExitosa: null,
  emisorSandbox: null,
  receptorSandbox: null,
  csdConfigurado: true,
  csdActualizadoAt: '2026-09-29T12:00:00Z',
  csdNotBefore: '2025-10-01T00:00:00Z',
  csdNotAfter: '2027-10-01T00:00:00Z',
  csdEstado: 'Vigente',
  createdAt: '2026-09-29T12:00:00Z',
  updatedAt: '2026-09-29T12:00:00Z',
  version: 1,
} satisfies ConfiguracionPacResponse;

/**
 * Regresión FAC-DET-PR5: el switch "Modo sandbox" debe REFLEJARSE en el
 * input de Base URL (register + Controller paralelos sobre el mismo name
 * dejaban el DOM sin actualizar — la prueba de conexión salía contra live
 * con credenciales sk_test).
 */

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.IntegracionesFiscalAdministrar],
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

describe('<ConfiguracionPacForm> — modo sandbox', () => {
  it('el toggle alterna la Base URL visible entre live y test', () => {
    render(<ConfiguracionPacForm empresaId="e-1" existing={null} />, {
      wrapper: createQueryWrapper(),
    });

    const baseUrl = screen.getByLabelText('Base URL') as HTMLInputElement;
    const toggle = screen.getByRole('checkbox', { name: /modo sandbox/i });

    expect(baseUrl.value).toBe('https://live.fiscalapi.com');

    fireEvent.click(toggle);
    expect(baseUrl.value).toBe('https://test.fiscalapi.com');

    fireEvent.click(toggle);
    expect(baseUrl.value).toBe('https://live.fiscalapi.com');
  });

  it('Probar conexión se deshabilita con cambios sin guardar (usa la config persistida)', () => {
    render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    const probar = screen.getByRole('button', { name: /probar conexión/i });
    // Config guardada + form pristine → habilitado.
    expect(probar).toBeEnabled();

    // Pegar una key nueva (sin guardar) → deshabilitado + hint de guardar.
    fireEvent.change(screen.getByLabelText(/^API Key/i), {
      target: { value: 'sk_test_nueva' },
    });
    expect(probar).toBeDisabled();
    expect(screen.getByText(/guarda primero/i)).toBeInTheDocument();
  });
});

describe('<ConfiguracionPacForm> — secretos, CSD y permisos', () => {
  it('no rehidrata secretos existentes y muestra solo estados enmascarados', () => {
    render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    const apiKey = screen.getByLabelText(/^API Key/i) as HTMLInputElement;
    const password = screen.getByLabelText('Password de la llave') as HTMLInputElement;
    expect(apiKey).toHaveValue('');
    expect(apiKey).toHaveAttribute('placeholder', '••••');
    expect(password).toHaveValue('');
    expect(password).toHaveAttribute('placeholder', '••••');
    expect(screen.getByText(/CSD configurado y validado/i)).toBeInTheDocument();
    expect(screen.getByText('Vigente')).toBeInTheDocument();
    expect(screen.getByText(/vence:/i)).toBeInTheDocument();
    expect((screen.getByLabelText('Certificado (.cer)') as HTMLInputElement).files).toHaveLength(0);
    expect((screen.getByLabelText('Llave privada (.key)') as HTMLInputElement).files).toHaveLength(0);
  });

  it.each([
    ['ProximoAVencer', 'Próximo a vencer'],
    ['Vencido', 'Vencido'],
  ])('muestra el badge seguro para %s', (csdEstado, etiqueta) => {
    render(
      <ConfiguracionPacForm
        empresaId="e-1"
        existing={{ ...existing, csdEstado: csdEstado as ConfiguracionPacResponse['csdEstado'] }}
      />,
      { wrapper: createQueryWrapper() },
    );

    expect(screen.getByText(etiqueta)).toBeInTheDocument();
  });

  it('envia el CSD completo una sola vez y limpia los drafts tras guardar', async () => {
    const bodies: unknown[] = [];
    mswServer.use(
      http.put('*/api/v1/integraciones/fiscal/configuracion/e-1/1', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json(existing);
      }),
    );

    render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    const cer = new File(['cer'], 'test.cer');
    const key = new File(['key'], 'test.key');
    Object.defineProperty(cer, 'arrayBuffer', {
      value: async () => new TextEncoder().encode('cer').buffer,
    });
    Object.defineProperty(key, 'arrayBuffer', {
      value: async () => new TextEncoder().encode('key').buffer,
    });
    await act(async () => {
      fireEvent.change(screen.getByLabelText('Certificado (.cer)'), { target: { files: [cer] } });
      fireEvent.change(screen.getByLabelText('Llave privada (.key)'), { target: { files: [key] } });
      await Promise.resolve();
    });
    fireEvent.change(screen.getByLabelText('Password de la llave'), { target: { value: 'secreto-temporal' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      csd: { certificadoBase64: 'Y2Vy', llavePrivadaBase64: 'a2V5', password: 'secreto-temporal' },
    });
    await waitFor(() => expect(screen.getByLabelText('Password de la llave')).toHaveValue(''));

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]).toMatchObject({ csd: null });
  });

  it('muestra un error util sin reflejar el detalle sensible del servidor', async () => {
    mswServer.use(
      http.put('*/api/v1/integraciones/fiscal/configuracion/e-1/1', () =>
        HttpResponse.json(
          {
            title: 'No fue posible guardar la configuración.',
            detail: 'El payload sensible del proveedor no debe reflejarse.',
            status: 400,
            traceId: 'trace-seguro',
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('No fue posible guardar la configuración.', {
        description: 'Código: trace-seguro',
      }),
    );
    expect(toast.error).not.toHaveBeenCalledWith(
      expect.stringContaining('payload sensible'),
      expect.anything(),
    );
  });

  it('sin permiso de administrar deja el formulario en solo lectura', () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.IntegracionesFiscalLeer] });
    const { container } = render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    expect(container.querySelector('fieldset')).toBeDisabled();
    expect(screen.getByText(/Solo lectura/i)).toHaveTextContent('integraciones.fiscal.administrar');
  });
});

describe('<ConfiguracionPacForm> — identidades de prueba', () => {
  it('la sección solo aparece en modo sandbox y pre-llena la persona EKU', () => {
    render(<ConfiguracionPacForm empresaId="e-1" existing={null} />, {
      wrapper: createQueryWrapper(),
    });

    // En live no existe la sección.
    expect(screen.queryByText('Identidades de prueba')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: /modo sandbox/i }));
    expect(screen.getByText('Identidades de prueba')).toBeInTheDocument();

    // Habilitar sustitución pre-llena el emisor con la persona EKU.
    fireEvent.click(
      screen.getByRole('checkbox', { name: /sustituir emisor\/receptor/i }),
    );
    expect(
      (screen.getByLabelText('RFC') as HTMLInputElement).value,
    ).toBe('EKU9003173C9');

    // Receptor usa la misma identidad por default (sin segundo cuarteto).
    expect(
      screen.getByRole('checkbox', { name: /usar la misma identidad/i }),
    ).toBeChecked();
    expect(screen.queryByText('Receptor de prueba')).not.toBeInTheDocument();

    // Desmarcar "misma identidad" muestra los campos del receptor.
    fireEvent.click(
      screen.getByRole('checkbox', { name: /usar la misma identidad/i }),
    );
    expect(screen.getByText('Receptor de prueba')).toBeInTheDocument();
  });

  it('salir de modo sandbox oculta y limpia las identidades', () => {
    render(<ConfiguracionPacForm empresaId="e-1" existing={null} />, {
      wrapper: createQueryWrapper(),
    });

    const sandbox = screen.getByRole('checkbox', { name: /modo sandbox/i });
    fireEvent.click(sandbox);
    fireEvent.click(
      screen.getByRole('checkbox', { name: /sustituir emisor\/receptor/i }),
    );
    expect(screen.getByText('Emisor de prueba')).toBeInTheDocument();

    fireEvent.click(sandbox); // de regreso a live
    expect(screen.queryByText('Identidades de prueba')).not.toBeInTheDocument();

    // Al volver a sandbox el checkbox quedó apagado (se limpió).
    fireEvent.click(sandbox);
    expect(
      screen.getByRole('checkbox', { name: /sustituir emisor\/receptor/i }),
    ).not.toBeChecked();
  });
});
