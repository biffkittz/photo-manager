import { computed, type MaybeRefOrGetter, toValue } from 'vue'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'
import type { GallerySort, PhotoDetails } from '@/api/types'

export const photoKeys = {
  all: ['photos'] as const,
  list: (q: string, sort: GallerySort) => ['photos', 'list', q, sort] as const,
  detail: (id: string) => ['photos', 'detail', id] as const,
}

/** Infinite, cursor-paginated gallery listing. Vue Query cancels stale requests (via the AbortSignal) when the search changes. */
export function useGalleryQuery(
  search: MaybeRefOrGetter<string>,
  sort: MaybeRefOrGetter<GallerySort>,
) {
  return useInfiniteQuery({
    queryKey: computed(() => photoKeys.list(toValue(search), toValue(sort))),
    queryFn: ({ pageParam, signal }) =>
      api.listPhotos({ q: toValue(search), sort: toValue(sort), cursor: pageParam }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })
}

export function usePhotoQuery(id: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: computed(() => photoKeys.detail(toValue(id))),
    queryFn: ({ signal }) => api.photo(toValue(id), signal),
    // Poll while a regenerated description is pending.
    refetchInterval: (query) => (query.state.data?.descriptionState === 'Pending' ? 3000 : false),
  })
}

/** Edits a description with an optimistic update that is rolled back if the server rejects it. */
export function useUpdateDescription() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, description }: { id: string; description: string }) =>
      api.updateDescription(id, description),
    onMutate: async ({ id, description }) => {
      await client.cancelQueries({ queryKey: photoKeys.detail(id) })
      const previous = client.getQueryData<PhotoDetails>(photoKeys.detail(id))
      if (previous) {
        client.setQueryData<PhotoDetails>(photoKeys.detail(id), {
          ...previous,
          description,
          descriptionSource: 'User',
        })
      }

      return { previous }
    },
    onError: (_error, { id }, context) => {
      if (context?.previous) {
        client.setQueryData(photoKeys.detail(id), context.previous)
      }
    },
    onSettled: (_data, _error, { id }) => {
      void client.invalidateQueries({ queryKey: photoKeys.detail(id) })
      void client.invalidateQueries({ queryKey: ['photos', 'list'] })
    },
  })
}

export function useRegenerateDescription() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.regenerateDescription(id),
    onSuccess: (_data, id) => void client.invalidateQueries({ queryKey: photoKeys.detail(id) }),
  })
}
