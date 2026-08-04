import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConfiguracionPacForm } from '@/features/integraciones-fiscal/components/ConfiguracionPacForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

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
    const existing = {
      id: 'cfg-1',
      empresaId: 'e-1',
      proveedor: 1,
      baseUrl: 'https://test.fiscalapi.com',
      apiKeyConfigured: true,
      activo: true,
      ultimaRotacionAt: null,
      ultimaTestConexionAt: null,
      ultimaTestConexionExitosa: null,
      version: 1,
    } as never;

    render(<ConfiguracionPacForm empresaId="e-1" existing={existing} />, {
      wrapper: createQueryWrapper(),
    });

    const probar = screen.getByRole('button', { name: /probar conexión/i });
    // Config guardada + form pristine → habilitado.
    expect(probar).toBeEnabled();

    // Pegar una key nueva (sin guardar) → deshabilitado + hint de guardar.
    fireEvent.change(screen.getByPlaceholderText('••••'), {
      target: { value: 'sk_test_nueva' },
    });
    expect(probar).toBeDisabled();
    expect(screen.getByText(/guarda primero/i)).toBeInTheDocument();
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
