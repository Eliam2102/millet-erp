import { createFileRoute } from '@tanstack/react-router';
import { Ayuda } from '@/features/facturacion/pages/Ayuda';

/**
 * Ayuda del módulo Facturación — <c>/facturacion/ayuda</c> (FE-F10).
 * Sin guard de permiso fino (referencia/glosario); <c>/_app</c> ya cubrió
 * la autenticación.
 */
export const Route = createFileRoute('/_app/facturacion/ayuda')({
  component: Ayuda,
});
