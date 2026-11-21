import type { ComputedRef, InjectionKey, Ref } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import type { AlbumSummary } from '@/api/types'

/**
 * The list the photo viewer steps through, provided by the page it opens over (the gallery or an album). The viewer
 * is a nested route component, not a direct child that could take props, hence provide/inject.
 */
export interface PhotoNavigation {
  ids: Ref<string[]>
  loadMore: () => Promise<void>
  /** The route of a photo in this list (previous/next). */
  photoRoute: (id: string) => RouteLocationRaw
  /** Where closing the viewer goes. */
  closeRoute: () => RouteLocationRaw
  /** Set when the list is an album: the viewer then offers to make the photo its cover. */
  album?: ComputedRef<AlbumSummary | undefined>
}

export const photoNavigationKey: InjectionKey<PhotoNavigation> = Symbol('photoNavigation')
