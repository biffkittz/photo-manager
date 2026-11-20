import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'
import type { JobKind } from '@/api/types'

const jobKeys = { list: ['jobs'] as const, overview: ['jobs', 'overview'] as const }

export function useJobsQuery() {
  return useQuery({
    queryKey: jobKeys.list,
    queryFn: ({ signal }) => api.jobs(signal),
    // Refresh while anything is running so progress updates without a manual reload.
    refetchInterval: (query) =>
      query.state.data?.some((j) => j.status === 'Running' || j.status === 'Queued') ? 4000 : false,
  })
}

export function useOverviewQuery() {
  return useQuery({
    queryKey: jobKeys.overview,
    queryFn: ({ signal }) => api.overview(signal),
    refetchInterval: 30_000,
  })
}

export function useStartJob() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ kind, force = false }: { kind: JobKind; force?: boolean }) =>
      api.startJob(kind, force),
    onSettled: () => void client.invalidateQueries({ queryKey: jobKeys.list }),
  })
}

export function useCancelJob() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.cancelJob(id),
    onSettled: () => void client.invalidateQueries({ queryKey: jobKeys.list }),
  })
}
