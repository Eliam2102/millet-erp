import { createFileRoute } from '@tanstack/react-router';
import { TableroEnviosAlmacenes } from '@/features/compras/integraciones/pages/TableroEnviosAlmacenes';

/** Ruta «Envíos a Almacenes» (I13.6, 86e3jvu83): mensajes de compras a A+W con su acuse y reintento. */
export const Route = createFileRoute('/_app/compras/integraciones/alm-compra')({
  component: TableroEnviosAlmacenes,
});
