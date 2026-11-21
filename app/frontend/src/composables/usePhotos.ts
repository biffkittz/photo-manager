import { computed, type MaybeRefOrGetter, toValue } from 'vue'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'
import type { GalleryPeriod, GallerySort, PhotoDetails } from '@/api/types'
import { albumKeys } from '@/composables/useAlbums'
import { localDate, utcOffsetMinutes } from '@/utils/calendar'

export const photoKeys = {
  all: ['photos'] as const,
  list: (q: string, sort: GallerySort, period: GalleryPeriod | null, offset: number) =>
    ['photos', 'list', q, sort, period?.year ?? null, period?.month ?? null, offset] as const,
  timeline: (q: string, offset: number) => ['photos', 'timeline', q, offset] as const,
  onThisDay: (date: string, offset: number) => ['photos', 'on-this-day', date, offset] as const,
  detail: (id: string) => ['photos', 'detail', id] as const,
}

/**
 * Infinite, cursor-paginated gallery listing, optionally narrowed to a year or month. Vue Query cancels stale
 * requests (via the AbortSignal) when the search changes.
 */
export function useGalleryQuery(
  search: MaybeRefOrGetter<string>,
  sort: MaybeRefOrGetter<GallerySort>,
  period: MaybeRefOrGetter<GalleryPeriod | null> = null,
) {
  const offset = utcOffsetMinutes()
  return useInfiniteQuery({
    queryKey: computed(() =>
      photoKeys.list(toValue(search), toValue(sort), toValue(period), offset),
    ),
    queryFn: ({ pageParam, signal }) =>
      api.listPhotos(
        {
          q: toValue(search),
          sort: toValue(sort),
          period: toValue(period),
          offset,
          cursor: pageParam,
        },
        signal,
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })
}

/** Photo counts per month (newest first) for the timeline; follows the search. */
export function useTimelineQuery(search: MaybeRefOrGetter<string>) {
  const offset = utcOffsetMinutes()
  return useQuery({
    queryKey: computed(() => photoKeys.timeline(toValue(search), offset)),
    queryFn: ({ signal }) => api.timeline(toValue(search), offset, signal),
    staleTime: 5 * 60_000,
  })
}

/** Photos taken on today's date in earlier years, by year. */
export function useOnThisDayQuery() {
  const date = localDate()
  const offset = utcOffsetMinutes()
  return useQuery({
    queryKey: photoKeys.onThisDay(date, offset),
    queryFn: ({ signal }) => api.onThisDay(date, offset, signal),
    staleTime: 60 * 60_000,
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
      void client.invalidateQueries({ queryKey: albumKeys.all })
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
