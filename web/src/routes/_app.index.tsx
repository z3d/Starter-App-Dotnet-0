import { createFileRoute } from '@tanstack/react-router'
import { SamplePage } from '@/sample/SamplePage'

export const Route = createFileRoute('/_app/')({
  component: SamplePage,
})
