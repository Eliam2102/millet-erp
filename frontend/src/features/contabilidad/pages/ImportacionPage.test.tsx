import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { conPermisos, limpiarAuth } from '@/features/centros-costo/pages/__fixtures__/configuracion-test-harness';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

import { ImportacionPage } from './ImportacionPage';
import { ALIAS_FIX, libroFormatoLaura, libroTresHojas, libroUnaHoja } from '../lib/__fixtures__/libro-fix';

const IMPORTAR = 'contabilidad.catalogo.importar';
const B = '*/api/v1/contabilidad/importaciones';

const err = (o: Record<string, unknown>) => ({
  fila: 3, columna: 'codigo_padre', codigo: 'CONTAB_IMPORT_PADRE_INEXISTENTE', severidad: 'Error',
  mensaje: 'El padre FIX-999 no existe.', sugerencia: 'Agrega la cuenta padre al archivo o corrige codigo_padre.', ...o,
});
const resumen = (o: Record<string, unknown> = {}) => ({
  leidas: 3, vacias: 0, crear: 2, actualizar: 0, sinCambios: 0, rechazadas: 1, errores: 1, advertencias: 1, ...o,
});
const PERFIL = {
  resumen: { filasLeidas: 3, invalidas: 1 }, distribuciones: {}, estructura: { huerfanas: 1, ciclos: 0 }, control: {},
  pendientesValidacion: { sinNaturaleza: 2, sinTipo: 2, pendientes: 2 }, nivelContable: {}, columnasSinMapeo: [],
  porCodigoError: [{ codigo: 'CONTAB_IMPORT_PADRE_INEXISTENTE', severidad: 'Error', conteo: 1, ejemplos: [{ fila: 3, columna: 'codigo_padre' }] }],
  queSeReabre: [{ hallazgo: 'Cuentas huérfanas', decision: 'P1/P15: archivo incompleto' }],
};
const VP_ERRORES = {
  resumen: resumen(), huella: 'h1', puedeAplicar: false,
  archivo: [err({ fila: 0, severidad: 'Advertencia', codigo: 'CONTAB_IMPORT_CAMPO_PENDIENTE', columna: 'naturaleza', mensaje: 'Sin naturaleza.', sugerencia: 'Pídela a Contabilidad.' })],
  filas: [
    { fila: 2, accion: 'Crear', errores: [] },
    { fila: 3, accion: 'Rechazar', errores: [err({})] },
  ],
};
const VP_OK = { resumen: resumen({ errores: 0, rechazadas: 0, advertencias: 0 }), huella: 'h2', puedeAplicar: true, archivo: [], filas: [{ fila: 2, accion: 'Crear', errores: [] }] };

const csv = () => new File(['codigo,nombre\nFIX-1,FIX Uno\n'], 'FIX-catalogo.csv', { type: 'text/csv' });

async function subir(archivo = csv()) {
  fireEvent.change(screen.getByLabelText('Archivo del catálogo'), { target: { files: [archivo] } });
}

beforeEach(() => {
  conPermisos([IMPORTAR]);
  vi.mocked(toast.success).mockClear();
});
afterEach(limpiarAuth);

describe('<ImportacionPage>', () => {
  it('sin permiso de importar: no renderiza el asistente', () => {
    conPermisos(['contabilidad.catalogo.leer']);
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('alert')).toHaveTextContent(/No tienes permiso para importar/);
    expect(screen.queryByLabelText('Archivo del catálogo')).not.toBeInTheDocument();
  });

  it('formato no soportado: error en pantalla, sin llamadas al API', async () => {
    const llamada = vi.fn();
    mswServer.use(http.post(`${B}/perfilado`, () => { llamada(); return HttpResponse.json(PERFIL); }));
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir(new File(['x'], 'FIX.pdf'));
    expect(await screen.findByText(/Formato no soportado/)).toBeInTheDocument();
    expect(llamada).not.toHaveBeenCalled();
  });

  it('paso 2 perfilado: errores agrupados por código y "qué se reabre"; vista previa con errores deshabilita Aplicar', async () => {
    mswServer.use(
      http.post(`${B}/perfilado`, () => HttpResponse.json(PERFIL)),
      http.post(`${B}/vista-previa`, () => HttpResponse.json(VP_ERRORES)),
    );
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir();

    expect(await screen.findByText('Errores y avisos encontrados')).toBeInTheDocument();
    expect(screen.getByText('Cuentas huérfanas', { exact: false })).toBeInTheDocument();
    // El reporte agrupa por hallazgo legible (no por código interno) y la sección se llama «Qué revisar».
    expect(screen.getByText('Cuenta padre inexistente')).toBeInTheDocument();
    expect(screen.getByText('Qué revisar')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/CONTAB_/);
    expect(screen.getByRole('button', { name: /Descargar reporte/ })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Continuar a la vista previa' }));
    expect(await screen.findByLabelText('Resumen de la vista previa')).toBeInTheDocument();
    // Acción por fila, error con sugerencia y hallazgo del archivo.
    expect(screen.getByText('Rechazada')).toBeInTheDocument();
    expect(screen.getByText(/El padre FIX-999 no existe/)).toBeInTheDocument();
    expect(screen.getByText(/Agrega la cuenta padre al archivo/)).toBeInTheDocument();
    expect(screen.getByText(/Pídela a Contabilidad/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Aplicar importación' })).toBeDisabled();
    // Lenguaje de usuario: título legible y nombre de columna legible; nunca el código interno ni la columna técnica.
    expect(screen.getByText(/Cuenta padre inexistente \(columna «Cuenta padre»\)/)).toBeInTheDocument();
    expect(screen.getByText(/Dato pendiente de validación \(columna «Naturaleza»\)/)).toBeInTheDocument();
    expect(screen.getAllByText(/Qué hacer:/).length).toBeGreaterThan(0);
    // (El texto de ejemplo del servidor puede nombrar columnas; lo que pinta la UI no.)
    expect(document.body.textContent).not.toMatch(/CONTAB_|\(codigo_padre\)|\(naturaleza\)|\(cuenta_control\)/);

    // Filtro "solo con errores": oculta la fila 2 (sin errores).
    expect(screen.getByText('Crear', { selector: 'div' })).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Solo con errores'));
    expect(screen.queryByText('Crear', { selector: 'div' })).not.toBeInTheDocument();
    expect(screen.getByText('Rechazada')).toBeInTheDocument();
  });

  it('aplicar: envía huella e Idempotency-Key y muestra el resultado; toast solo tras 201', async () => {
    let capturado: { key: string | null; body: Record<string, unknown> } | null = null;
    mswServer.use(
      http.post(`${B}/perfilado`, () => HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] })),
      http.post(`${B}/vista-previa`, () => HttpResponse.json(VP_OK)),
      http.post(B, async ({ request }) => {
        capturado = { key: request.headers.get('Idempotency-Key'), body: (await request.json()) as Record<string, unknown> };
        return HttpResponse.json({ idempotente: false, lote: { id: 'l', fuente: 'X', archivoNombre: 'FIX-catalogo.csv', huella: 'h2', totalFilas: 1, creadas: 1, actualizadas: 0, sinCambios: 0, aplicadoEn: '2026-10-02T00:00:00Z', aplicadoPor: null } }, { status: 201 });
      }),
    );
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir();
    fireEvent.click(await screen.findByRole('button', { name: 'Continuar a la vista previa' }));
    const aplicar = await screen.findByRole('button', { name: 'Aplicar importación' });
    expect(aplicar).toBeEnabled();
    expect(toast.success).not.toHaveBeenCalled();
    fireEvent.click(aplicar);

    expect(await screen.findByText('Importación aplicada.')).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith('Importación aplicada');
    expect(capturado!.key).toBeTruthy();
    expect(capturado!.body.huella).toBe('h2');
    expect(capturado!.body.csvBase64).toBeTruthy();
  });

  it('resultado idempotente: muestra "idempotente: ya aplicado"', async () => {
    mswServer.use(
      http.post(`${B}/perfilado`, () => HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] })),
      http.post(`${B}/vista-previa`, () => HttpResponse.json(VP_OK)),
      http.post(B, () => HttpResponse.json({ idempotente: true, lote: { id: 'l', fuente: 'X', archivoNombre: null, huella: 'h2', totalFilas: 1, creadas: 1, actualizadas: 0, sinCambios: 0, aplicadoEn: '2026-10-02T00:00:00Z', aplicadoPor: null } })),
    );
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir();
    fireEvent.click(await screen.findByRole('button', { name: 'Continuar a la vista previa' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Aplicar importación' }));
    expect(await screen.findByText(/Idempotente: ya aplicado/)).toBeInTheDocument();
  });

  it('422 FILAS_CON_ERRORES al aplicar: lista los errores y deja reintentar sin perder el archivo', async () => {
    mswServer.use(
      http.post(`${B}/perfilado`, () => HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] })),
      http.post(`${B}/vista-previa`, () => HttpResponse.json(VP_OK)),
      http.post(B, () => HttpResponse.json(
        { title: 'x', status: 422, code: 'CONTAB_IMPORT_FILAS_CON_ERRORES', detail: '1 errores.', errores: [err({ fila: 7, mensaje: 'Fila 7 inválida.' })] },
        { status: 422, headers: { 'Content-Type': 'application/problem+json' } })),
    );
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir();
    fireEvent.click(await screen.findByRole('button', { name: 'Continuar a la vista previa' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Aplicar importación' }));
    expect(await screen.findByText(/Fila 7: Fila 7 inválida\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Aplicar importación' })).toBeEnabled(); // sigue en el paso 3 para reintentar
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('fallo recuperable en el perfilado: mensaje y botón de reintento', async () => {
    let falla = true;
    mswServer.use(http.post(`${B}/perfilado`, () =>
      falla ? HttpResponse.json({ title: 'Boom', status: 500 }, { status: 500 }) : HttpResponse.json(PERFIL)));
    render(<ImportacionPage />, { wrapper: createQueryWrapper() });
    await subir();
    const reintentar = await screen.findByRole('button', { name: 'Reintentar análisis' });
    falla = false;
    fireEvent.click(reintentar);
    await waitFor(() => expect(screen.getByText('Errores y avisos encontrados')).toBeInTheDocument());
  });

  describe('.xlsx real: varias hojas, título y filas vacías', () => {
    const CONFIG = '*/api/v1/contabilidad/configuracion-formato';
    const alias = { importacion: { columnas: ALIAS_FIX } };

    it('3 hojas: muestra el selector con nombre y filas, preselecciona «Plan de cuentas-VILO», envía el encabezado de la fila 2 y muestra los números de fila del archivo', async () => {
      let perfilado: { columnas: string[]; filas: (string | null)[][] } | null = null;
      mswServer.use(
        http.get(CONFIG, () => HttpResponse.json(alias)),
        http.post(`${B}/perfilado`, async ({ request }) => {
          perfilado = (await request.json()) as typeof perfilado;
          return HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] });
        }),
        // El servidor numera con cabecera = 1: la fila 4 del archivo llega como fila 3.
        http.post(`${B}/vista-previa`, () => HttpResponse.json({
          ...VP_OK, filas: [{ fila: 3, accion: 'Rechazar', errores: [err({ fila: 3 })] }],
        })),
      );
      render(<ImportacionPage />, { wrapper: createQueryWrapper() });
      await subir(await libroTresHojas());

      const selector = (await screen.findByLabelText('Hoja del libro')) as HTMLSelectElement;
      expect(Array.from(selector.options).map((o) => o.textContent)).toEqual([
        'Plan de cuentas (5) — 2 filas con datos',
        'Catalogo — 2 filas con datos',
        'Plan de cuentas-VILO — 4 filas con datos',
      ]);
      expect(selector.value).toBe('2'); // preselección
      expect(perfilado).toBeNull(); // no se perfila hasta elegir

      fireEvent.click(screen.getByRole('button', { name: 'Analizar hoja' }));
      expect(await screen.findByText(/Encabezado detectado en la fila 2/)).toBeInTheDocument();
      expect(perfilado!.columnas.slice(0, 3)).toEqual(['Nivel Contable', 'Numero', 'Cuenta']); // el título no es la cabecera
      expect(perfilado!.filas[1].slice(0, 3)).toEqual(['1', '100', 'FIX Activo']);

      fireEvent.click(screen.getByRole('button', { name: 'Continuar a la vista previa' }));
      const fila = await screen.findByRole('cell', { name: '4' }); // fila 3 del servidor + 1 de desplazamiento
      expect(fila).toBeInTheDocument();
      expect(screen.queryByRole('cell', { name: '3' })).not.toBeInTheDocument();
    });

    it('formato de Contabilidad: preselecciona «Plan de cuentas», envía las filas de título sin código y las muestra como omitidas sin bloquear', async () => {
      let perfilado: { columnas: string[]; filas: (string | null)[][] } | null = null;
      mswServer.use(
        http.get(CONFIG, () => HttpResponse.json(alias)),
        http.post(`${B}/perfilado`, async ({ request }) => {
          perfilado = (await request.json()) as typeof perfilado;
          return HttpResponse.json({ ...PERFIL, resumen: { filasLeidas: 6, filasTitulo: 2, rubros: 1 }, porCodigoError: [], queSeReabre: [] });
        }),
        http.post(`${B}/vista-previa`, () => HttpResponse.json({
          resumen: resumen({ leidas: 6, vacias: 1, crear: 3, rechazadas: 0, errores: 0, advertencias: 2, omitidas: 2 }),
          huella: 'h3', puedeAplicar: true, archivo: [],
          filas: [
            { fila: 2, accion: 'Crear', errores: [] },
            { fila: 3, accion: 'Omitida', errores: [err({ fila: 3, columna: 'codigo', codigo: 'CONTAB_IMPORT_FILA_TITULO', severidad: 'Advertencia',
              mensaje: 'Fila de título de reporte (sin código): no es una cuenta y no se carga.', sugerencia: 'Nada: es un título de presentación del reporte.' })] },
            { fila: 4, accion: 'Crear', errores: [] },
          ],
        })),
      );
      render(<ImportacionPage />, { wrapper: createQueryWrapper() });
      await subir(await libroFormatoLaura());

      const selector = (await screen.findByLabelText('Hoja del libro')) as HTMLSelectElement;
      expect(selector.options[Number(selector.value)].textContent).toMatch(/^Plan de cuentas/);
      fireEvent.click(screen.getByRole('button', { name: 'Analizar hoja' }));
      expect(await screen.findByText(/Encabezado detectado en la fila 2/)).toBeInTheDocument();
      expect(perfilado!.columnas).toEqual(['Nivel Contable', 'Numero', 'Cuenta', 'Tipo', 'Naturaleza', 'Reporte', 'Nivel de cuenta SAT', 'Código agrupador SAT']);
      // La fila de título viaja con el código vacío (null): el servidor decide que es título y la omite.
      expect(perfilado!.filas[1].slice(1, 4)).toEqual([null, 'FIX Activo circulante', 'Título']);
      expect(screen.getByText('Títulos de reporte (no se cargan)')).toBeInTheDocument();

      fireEvent.click(screen.getByRole('button', { name: 'Continuar a la vista previa' }));
      expect(await screen.findByText('Omitida (título de reporte)')).toBeInTheDocument();
      expect(screen.getByText(/Fila de título de reporte \(sin código\)/)).toBeInTheDocument();
      expect(screen.getByText('Títulos omitidos').nextSibling).toHaveTextContent('2');
      expect(screen.getByRole('button', { name: 'Aplicar importación' })).toBeEnabled();
    });

    it('una sola hoja con encabezado en la fila 1: sin selector ni paso extra', async () => {
      let perfilado: { columnas: string[] } | null = null;
      mswServer.use(
        http.get(CONFIG, () => HttpResponse.json(alias)),
        http.post(`${B}/perfilado`, async ({ request }) => {
          perfilado = (await request.json()) as typeof perfilado;
          return HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] });
        }),
      );
      render(<ImportacionPage />, { wrapper: createQueryWrapper() });
      await subir(await libroUnaHoja());
      expect(await screen.findByText('Errores y avisos encontrados')).toBeInTheDocument();
      expect(screen.queryByLabelText('Hoja del libro')).not.toBeInTheDocument();
      expect(screen.queryByText(/Encabezado detectado/)).not.toBeInTheDocument();
      expect(perfilado!.columnas).toEqual(['codigo', 'nombre']);
    });

    it('sin encabezado reconocible: avisa que se usó la fila 1', async () => {
      mswServer.use(
        http.get(CONFIG, () => HttpResponse.json({ importacion: { columnas: {} } })),
        http.post(`${B}/perfilado`, () => HttpResponse.json({ ...PERFIL, porCodigoError: [], queSeReabre: [] })),
      );
      render(<ImportacionPage />, { wrapper: createQueryWrapper() });
      await subir(await libroUnaHoja());
      expect(await screen.findByText(/No se reconoció una fila de encabezado/)).toBeInTheDocument();
    });
  });
});
