import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { DiferenciasFiscalesAwPage } from './DiferenciasFiscalesAwPage';
import type { FilaDiferenciaFiscal, ReporteDiferenciasFiscales } from '../api/diferencias-fiscales-aw';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => (
    <a href={params ? to.replace('$id', params.id) : to} {...rest}>
      {children}
    </a>
  ),
}));

const base = '*/api/v1/datos-maestros/diferencias-fiscales-aw';
const huella = 'a'.repeat(64);
const fila: FilaDiferenciaFiscal = {
  tipo: 'Cliente', tipoEtiqueta: 'Cliente', entidadId: 'c-1', referencia: '1001', nombre: 'CLIENTE DEMO',
  campo: 'RegimenFiscal', campoEtiqueta: 'Régimen fiscal', fuente: 'A+W', valorOrigen: '601', valorErp: '612',
  estado: 'Diferente', estadoEtiqueta: 'Diferente', huella, revisada: false, revisadaEn: null, revisadaPor: null, nota: null,
};
const reporte = (filas: FilaDiferenciaFiscal[], avisos: string[] = []): ReporteDiferenciasFiscales => ({
  titulo: 'Diferencias fiscales A+W vs ERP', generadoEn: '2026-10-19T12:00:00Z', filtrosAplicados: { Tipo: 'Todos' },
  columnas: [
    { clave: 'referencia', etiqueta: 'Clave A+W', tipo: 'texto' },
    { clave: 'campoEtiqueta', etiqueta: 'Campo', tipo: 'texto' },
    { clave: 'valorOrigen', etiqueta: 'Valor A+W o regla', tipo: 'texto' },
    { clave: 'valorErp', etiqueta: 'Valor en el ERP', tipo: 'texto' },
    { clave: 'estadoEtiqueta', etiqueta: 'Estado', tipo: 'texto' },
  ],
  filas, totales: null, avisos,
  resumen: { clientesComparados: 1, productosComparados: 0, pendientes: filas.length, revisadas: 0 },
});

function abrir(permisos: string[]) {
  useAuthStore.setState({ status: 'authenticated', accessToken: 't', permisos, expiresAt: new Date(Date.now() + 3_600_000) });
  mswServer.use(http.get(`${base}/corridas/ultima`, () => HttpResponse.json({ corrida: null, reporte: null, ultimaProgramada: null })));
  return render(<DiferenciasFiscalesAwPage />, { wrapper: createQueryWrapper() });
}
afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [] }));

describe('Diferencias fiscales A+W', () => {
  it('solo lee A+W al ejecutar y muestra ambos valores, avisos y el enlace para corregir', async () => {
    let lecturas = 0;
    mswServer.use(http.get(base, ({ request }) => {
      lecturas++;
      expect(new URL(request.url).searchParams.get('tipo')).toBe('Cliente');
      return HttpResponse.json(reporte([fila], ['No se compararon productos de prueba.']));
    }));
    abrir([PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer, PermisosCanonicos.DatosMaestrosClientesGestionar]);

    expect(await screen.findByText(/Aún no hay corridas programadas/)).toBeInTheDocument();
    expect(lecturas).toBe(0);
    fireEvent.change(screen.getByLabelText('Tipo'), { target: { value: 'Cliente' } });
    fireEvent.click(screen.getByRole('button', { name: /Ejecutar reporte/ }));

    const celda = await screen.findByText('612');
    const renglon = celda.closest('tr')!;
    expect(within(renglon).getByText('601')).toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('No se compararon productos de prueba.');
    expect(within(renglon).getByRole('link', { name: /Corregir Régimen fiscal de 1001/ })).toHaveAttribute('href', '/admin/datos-maestros/clientes/c-1');
    const marcar = within(renglon).getByRole('button', { name: /Marcar revisado/ });
    expect(marcar).toBeDisabled();
    expect(marcar).toHaveAttribute('title', 'Necesitas el permiso para revisar diferencias fiscales');
    expect(screen.getByRole('button', { name: /Ejecutar y guardar corrida/ })).toBeDisabled();
  });

  it('marca revisado con la huella vista e Idempotency-Key y vuelve a consultar', async () => {
    let filas = [fila];
    let enviado: unknown = null;
    mswServer.use(
      http.get(base, () => HttpResponse.json(reporte(filas))),
      http.post(`${base}/revisiones`, async ({ request }) => {
        expect(request.headers.get('Idempotency-Key')).toBeTruthy();
        enviado = await request.json();
        filas = [];
        return HttpResponse.json({ id: 'r-1', tipo: 'Cliente', entidadId: 'c-1', campo: 'RegimenFiscal', huella, revisadoEn: '2026-10-19T12:00:00Z', revisadoPor: 'demo', version: 1 });
      }),
    );
    abrir([PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer, PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwRevisar]);

    fireEvent.click(screen.getByRole('button', { name: /Ejecutar reporte/ }));
    const renglon = (await screen.findByText('612')).closest('tr')!;
    expect(within(renglon).getByText('Sin acceso para corregir')).toBeInTheDocument();
    fireEvent.click(within(renglon).getByRole('button', { name: /Marcar revisado/ }));

    await waitFor(() => expect(enviado).toEqual({ tipo: 'Cliente', entidadId: 'c-1', campo: 'RegimenFiscal', huella, nota: null }));
    expect(await screen.findByText('Sin datos para los filtros aplicados.')).toBeInTheDocument();
  });
});
