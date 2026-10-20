import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { SistemaDisenoPage } from './SistemaDisenoPage';
import catalogo from './catalogo.json';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { Route } from '@/routes/_app/sistema-diseno';
import { rutaPermitida } from '@/lib/nav';

afterEach(() => {
  cleanup();
  useAuthStore.getState().clearSession();
  vi.restoreAllMocks();
});

describe('Catálogo del sistema de diseño', () => {
  it('renderiza todas las secciones y los componentes sin solicitudes de red', async () => {
    useAuthStore.setState({ currentEmpresaId: 'empresa-demo', currentSucursalId: 'sucursal-demo' });
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    render(<SistemaDisenoPage />);
    const titulos = new Set(screen.getAllByRole('heading').map((h) => h.textContent));
    for (const nombre of [
      'Fundamentos',
      'Componentes',
      'Plantillas 5.1 y 5.2',
      'Reglas de contenido',
      ...catalogo.map((c) => c.nombre),
    ]) {
      expect(titulos.has(nombre), nombre).toBe(true);
    }
    expect(document.querySelectorAll('article')).toHaveLength(catalogo.length);
    expect(screen.getByText('Sucursal: [SUCURSAL FICTICIA]')).toBeInTheDocument();
    expect(screen.getAllByText(/import .* from/).length).toBe(catalogo.length);
    await Promise.resolve();
    expect(fetchSpy).not.toHaveBeenCalled();
  });
  it('permite filtrar, paginar y simular vacío, carga y recuperación de error', () => {
    render(<SistemaDisenoPage />);
    const plantilla = within(screen.getByRole('region', { name: 'Plantilla 5.1' }));
    expect(plantilla.getAllByRole('row')).toHaveLength(11);
    fireEvent.click(plantilla.getByRole('button', { name: 'Página siguiente' }));
    expect(plantilla.getByText('11–20 de 23')).toBeInTheDocument();
    fireEvent.change(plantilla.getByRole('textbox'), { target: { value: '999' } });
    expect(plantilla.getByText('Sin ejemplos')).toBeInTheDocument();
    fireEvent.click(plantilla.getByRole('button', { name: 'Limpiar filtros' }));
    fireEvent.change(plantilla.getByRole('combobox'), { target: { value: 'cargando' } });
    expect(plantilla.getByLabelText('Cargando tabla')).toBeInTheDocument();
    fireEvent.change(plantilla.getByRole('combobox'), { target: { value: 'error' } });
    fireEvent.click(plantilla.getByRole('button', { name: 'Reintentar' }));
    expect(plantilla.getAllByRole('row')).toHaveLength(11);
  });
  it('copia el código del ejemplo ejecutable', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    render(<SistemaDisenoPage />);
    fireEvent.click(screen.getAllByText('Código de uso e imports')[0]);
    fireEvent.click(screen.getAllByRole('button', { name: 'Copiar código' })[0]);
    expect(await screen.findByText('Código copiado')).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith(
      expect.stringContaining("from '@/components/erp/PageHeader'"),
    );
    vi.unstubAllGlobals();
  });
  it.each([
    { permisos: [] },
    { permisos: [P.ComprasOrdenesLeer] },
    { permisos: [P.AdminParametrosLeer] },
  ])('bloquea acceso directo sin permiso administrativo de edición (%j)', ({ permisos }) => {
    useAuthStore.setState({ permisos });
    const guard = Route.options.beforeLoad as () => void;
    expect(guard).toThrow();
    expect(rutaPermitida('/sistema-diseno', permisos)).toBe(false);
  });
  it('permite al administrador y vuelve a evaluar permisos revocados', () => {
    useAuthStore.setState({ permisos: [P.AdminParametrosEditar] });
    const guard = Route.options.beforeLoad as () => void;
    expect(guard).not.toThrow();
    expect(rutaPermitida('/sistema-diseno', [P.AdminParametrosEditar])).toBe(true);
    useAuthStore.setState({ permisos: [] });
    expect(guard).toThrow();
  });
});
