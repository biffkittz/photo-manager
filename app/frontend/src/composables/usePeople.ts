import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'

const peopleKeys = { list: ['people'] as const }

/** Everyone who can use Photo Manager (People page, admins only). */
export function usePeopleQuery() {
  return useQuery({ queryKey: peopleKeys.list, queryFn: ({ signal }) => api.people(signal) })
}

export function useInviteMember() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ name, email }: { name: string; email: string | null }) =>
      api.inviteMember(name, email),
    onSettled: () => void client.invalidateQueries({ queryKey: peopleKeys.list }),
  })
}

export function useRenewInvite() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => api.renewInvite(id),
    onSettled: () => void client.invalidateQueries({ queryKey: peopleKeys.list }),
  })
}

export function useRemoveMember() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => api.removeMember(id),
    onSettled: () => void client.invalidateQueries({ queryKey: peopleKeys.list }),
  })
}
