import 'vue-router'
import type { RouteLocationRaw } from 'vue-router'

export interface Breadcrumb {
  label: string
  to?: RouteLocationRaw
}

declare module 'vue-router' {
  interface RouteMeta {
    /** Document title segment. */
    title?: string
    /** Reachable without signing in. */
    public?: boolean
    /** Only for the Admin role (the API enforces this too; the guard is for UX). */
    requiresAdmin?: boolean
    breadcrumb?: Breadcrumb[]
  }
}
