import { computed, type MaybeRefOrGetter, toValue } from 'vue'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { api } from '@/api/endpoints'
import type { AlbumSummary } from '@/api/types'

export const albumKeys = {
  all: ['albums'] as const,
  list: ['albums', 'list'] as const,
  detail: (id: string) => ['albums', 'detail', id] as const,
  photos: (id: string) => ['albums', 'photos', id] as const,
}

/** Every album, most recently changed first. */
export function useAlbumsQuery() {
  return useQuery({ queryKey: albumKeys.list, queryFn: ({ signal }) => api.albums(signal) })
}

/** One album's name, count, dates and cover; nothing is fetched while `id` is empty. */
export function useAlbumQuery(id: MaybeRefOrGetter<string | null | undefined>) {
  return useQuery({
    queryKey: computed(() => albumKeys.detail(toValue(id) ?? '')),
    queryFn: ({ signal }) => api.album(toValue(id) ?? '', signal),
    enabled: computed(() => !!toValue(id)),
  })
}

/** An album's photos, oldest first, a page at a time. */
export function useAlbumPhotosQuery(id: MaybeRefOrGetter<string>) {
  return useInfiniteQuery({
    queryKey: computed(() => albumKeys.photos(toValue(id))),
    queryFn: ({ pageParam, signal }) => api.albumPhotos(toValue(id), pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextCursor,
  })
}

/** Every album change can alter the list, the album itself and its photos: all of it is refetched. */
function useAlbumMutation<TVariables>(
  mutationFn: (variables: TVariables) => Promise<AlbumSummary>,
) {
  const client = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (album) => client.setQueryData(albumKeys.detail(album.id), album),
    onSettled: () => void client.invalidateQueries({ queryKey: albumKeys.all }),
  })
}

export function useCreateAlbum() {
  return useAlbumMutation(({ name, photoIds }: { name: string; photoIds: string[] }) =>
    api.createAlbum(name, photoIds),
  )
}

export function useUpdateAlbum() {
  return useAlbumMutation(
    ({ id, ...changes }: { id: string; name?: string; coverPhotoId?: string }) =>
      api.updateAlbum(id, changes),
  )
}

export function useAddToAlbum() {
  return useAlbumMutation(({ id, photoIds }: { id: string; photoIds: string[] }) =>
    api.addToAlbum(id, photoIds),
  )
}

export function useRemoveFromAlbum() {
  return useAlbumMutation(({ id, photoIds }: { id: string; photoIds: string[] }) =>
    api.removeFromAlbum(id, photoIds),
  )
}

export function useDeleteAlbum() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.deleteAlbum(id),
    onSuccess: (_data, id) => client.removeQueries({ queryKey: albumKeys.detail(id) }),
    onSettled: () => void client.invalidateQueries({ queryKey: albumKeys.list }),
  })
}
