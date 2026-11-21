<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useNotificationsStore } from '@/stores/notifications'

const auth = useAuthStore()
const { user, isAdmin, displayName } = storeToRefs(auth)
const router = useRouter()
const route = useRoute()
const notifications = useNotificationsStore()

async function signOut(): Promise<void> {
  try {
    await auth.logout()
  } catch {
    // The local session is cleared regardless; the server-side cookie may still be valid until it expires.
    notifications.notify(
      'error',
      'Could not reach the server to sign out. Close the browser to end the session.',
    )
  } finally {
    await router.push({ name: 'login' })
  }
}

// Phones: the links share the full width of their own row, below the name and Sign out (two rows instead of
// three), with little padding so five of them fit a 360px screen; touch screens get 44px tall tap targets.
const baseLinkClass =
  'block rounded-md px-1 py-2 text-center text-sm font-medium hover:bg-surface-muted hover:text-text sm:px-3 pointer-coarse:py-3'

/** A section's link stays highlighted on its sub-pages (an album, a photo). */
function linkClass(section: string): string {
  const current = route.path === section || route.path.startsWith(`${section}/`)
  return `${baseLinkClass} ${current ? 'bg-surface-muted text-text' : 'text-muted'}`
}
</script>

<template>
  <header class="border-b border-border bg-surface">
    <div
      class="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-x-3 gap-y-1 px-4 py-2 sm:py-3"
    >
      <RouterLink to="/gallery" class="py-1 text-lg font-semibold pointer-coarse:py-2"
        >Photo Manager</RouterLink
      >
      <nav v-if="user" aria-label="Main" class="order-last w-full sm:order-none sm:w-auto">
        <ul class="flex gap-1">
          <li class="flex-1 sm:flex-none">
            <RouterLink to="/gallery" :class="linkClass('/gallery')">Gallery</RouterLink>
          </li>
          <li class="flex-1 sm:flex-none">
            <RouterLink to="/albums" :class="linkClass('/albums')">Albums</RouterLink>
          </li>
          <li class="flex-1 sm:flex-none">
            <RouterLink to="/uploads" :class="linkClass('/uploads')">Uploads</RouterLink>
          </li>
          <li v-if="isAdmin" class="flex-1 sm:flex-none">
            <RouterLink to="/actions" :class="linkClass('/actions')">Actions</RouterLink>
          </li>
          <li v-if="isAdmin" class="flex-1 sm:flex-none">
            <RouterLink to="/people" :class="linkClass('/people')">People</RouterLink>
          </li>
        </ul>
      </nav>
      <div v-if="user" class="flex min-w-0 items-center gap-3">
        <img
          v-if="user.avatarUrl"
          :src="user.avatarUrl"
          alt=""
          class="size-8 rounded-full"
          referrerpolicy="no-referrer"
        />
        <span class="max-w-32 truncate text-sm sm:max-w-48" :title="displayName">{{
          displayName
        }}</span>
        <button
          type="button"
          class="shrink-0 text-sm text-muted underline hover:text-text pointer-coarse:px-1 pointer-coarse:py-3"
          @click="signOut"
        >
          Sign out
        </button>
      </div>
    </div>
  </header>
</template>
