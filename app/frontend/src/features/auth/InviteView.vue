<script setup lang="ts">
/**
 * Invite landing page (/invite#<token>). The token lives in the URL fragment, which browsers never send to a
 * server, so it appears in no request line, proxy or log; this page sends it once in a POST body and then
 * removes it from the address bar and history. The server remembers the opened invite (HttpOnly cookie) for the
 * Google sign-in that follows.
 */
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, urls } from '@/api/endpoints'
import { ApiError } from '@/api/http'
import type { InvitePreview } from '@/api/types'
import BaseSpinner from '@/components/BaseSpinner.vue'
import { formatDate } from '@/utils/format'

const router = useRouter()
const state = ref<'loading' | 'ready' | 'invalid' | 'error'>('loading')
const invite = ref<InvitePreview | null>(null)
const continueUrl = urls.googleLogin('/gallery')

onMounted(async () => {
  const token = window.location.hash.slice(1)
  if (window.location.hash) {
    await router.replace({ path: '/invite', hash: '' })
  }

  if (!token) {
    state.value = 'invalid'
    return
  }

  try {
    invite.value = await api.redeemInvite(token)
    state.value = 'ready'
  } catch (error) {
    state.value = error instanceof ApiError && error.status === 404 ? 'invalid' : 'error'
  }
})
</script>

<template>
  <section
    class="mx-auto mt-8 max-w-md rounded-lg border border-border bg-surface p-6 sm:mt-16 sm:p-8 text-center shadow-sm"
    aria-live="polite"
  >
    <h1 data-page-title tabindex="-1" class="text-2xl font-bold">Photo Manager</h1>

    <BaseSpinner v-if="state === 'loading'" class="mt-6" label="Opening your invitation" />

    <template v-else-if="state === 'ready' && invite">
      <p class="mt-4 text-lg">
        {{ invite.invitedBy }} invited <strong>{{ invite.name }}</strong> to the family photo
        library.
      </p>
      <p class="mt-2 text-sm text-muted">
        Sign in with your Google account to accept. From then on you sign in with that same Google
        account.
        <template v-if="invite.forSpecificAccount">
          Use the Google account that received this invitation.
        </template>
      </p>
      <!-- A full-page navigation: the sign-in goes through Google and comes back here. -->
      <a
        :href="continueUrl"
        class="mt-6 inline-flex w-full items-center justify-center rounded-md bg-brand px-4 py-2 font-medium pointer-coarse:py-3 text-on-brand hover:bg-brand-hover"
      >
        Continue with Google
      </a>
      <p class="mt-4 text-xs text-muted">
        This link works once, until {{ formatDate(invite.expiresAt) }}. No Google account? You can
        create one with any email address at accounts.google.com.
      </p>
    </template>

    <p
      v-else-if="state === 'invalid'"
      role="alert"
      class="mt-4 rounded-md bg-danger/10 p-3 text-sm text-danger"
    >
      This invite link does not work. It may have expired, been used already, or been replaced by a
      newer link. Ask the person who invited you for a new one.
    </p>

    <p v-else role="alert" class="mt-4 rounded-md bg-danger/10 p-3 text-sm text-danger">
      Photo Manager could not be reached. Check your connection and open the invite link again.
    </p>
  </section>
</template>
