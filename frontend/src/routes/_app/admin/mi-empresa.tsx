import { createFileRoute } from '@tanstack/react-router';
import { MiEmpresaPage } from '@/modules/administracion/components/MiEmpresaPage';

export const Route = createFileRoute('/_app/admin/mi-empresa')({
  component: MiEmpresaPage,
});
