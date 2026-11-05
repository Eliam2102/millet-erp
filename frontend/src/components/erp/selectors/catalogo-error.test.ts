import { expect, it } from 'vitest';
import { ApiError } from '@/lib/api';
import { mensajeErrorCatalogo } from './catalogo-error';

it('explica el permiso faltante sin mostrar la clave técnica del servidor', () => {
  const error = new ApiError({ type: 'about:blank', status: 403, title: 'Forbidden', detail: 'Falta catalogos.articulos.leer' }, 403);
  expect(mensajeErrorCatalogo(error)).toBe('Sin permiso para consultar este catálogo. Pídelo al administrador.');
});
it('los fallos de consulta conservan un mensaje de usuario', () => {
  expect(mensajeErrorCatalogo(new Error('TypeError: Failed to fetch'))).toBe('No se pudo cargar el catálogo. Reintenta más tarde.');
});
