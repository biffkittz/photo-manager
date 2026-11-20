<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useNotificationsStore } from '@/stores/notifications'

const auth = useAuthStore()
const { user, isAdmin, displayName } = storeToRefs(auth)
const router = useRouter()
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

const linkClass =
  'rounded-md px-3 py-2 text-sm font-medium text-muted hover:bg-surface-muted hover:text-text'
const activeClass = 'bg-surface-muted !text-text'
</script>

<template>
  <header class="border-b border-border bg-surface">
    <div class="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3 px-4 py-3">
      <RouterLink to="/gallery" class="text-lg font-semibold">Photo Manager</RouterLink>
      <nav v-if="user" aria-label="Main">
        <ul class="flex gap-1">
          <li>
            <RouterLink to="/gallery" :class="linkClass" :active-class="activeClass"
              >Gallery</RouterLink
            >
          </li>
          <li>
            <RouterLink to="/uploads" :class="linkClass" :active-class="activeClass"
              >Uploads</RouterLink
            >
          </li>
          <li v-if="isAdmin">
            <RouterLink to="/actions" :class="linkClass" :active-class="activeClass"
              >Actions</RouterLink
            >
          </li>
        </ul>
      </nav>
      <div v-if="user" class="flex items-center gap-3">
        <img
          v-if="user.avatarUrl"
          :src="user.avatarUrl"
          alt=""
          class="size-8 rounded-full"
          referrerpolicy="no-referrer"
        />
        <span class="text-sm">{{ displayName }}</span>
        <button type="button" class="text-sm text-muted underline hover:text-text" @click="signOut">
          Sign out
        </button>
      </div>
    </div>
  </header>
</template>
