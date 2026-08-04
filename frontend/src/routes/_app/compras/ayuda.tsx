import { createFileRoute } from '@tanstack/react-router';
import { Ayuda } from '@/features/compras/pages/Ayuda';

/**
 * Ruta P11 — Ayuda del módulo Compras Requisiciones (doc 05 §13.7).
 * Sin <c>beforeLoad</c> de permisos: la ayuda es referencia pública
 * para cualquier usuario autenticado que use el módulo. La gate de
 * acceso al módulo en sí ya vive en el sidebar.
 */
export const Route = createFileRoute('/_app/compras/ayuda')({
  component: Ayuda,
});
