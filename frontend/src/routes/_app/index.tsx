import { createFileRoute } from '@tanstack/react-router';
import { PanelInicio } from '@/features/inicio/components/PanelInicio';

export const Route = createFileRoute('/_app/')({
  component: PanelInicio,
});
