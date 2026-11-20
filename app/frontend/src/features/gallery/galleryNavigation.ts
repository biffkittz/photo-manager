import type { InjectionKey, Ref } from 'vue'

/**
 * Provided by GalleryView, injected by PhotoViewer for previous/next navigation. provide/inject is used here
 * (sparingly) because the viewer is a nested route component, not a direct child that could take props.
 */
export interface GalleryNavigation {
  ids: Ref<string[]>
  loadMore: () => Promise<void>
}

export const galleryNavigationKey: InjectionKey<GalleryNavigation> = Symbol('galleryNavigation')
